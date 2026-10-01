using System.Text.RegularExpressions;
using HistorialAcademico.Core.Horarios;
using Microsoft.Playwright;

namespace HistorialAcademico.Banner;

/// <summary>
/// Automatiza Banner Self-Service. No guarda ni recibe credenciales: el usuario inicia sesión a mano
/// en un Chromium visible y solo se persiste el estado de la sesión (.auth/banner.json).
/// </summary>
public class BannerClient
{
    /// <summary>Tiempo máximo para que una página de Banner empiece a mostrarse (DOMContentLoaded).</summary>
    private int TiempoNavegacionMs => _opciones.TiempoNavegacionSegundos * 1000;
    /// <summary>Espera, como máximo, a que la red quede tranquila; si no queda (anuncios, rastreadores) se sigue igual.</summary>
    private const int TiempoRedQuietaMs = 15_000;

    private static readonly Regex EnlaceHistorico = new(
        @"hist[oó]rico\s+acad[eé]mico|academic\s+(history|transcript)|expediente\s+acad[eé]mico",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly BannerOptions _opciones;

    public BannerClient(BannerOptions opciones) => _opciones = opciones;

    public virtual bool TieneSesionGuardada => File.Exists(_opciones.RutaSesion);

    // ── Iniciar sesión ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Abre Chromium visible, navega a Banner:BaseUrl y espera a que el usuario inicie sesión.
    /// Al detectar la sesión activa guarda el estado en .auth/banner.json.
    /// </summary>
    public virtual async Task IniciarSesionAsync(CancellationToken ct = default)
    {
        try { await IniciarSesionInternoAsync(ct); }
        catch (Exception ex) when (EsFalloDeNavegador(ex)) { throw ErrorDeNavegador(ex); }
    }

    private async Task IniciarSesionInternoAsync(CancellationToken ct)
    {
        var url = ValidarUrl();
        using var pw = await Playwright.CreateAsync();
        await using var browser = await pw.Chromium.LaunchAsync(new() { Headless = false });
        var context = await browser.NewContextAsync();
        var page = await context.NewPageAsync();

        // Si la página tarda en cargar no importa: la ventana sigue abierta y el usuario puede iniciar sesión igual.
        await IrAsync(page, url);

        var limite = DateTime.UtcNow.AddSeconds(_opciones.TiempoEsperaLoginSegundos);
        while (!EsSesionActiva(context, url))
        {
            ct.ThrowIfCancellationRequested();
            if (DateTime.UtcNow > limite)
                throw new BannerException("Se agotó el tiempo esperando el inicio de sesión en Banner.");
            if (page.IsClosed && context.Pages.Count == 0)
                throw new BannerException("Se cerró la ventana de Banner antes de iniciar sesión.");
            await Task.Delay(1000, ct);
        }

        // Deja que la página termine de cargar antes de guardar la sesión.
        await EsperarRedQuietaAsync(ActivaPagina(context));
        Directory.CreateDirectory(Path.GetDirectoryName(_opciones.RutaSesion)!);
        await context.StorageStateAsync(new() { Path = _opciones.RutaSesion });
    }

    // ── Capturar el histórico ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Usa la sesión guardada, entra al Histórico Académico y guarda el HTML en samples/historico.html.
    /// Devuelve la ruta del archivo. Lanza <see cref="BannerSesionExpiradaException"/> si hay que volver a iniciar sesión
    /// y <see cref="BannerException"/> (con un mensaje en español) para cualquier otro fallo, incluidos los del navegador.
    /// </summary>
    public virtual async Task<string> CapturarHistoricoAsync(bool visible = false, CancellationToken ct = default)
    {
        try { return await CapturarInternoAsync(visible, ct); }
        catch (Exception ex) when (EsFalloDeNavegador(ex)) { throw ErrorDeNavegador(ex); }
    }

    private async Task<string> CapturarInternoAsync(bool visible, CancellationToken ct)
    {
        var url = ValidarUrl();
        if (!TieneSesionGuardada) throw new BannerSesionExpiradaException();

        using var pw = await Playwright.CreateAsync();
        await using var browser = await pw.Chromium.LaunchAsync(new() { Headless = !visible });
        var context = await browser.NewContextAsync(new() { StorageStatePath = _opciones.RutaSesion });
        var page = await context.NewPageAsync();

        await NavegarAsync(page, url, "el panel de Banner");

        Directory.CreateDirectory(_opciones.CarpetaMuestras);

        // Guardamos siempre el panel: sirve para localizar la ruta al histórico si el enlace cambia.
        await File.WriteAllTextAsync(Path.Combine(_opciones.CarpetaMuestras, "dashboard.html"), await page.ContentAsync(), ct);

        var enlace = page.Locator("a[href]").Filter(new() { HasTextRegex = EnlaceHistorico }).First;
        if (await enlace.CountAsync() == 0)
        {
            await GuardarEnlacesAsync(page, ct);
            throw new BannerException(
                "No encontré el enlace al Histórico Académico en el panel. " +
                "Guardé samples/dashboard.html y samples/enlaces.txt para revisarlos.");
        }

        // El enlace apunta a otro host (Banner clásico, vía sso.unapec.edu.do), por eso se sigue el href
        // en vez de hacer clic: el clic sobre el menú del panel no navega.
        var href = await enlace.GetAttributeAsync("href");
        var destino = page;
        await NavegarAsync(destino, new Uri(new Uri(page.Url), href).ToString(), "el Histórico Académico");

        // Banner clásico muestra primero "Opciones de Histórico Académico" (nivel y tipo) y solo
        // después de enviar el formulario (POST a bwskotrn.P_ViewTran) devuelve el histórico.
        var formulario = destino.Locator("form[action$='bwskotrn.P_ViewTran']");
        if (await formulario.CountAsync() > 0)
        {
            await File.WriteAllTextAsync(Path.Combine(_opciones.CarpetaMuestras, "historico.opciones.html"), await destino.ContentAsync(), ct);
            await formulario.Locator("select[name=levl]").SelectOptionAsync(new SelectOptionValue { Value = "" }); // Todos los niveles
            await formulario.Locator("select[name=tprt]").SelectOptionAsync(new SelectOptionValue { Value = "UNAP" }); // HISTORICO UNAPEC
            await Task.WhenAll(
                destino.WaitForURLAsync(new Regex(@"bwskotrn\.P_ViewTran$", RegexOptions.IgnoreCase),
                    new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = TiempoNavegacionMs }),
                formulario.Locator("button[type=submit]").ClickAsync());
            await EsperarRedQuietaAsync(destino);
            if (await HayLoginAsync(destino)) throw new BannerSesionExpiradaException();
        }

        // Solo se escribe el archivo si la página realmente trae el histórico. Antes de esta validación,
        // una página de cierre de sesión pisó un historico.html bueno.
        if (await destino.Locator("table.datadisplaytable").CountAsync() == 0)
            throw new BannerException(
                $"La página final no contiene el histórico (terminé en {destino.Url}). " +
                "Si Banner cerró la sesión, inicia sesión de nuevo.");

        var ruta = Path.Combine(_opciones.CarpetaMuestras, "historico.html");
        await File.WriteAllTextAsync(ruta, await destino.ContentAsync(), ct);
        await File.WriteAllTextAsync(Path.Combine(_opciones.CarpetaMuestras, "historico.url.txt"), destino.Url, ct);
        return ruta;
    }

    // ── Exploración de la programación académica (horarios) ───────────────────────────────

    /// <summary>
    /// HERRAMIENTA DE CAPTURA (E01-A): recorre "Consultar programación académica" con la sesión guardada y guarda en
    /// samples/horarios/ el HTML de las pantallas, las respuestas JSON reales de los servicios que usa la página
    /// (períodos, materias, secciones) y un registro paso a paso. Sirve para construir el parser sobre datos reales.
    /// Cada paso está aislado: si uno falla se anota y se sigue, para no perder lo que sí se pudo capturar.
    /// </summary>
    /// <returns>La carpeta con los archivos y el registro de lo ocurrido.</returns>
    public virtual async Task<(string Carpeta, IReadOnlyList<string> Pasos)> ExplorarHorariosAsync(string materia = "ISO", CancellationToken ct = default)
    {
        try { return await ExplorarHorariosInternoAsync(materia, ct); }
        catch (Exception ex) when (EsFalloDeNavegador(ex)) { throw ErrorDeNavegador(ex); }
    }

    private async Task<(string, IReadOnlyList<string>)> ExplorarHorariosInternoAsync(string materia, CancellationToken ct)
    {
        ValidarUrl();
        if (!TieneSesionGuardada) throw new BannerSesionExpiradaException();

        var carpeta = Path.Combine(_opciones.CarpetaMuestras, "horarios");
        Directory.CreateDirectory(carpeta);
        var pasos = new List<string>();
        void Anotar(string texto) => pasos.Add($"{DateTime.Now:HH:mm:ss}  {texto}");

        using var pw = await Playwright.CreateAsync();
        await using var browser = await pw.Chromium.LaunchAsync(new() { Headless = true });
        var context = await browser.NewContextAsync(new() { StorageStatePath = _opciones.RutaSesion, Locale = "es-DO" });
        var page = await context.NewPageAsync();

        // Todo lo que la página pide a sus servicios internos queda registrado (URL, estado y tamaño); los JSON se guardan.
        var trafico = new List<string>();
        var guardados = 0;
        page.Response += (_, r) =>
        {
            if (!r.Url.Contains("/StudentRegistrationSsb/", StringComparison.OrdinalIgnoreCase)) return;
            var tipo = r.Headers.TryGetValue("content-type", out var t) ? t : "";
            if (tipo.Contains("json", StringComparison.OrdinalIgnoreCase) || r.Url.Contains("searchResults", StringComparison.OrdinalIgnoreCase))
            {
                var n = Interlocked.Increment(ref guardados);
                if (n <= 40)
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var cuerpo = await r.TextAsync();
                            lock (trafico) trafico.Add($"{r.Request.Method} {r.Status} {r.Url} ({cuerpo.Length} bytes)");
                            await File.WriteAllTextAsync(Path.Combine(carpeta, $"xhr-{n:D2}.json"), cuerpo, ct);
                        }
                        catch (Exception) { /* la respuesta se descartó (redirección o navegación): no importa */ }
                    });
            }
        };

        // 1) Pantalla de selección de período.
        await NavegarAsync(page, _opciones.UrlProgramacionAcademica, "la programación académica");
        await File.WriteAllTextAsync(Path.Combine(carpeta, "01-seleccion-periodo.html"), await page.ContentAsync(), ct);
        try { await page.ScreenshotAsync(new() { Path = Path.Combine(carpeta, "01-seleccion-periodo.png"), FullPage = true }); } catch (Exception) { }
        Anotar($"Abrí {page.Url}");

        var ids = await page.EvaluateAsync<string[]>(
            "() => [...document.querySelectorAll('[id]')].map(e => e.tagName.toLowerCase() + '#' + e.id + (e.type ? ' [' + e.type + ']' : '') + ' \"' + (e.innerText || e.value || '').trim().replace(/\\s+/g, ' ').slice(0, 40) + '\"')");
        await File.WriteAllLinesAsync(Path.Combine(carpeta, "01-elementos.txt"), ids, ct);
        Anotar($"La pantalla tiene {ids.Length} elementos con id (ver 01-elementos.txt)");

        var token = await page.EvaluateAsync<string?>(
            "() => document.querySelector('meta[name=synchronizerToken]')?.content ?? window.synchronizerToken ?? null");
        Anotar(token is null ? "No encontré el token de sincronización (meta synchronizerToken)" : $"Token de sincronización presente ({token.Length} caracteres)");

        // Llamada a un servicio de la propia página, con las cookies de la sesión (misma petición que hace la interfaz).
        async Task<(int Estado, string Cuerpo)> LlamarAsync(string metodo, string ruta, string? formulario = null)
        {
            var json = await page.EvaluateAsync<string>(
                """
                async ([metodo, ruta, formulario, token]) => {
                    const cab = { 'Accept': 'application/json, text/javascript, */*; q=0.01', 'X-Requested-With': 'XMLHttpRequest' };
                    if (token) cab['X-Synchronizer-Token'] = token;
                    if (formulario !== null) cab['Content-Type'] = 'application/x-www-form-urlencoded; charset=UTF-8';
                    const r = await fetch(ruta, { method: metodo, headers: cab, body: formulario, credentials: 'same-origin' });
                    return JSON.stringify({ estado: r.status, cuerpo: await r.text() });
                }
                """, new object?[] { metodo, ruta, formulario, token });
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            return (doc.RootElement.GetProperty("estado").GetInt32(), doc.RootElement.GetProperty("cuerpo").GetString() ?? "");
        }

        // 2) Períodos disponibles.
        var periodos = new List<(string Codigo, string Descripcion)>();
        try
        {
            var (estado, cuerpo) = await LlamarAsync("GET", "/StudentRegistrationSsb/ssb/classSearch/getTerms?searchTerm=&offset=1&max=30");
            await File.WriteAllTextAsync(Path.Combine(carpeta, "02-periodos.json"), cuerpo, ct);
            Anotar($"getTerms → HTTP {estado}, {cuerpo.Length} bytes");
            using var doc = System.Text.Json.JsonDocument.Parse(cuerpo);
            if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array)
                foreach (var e in doc.RootElement.EnumerateArray())
                    periodos.Add((e.GetProperty("code").GetString() ?? "", e.GetProperty("description").GetString() ?? ""));
            Anotar($"Períodos publicados: {periodos.Count} → " + string.Join(" | ", periodos.Take(8).Select(p => $"{p.Codigo} «{p.Descripcion}»")));
        }
        catch (Exception ex) { Anotar($"FALLÓ getTerms: {ex.GetType().Name}: {ex.Message.Split('\n')[0]}"); }

        // 3) Períodos de Grado a consultar: el más reciente (aunque no tenga nada publicado: es el estado vacío que hay que
        //    conocer) y los dos siguientes más recientes que sí tengan materias publicadas (para ver secciones reales).
        var sesion = await page.EvaluateAsync<string>("() => Math.random().toString(36).substring(2, 7) + Date.now()");
        var grado = periodos.Where(p => { var d = System.Net.WebUtility.HtmlDecode(p.Descripcion); return d.Contains("GRADO") && !d.Contains("POSGRADO"); }).ToList();
        var objetivos = new List<(string Codigo, string Descripcion)>();
        if (grado.Count > 0) objetivos.Add(grado[0]);
        foreach (var p in grado.Skip(1))
        {
            if (objetivos.Count >= 3) break;
            try
            {
                var (_, cuerpoMaterias) = await LlamarAsync("GET", $"/StudentRegistrationSsb/ssb/classSearch/get_subject?searchTerm=&term={p.Codigo}&offset=1&max=300");
                var conMaterias = cuerpoMaterias.Trim().Length > 5;
                Anotar($"[{p.Codigo}] «{System.Net.WebUtility.HtmlDecode(p.Descripcion)}»: {(conMaterias ? "tiene materias publicadas" : "sin materias publicadas")}");
                if (conMaterias) objetivos.Add(p);
            }
            catch (Exception ex) { Anotar($"[{p.Codigo}] no pude revisar sus materias: {ex.Message.Split('\n')[0]}"); }
        }
        Anotar("Períodos que se capturan: " + string.Join(", ", objetivos.Select(o => o.Codigo)));

        foreach (var (codigo, descripcion) in objetivos)
        {
            try
            {
                var (eMat, cMat) = await LlamarAsync("GET", $"/StudentRegistrationSsb/ssb/classSearch/get_subject?searchTerm=&term={codigo}&offset=1&max=300");
                await File.WriteAllTextAsync(Path.Combine(carpeta, $"03-materias-{codigo}.json"), cMat, ct);
                Anotar($"[{codigo}] get_subject → HTTP {eMat}, {cMat.Length} bytes");

                var (eTerm, cTerm) = await LlamarAsync("POST", "/StudentRegistrationSsb/ssb/term/search?mode=search",
                    $"term={codigo}&studyPath=&studyPathText=&startDatePicker=&endDatePicker=&uniqueSessionId={sesion}");
                await File.WriteAllTextAsync(Path.Combine(carpeta, $"04-fijar-periodo-{codigo}.json"), cTerm, ct);
                Anotar($"[{codigo}] term/search (fijar período «{descripcion}») → HTTP {eTerm}: {cTerm[..Math.Min(160, cTerm.Length)].Replace('\n', ' ')}");

                var consulta = $"/StudentRegistrationSsb/ssb/searchResults/searchResults?txt_subject={Uri.EscapeDataString(materia)}&txt_term={codigo}" +
                               $"&startDatePicker=&endDatePicker=&uniqueSessionId={sesion}&pageOffset=0&pageMaxSize=50&sortColumn=subjectDescription&sortDirection=asc";
                var (eRes, cRes) = await LlamarAsync("GET", consulta);
                await File.WriteAllTextAsync(Path.Combine(carpeta, $"05-secciones-{codigo}-{materia}.json"), cRes, ct);
                using var res = System.Text.Json.JsonDocument.Parse(cRes);
                var total = res.RootElement.TryGetProperty("totalCount", out var tc) ? tc.ToString() : "?";
                var datos = res.RootElement.TryGetProperty("data", out var d) && d.ValueKind == System.Text.Json.JsonValueKind.Array ? d.GetArrayLength() : -1;
                Anotar($"[{codigo}] searchResults «{materia}» → HTTP {eRes}, totalCount={total}, secciones en la respuesta={datos}, {cRes.Length} bytes");
            }
            catch (Exception ex) { Anotar($"[{codigo}] FALLÓ: {ex.GetType().Name}: {ex.Message.Split('\n')[0]}"); }
        }

        await page.WaitForTimeoutAsync(500);
        Anotar($"Tráfico de la página hacia Banner 9 ({trafico.Count} respuestas):");
        lock (trafico) foreach (var t in trafico.Take(20)) Anotar("   " + t);

        await File.WriteAllLinesAsync(Path.Combine(carpeta, "pasos.log"), pasos, ct);
        return (carpeta, pasos);
    }

    // ── Consulta de secciones ─────────────────────────────────────────────────────────────

    /// <summary>Páginas como máximo por consulta: un tope de seguridad para no quedar en un ciclo si Banner responde algo raro.</summary>
    private const int MaxPaginas = 40;

    /// <summary>
    /// Consulta las secciones publicadas de una o varias materias en un período (código de Banner, p. ej. 202630), con la
    /// sesión guardada. Solo lectura y solo cuando el usuario lo pide. Pagina hasta traer todo y deja una pausa entre
    /// peticiones. Que no haya secciones no es un error: devuelve un resultado vacío.
    /// </summary>
    public virtual async Task<ResultadoBusqueda> ConsultarSeccionesAsync(
        string periodo, IReadOnlyList<ConsultaBanner> consultas, CancellationToken ct = default)
    {
        try { return await ConsultarSeccionesInternoAsync(periodo, consultas, ct); }
        catch (Exception ex) when (EsFalloDeNavegador(ex)) { throw ErrorDeNavegador(ex); }
    }

    private async Task<ResultadoBusqueda> ConsultarSeccionesInternoAsync(string periodo, IReadOnlyList<ConsultaBanner> consultas, CancellationToken ct)
    {
        ValidarUrl();
        if (!Regex.IsMatch(periodo ?? "", @"^\d{6}$")) throw new BannerException($"El período «{periodo}» no es un código de Banner válido (ej. 202630).");
        if (consultas.Count == 0) return new ResultadoBusqueda();

        return await ConSesionDeConsultaAsync(periodo!, sesion => PaginarAsync(sesion, consultas, ct), ct);
    }

    // ── Consulta de varias materias (E01-C) ───────────────────────────────────────────────

    /// <summary>
    /// Consulta varias materias del mismo período con una sola sesión de Banner (un solo navegador y el período fijado una
    /// vez), dejando una pausa entre una materia y la siguiente. Cada resultado se entrega a <paramref name="alTerminar"/>
    /// en cuanto está listo, para que se guarde y se pueda mostrar el avance. Si una materia falla (Banner responde raro)
    /// se anota y se sigue con las demás; si la sesión caduca se detiene todo y lo ya entregado queda a salvo.
    /// </summary>
    public virtual async Task ConsultarLoteAsync(
        string periodo, IReadOnlyList<LoteConsulta> lote, Func<LoteConsulta, ResultadoLote, Task> alTerminar, CancellationToken ct = default)
    {
        try
        {
            ValidarUrl();
            if (!Regex.IsMatch(periodo ?? "", @"^\d{6}$")) throw new BannerException($"El período «{periodo}» no es un código de Banner válido (ej. 202630).");
            if (lote.Count == 0) return;

            await ConSesionDeConsultaAsync(periodo!, async sesion =>
            {
                foreach (var item in lote)
                {
                    ct.ThrowIfCancellationRequested();
                    ResultadoLote resultado;
                    try { resultado = new ResultadoLote(await PaginarAsync(sesion, item.Consultas, ct), null); }
                    catch (BannerSesionExpiradaException) { throw; }
                    catch (BannerException ex) { resultado = new ResultadoLote(null, ex.Message); }
                    await alTerminar(item, resultado);
                }
                return true;
            }, ct);
        }
        catch (Exception ex) when (EsFalloDeNavegador(ex)) { throw ErrorDeNavegador(ex); }
    }

    /// <summary>Una sesión de consulta abierta: la página con la sesión de Banner, el token y el identificador de búsqueda.</summary>
    private sealed class SesionConsulta
    {
        public required IPage Page { get; init; }
        public required string Token { get; init; }
        public required string Id { get; init; }
        public required string Periodo { get; init; }
        /// <summary>Peticiones de búsqueda hechas: a partir de la segunda se espera antes de cada una.</summary>
        public int Peticiones { get; set; }
    }

    /// <summary>Abre el navegador con la sesión guardada, fija el período y ejecuta el trabajo con esa sesión.</summary>
    private async Task<T> ConSesionDeConsultaAsync<T>(string periodo, Func<SesionConsulta, Task<T>> trabajo, CancellationToken ct)
    {
        if (!TieneSesionGuardada) throw new BannerSesionExpiradaException();

        using var pw = await Playwright.CreateAsync();
        await using var browser = await pw.Chromium.LaunchAsync(new() { Headless = true });
        var context = await browser.NewContextAsync(new() { StorageStatePath = _opciones.RutaSesion, Locale = "es-DO" });
        var page = await context.NewPageAsync();

        await NavegarAsync(page, _opciones.UrlProgramacionAcademica, "la programación académica");
        var token = await page.EvaluateAsync<string?>(
            "() => document.querySelector('meta[name=synchronizerToken]')?.content ?? window.synchronizerToken ?? null");
        if (token is null) throw new BannerException("Banner no entregó el token de sincronización; inicia sesión de nuevo.");
        var id = await page.EvaluateAsync<string>("() => Math.random().toString(36).substring(2, 7) + Date.now()");

        // Fija el período (equivale a elegirlo en la pantalla y pulsar «Continuar»).
        var (eTerm, _) = await LlamarServicioAsync(page, token, "POST", "/StudentRegistrationSsb/ssb/term/search?mode=search",
            $"term={periodo}&studyPath=&studyPathText=&startDatePicker=&endDatePicker=&uniqueSessionId={id}");
        if (eTerm is 401 or 403) throw new BannerSesionExpiradaException();
        if (eTerm != 200) throw new BannerException($"Banner no aceptó el período {periodo} (HTTP {eTerm}).");

        return await trabajo(new SesionConsulta { Page = page, Token = token, Id = id, Periodo = periodo });
    }

    /// <summary>Trae todas las secciones de una o varias consultas (materia y curso), página por página y con pausa entre peticiones.</summary>
    private async Task<ResultadoBusqueda> PaginarAsync(SesionConsulta sesion, IReadOnlyList<ConsultaBanner> consultas, CancellationToken ct)
    {
        var secciones = new List<SeccionBanner>();
        var total = 0;
        var tamano = Math.Clamp(_opciones.TamanoPaginaSecciones, 1, 500);
        foreach (var consulta in consultas)
        {
            for (var pagina = 0; pagina < MaxPaginas; pagina++)
            {
                ct.ThrowIfCancellationRequested();
                if (sesion.Peticiones++ > 0) await Task.Delay(_opciones.PausaEntreConsultasMs, ct);   // nunca a ráfagas

                var url = "/StudentRegistrationSsb/ssb/searchResults/searchResults?txt_subject=" + Uri.EscapeDataString(consulta.Materia) +
                          (consulta.Curso is null ? "" : "&txt_courseNumber=" + Uri.EscapeDataString(consulta.Curso)) +
                          $"&txt_term={sesion.Periodo}&startDatePicker=&endDatePicker=&uniqueSessionId={sesion.Id}" +
                          $"&pageOffset={pagina * tamano}&pageMaxSize={tamano}&sortColumn=subjectDescription&sortDirection=asc";
                var (estado, cuerpo) = await LlamarServicioAsync(sesion.Page, sesion.Token, "GET", url);
                if (estado is 401 or 403) throw new BannerSesionExpiradaException();
                if (estado != 200) throw new BannerException($"Banner respondió HTTP {estado} al consultar {consulta.Codigo} en {sesion.Periodo}.");

                var resultado = SeccionesParser.Parse(cuerpo);
                secciones.AddRange(resultado.Secciones);
                if (pagina == 0) total += resultado.Total;
                if (resultado.Secciones.Count == 0 || (pagina + 1) * tamano >= resultado.Total) break;
            }
        }

        // Una misma sección no debería repetirse; por si Banner solapa páginas.
        var unicas = secciones.GroupBy(s => (s.Periodo, s.Nrc)).Select(g => g.First()).ToList();
        return new ResultadoBusqueda { Total = Math.Max(total, unicas.Count), Secciones = unicas };
    }

    /// <summary>Llama a un servicio de la propia página con las cookies de la sesión y el token de sincronización.</summary>
    private static async Task<(int Estado, string Cuerpo)> LlamarServicioAsync(IPage page, string? token, string metodo, string ruta, string? formulario = null)
    {
        var json = await page.EvaluateAsync<string>(
            """
            async ([metodo, ruta, formulario, token]) => {
                const cab = { 'Accept': 'application/json, text/javascript, */*; q=0.01', 'X-Requested-With': 'XMLHttpRequest' };
                if (token) cab['X-Synchronizer-Token'] = token;
                if (formulario !== null) cab['Content-Type'] = 'application/x-www-form-urlencoded; charset=UTF-8';
                const r = await fetch(ruta, { method: metodo, headers: cab, body: formulario, credentials: 'same-origin' });
                return JSON.stringify({ estado: r.status, cuerpo: await r.text() });
            }
            """, new object?[] { metodo, ruta, formulario, token });
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        return (doc.RootElement.GetProperty("estado").GetInt32(), doc.RootElement.GetProperty("cuerpo").GetString() ?? "");
    }

    // ── Navegación tolerante ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Va a una URL esperando solo a DOMContentLoaded. Las páginas de Banner cargan recursos de terceros que pueden
    /// tardar mucho o no terminar, así que esperar el evento "load" hacía fallar la sincronización por tiempo agotado.
    /// Devuelve false si la navegación no terminó a tiempo.
    /// </summary>
    private async Task<bool> IrAsync(IPage page, string url)
    {
        try
        {
            await page.GotoAsync(url, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = TiempoNavegacionMs });
            return true;
        }
        catch (Exception ex) when (EsTiempoAgotado(ex)) { return false; }
    }

    /// <summary>Navega y comprueba el resultado: sesión caducada (login), o Banner sin responder.</summary>
    private async Task NavegarAsync(IPage page, string url, string queEs)
    {
        var cargo = await IrAsync(page, url);
        if (await HayLoginAsync(page)) throw new BannerSesionExpiradaException();
        if (!cargo)
            throw new BannerException(
                $"Banner no respondió a tiempo al abrir {queEs} (más de {TiempoNavegacionMs / 1000} segundos). " +
                "Revisa tu conexión o que Banner esté disponible, y vuelve a intentarlo.");
        await EsperarRedQuietaAsync(page);
        if (await HayLoginAsync(page)) throw new BannerSesionExpiradaException();
    }

    private static async Task EsperarRedQuietaAsync(IPage page)
    {
        try { await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = TiempoRedQuietaMs }); }
        catch (Exception ex) when (EsTiempoAgotado(ex)) { /* la red no se calma: se sigue con lo que ya cargó */ }
    }

    /// <summary>Playwright .NET lanza System.TimeoutException (no PlaywrightException) cuando se agota un tiempo.</summary>
    private static bool EsTiempoAgotado(Exception ex) =>
        ex is TimeoutException ||
        (ex is PlaywrightException && ex.Message.Contains("Timeout", StringComparison.OrdinalIgnoreCase));

    private static bool EsFalloDeNavegador(Exception ex) => ex is PlaywrightException or TimeoutException;

    /// <summary>Convierte un fallo del navegador en un error legible (y sin la pila de Playwright).</summary>
    private static BannerException ErrorDeNavegador(Exception ex)
    {
        var primeraLinea = ex.Message.Split('\n')[0].Trim();
        if (ex.Message.Contains("Executable doesn't exist", StringComparison.OrdinalIgnoreCase))
            return new BannerException(InstaladorNavegador.MensajeFalta, ex);
        return new BannerException(
            EsTiempoAgotado(ex)
                ? $"Banner tardó demasiado en responder ({primeraLinea}). Vuelve a intentarlo."
                : $"El navegador falló al hablar con Banner: {primeraLinea}", ex);
    }

    // ── Detección de sesión ───────────────────────────────────────────────────────────────

    private string ValidarUrl()
    {
        if (string.IsNullOrWhiteSpace(_opciones.BaseUrl))
            throw new BannerException("No sé a qué Banner conectarme: elige tu universidad en «Carrera y pénsum» (o configura Banner:BaseUrl con dotnet user-secrets).");
        return _opciones.BaseUrl;
    }

    /// <summary>
    /// La sesión está activa cuando alguna pestaña está dentro de /StudentSelfService/ssb/ en el mismo host de Banner.
    /// El login vive en otra ruta (o en otro host), por eso una redirección al login la hace falsa.
    /// </summary>
    private static bool EsSesionActiva(IBrowserContext context, string baseUrl)
    {
        var host = new Uri(baseUrl).Host;
        return context.Pages.Any(p =>
            Uri.TryCreate(p.Url, UriKind.Absolute, out var u) &&
            u.Host.Equals(host, StringComparison.OrdinalIgnoreCase) &&
            u.AbsolutePath.Contains("/StudentSelfService/ssb/", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Redirigido al login o al cierre de sesión. Banner vive en los dominios de <see cref="BannerOptions.HostsBanner"/>
    /// (por defecto unapec.edu.do y sus subdominios): cualquier otro host (login.microsoftonline.com, wsignout…) es
    /// autenticación. También cuenta un campo de contraseña o una ruta de autenticación. Una página en blanco
    /// (la navegación no llegó a empezar) no es un login.
    /// </summary>
    private async Task<bool> HayLoginAsync(IPage page)
    {
        if (!Uri.TryCreate(page.Url, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme is not ("http" or "https")) return false;
        var esBanner = _opciones.HostsBanner.Any(h =>
            uri.Host.Equals(h, StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith("." + h, StringComparison.OrdinalIgnoreCase));
        if (!esBanner) return true;
        return Regex.IsMatch(uri.AbsolutePath, "/(login|cas|auth)(/|$)", RegexOptions.IgnoreCase) ||
               await page.Locator("input[type=password]").CountAsync() > 0;
    }

    private static IPage ActivaPagina(IBrowserContext context) => context.Pages[^1];

    private async Task GuardarEnlacesAsync(IPage page, CancellationToken ct)
    {
        var enlaces = await page.Locator("a, button, [role=menuitem], [role=link]").EvaluateAllAsync<string[]>(
            "els => els.map(e => (e.innerText || e.getAttribute('aria-label') || '').trim().replace(/\\s+/g,' ') + ' | ' + (e.getAttribute('href') || ''))");
        await File.WriteAllLinesAsync(Path.Combine(_opciones.CarpetaMuestras, "enlaces.txt"),
            enlaces.Where(l => l.Length > 3).Distinct(), ct);
    }
}
