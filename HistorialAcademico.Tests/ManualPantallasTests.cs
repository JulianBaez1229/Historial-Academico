using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using HistorialAcademico.Core.Entities;
using HistorialAcademico.Core.Manual;
using HistorialAcademico.Web.Data;
using HistorialAcademico.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static HistorialAcademico.Tests.PerfilesPantallasTests;

namespace HistorialAcademico.Tests;

/// <summary>Modo sin Banner: registrar materias a mano o importarlas de un CSV, de punta a punta como lo haría el navegador.</summary>
public class ManualPantallasTests
{
    private static readonly List<MateriaPensum> Pensum = DatosLab.Pensum();
    private static readonly MateriaPensum M1 = Pensum.First(m => m.Cuatrimestre == 1);
    private static readonly MateriaPensum M2 = Pensum.Where(m => m.Cuatrimestre == 1).Skip(1).First();
    private static readonly MateriaPensum M3 = Pensum.Where(m => m.Cuatrimestre == 1).Skip(2).First();

    /// <summary>Una aplicación vacía (sin histórico), con el pénsum ya cargado; solo con eso se pueden registrar materias.</summary>
    private static async Task<AppFactory> NuevaAsync(bool conPensum = true)
    {
        var app = new AppFactory();
        await app.InitializeAsync();
        if (conPensum)
        {
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<HistorialContext>();
            db.MateriasPensum.AddRange(DatosLab.Pensum());
            await db.SaveChangesAsync();
        }
        return app;
    }

    private static async Task<EstadoAcademico> EstadoAsync(AppFactory app)
    {
        using var scope = app.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AcademicoService>().ObtenerAsync();
    }

    private static async Task<int> ContarManualesAsync(AppFactory app)
    {
        using var scope = app.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<HistorialContext>().MateriasManuales.CountAsync();
    }

    private static async Task<string> TokenAsync(HttpClient c) =>
        Regex.Match(await c.GetStringAsync("/MateriasManuales"), "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;

    private static async Task<(HttpResponseMessage, string)> AgregarAsync(HttpClient c, string codigo, string periodo, string nota = "")
    {
        var campos = new Dictionary<string, string> { ["codigo"] = codigo, ["periodo"] = periodo, ["calificacion"] = nota, ["__RequestVerificationToken"] = await TokenAsync(c) };
        var r = await c.PostAsync("/MateriasManuales/Agregar", new FormUrlEncodedContent(campos));
        return (r, WebUtility.HtmlDecode(await r.Content.ReadAsStringAsync()));
    }

    private static async Task<(HttpResponseMessage, string)> ImportarAsync(HttpClient c, byte[]? contenido, string nombre = "materias.csv")
    {
        var form = new MultipartFormDataContent { { new StringContent(await TokenAsync(c)), "__RequestVerificationToken" } };
        if (contenido is not null) form.Add(new ByteArrayContent(contenido), "archivo", nombre);
        var r = await c.PostAsync("/MateriasManuales/Importar", form);
        return (r, WebUtility.HtmlDecode(await r.Content.ReadAsStringAsync()));
    }

    private static byte[] Utf8(string texto) => Encoding.UTF8.GetBytes(texto);

    // ── La pantalla ───────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SinPensumPideElegirLaCarreraPrimero()
    {
        using var app = await NuevaAsync(conPensum: false);
        using var c = app.CreateClient();

        var html = WebUtility.HtmlDecode(await c.GetStringAsync("/MateriasManuales"));

        Assert.Contains("Primero elige tu carrera", html);
        Assert.DoesNotContain("form-agregar", html);
    }

    [Fact]
    public async Task ConPensumMuestraElFormularioLaImportacionYSugiereCodigosYPeriodos()
    {
        using var app = await NuevaAsync();
        using var c = app.CreateClient();

        var html = WebUtility.HtmlDecode(await c.GetStringAsync("/MateriasManuales"));

        Assert.Contains("form-agregar", html);
        Assert.Contains($"<option value=\"{M1.Codigo}\">", html);   // los códigos del pénsum, para sugerir al escribir
        Assert.Contains($"ENE-ABR {DateTime.Today.Year}", html);
        Assert.Contains("Descargar la plantilla", html);
        Assert.Contains("Todavía no registraste ninguna materia", html);
        Assert.Contains("En curso (sin calificación)", html);
        Assert.Contains("Materias a mano", html);   // y el ítem del menú
    }

    // ── Agregar una materia ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task UnaMateriaConNotaCuentaEnTodaLaAplicacion()
    {
        using var app = await NuevaAsync();
        using var c = app.CreateClient();

        var (r, html) = await AgregarAsync(c, M1.Codigo.ToLowerInvariant(), "ene-abr 2026", "a");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);   // (se siguió la redirección a la lista)
        Assert.Contains($"Registrada: {M1.Codigo}, ENE-ABR 2026, con A.", html);
        Assert.Contains("tabla-manuales", html);

        var estado = await EstadoAsync(app);
        Assert.True(estado.HayHistorico);
        Assert.True(estado.HayManuales);
        Assert.Contains(estado.Cursadas, m => m.Codigo == M1.Codigo && m.Calificacion == "A");
        Assert.Equal(4.00m, estado.Indice.Global.Indice);
        Assert.Equal(M1.Creditos, estado.Pensum.CreditosAprobados);
        Assert.Empty(estado.Advertencias);   // no hay Banner con qué comparar

        var tomadas = WebUtility.HtmlDecode(await c.GetStringAsync("/Estudiante/MateriasTomadas"));
        Assert.Contains("ENE-ABR 2026", tomadas);
        Assert.Contains(M1.Codigo, tomadas);
        Assert.DoesNotContain("Aún no hay histórico", tomadas);
        Assert.Contains("4.00", WebUtility.HtmlDecode(await c.GetStringAsync("/Estudiante/IndiceAcademico")));
        Assert.DoesNotContain("Todavía no hay datos", WebUtility.HtmlDecode(await c.GetStringAsync("/")));
    }

    [Fact]
    public async Task SinNotaEsUnCursoEnProgresoQueNoEntraEnElIndice()
    {
        using var app = await NuevaAsync();
        using var c = app.CreateClient();

        var (_, html) = await AgregarAsync(c, M1.Codigo, "SEP-DIC 2026");

        Assert.Contains($"Registrada: {M1.Codigo}, SEP-DIC 2026, en curso.", html);
        Assert.Contains("En curso", html);
        var estado = await EstadoAsync(app);
        Assert.Equal(M1.Codigo, Assert.Single(estado.CursosEnProgreso).Codigo);
        Assert.Empty(estado.Cursadas);
        Assert.Equal(0m, estado.Indice.Global.Indice);
        Assert.Equal(M1.Creditos, estado.Pensum.CreditosEnCurso);
    }

    [Fact]
    public async Task UnDatoIncorrectoSeExplicaYNoPierdeLoEscrito()
    {
        using var app = await NuevaAsync();
        using var c = app.CreateClient();

        var (codigo, htmlCodigo) = await AgregarAsync(c, "ZZZ999", "ENE-ABR 2026", "A");
        Assert.Equal(HttpStatusCode.OK, codigo.StatusCode);
        Assert.Contains("«ZZZ999» no está en tu pénsum", htmlCodigo);
        Assert.Contains("value=\"ZZZ999\"", htmlCodigo);   // lo escrito sigue en el formulario
        Assert.Contains("value=\"ENE-ABR 2026\"", htmlCodigo);

        var (_, htmlPeriodo) = await AgregarAsync(c, M1.Codigo, "primavera 2026", "A");
        Assert.Contains("no se entiende", htmlPeriodo);

        var (_, htmlNota) = await AgregarAsync(c, M1.Codigo, "ENE-ABR 2026", "Z");
        Assert.Contains("«Z» no existe", htmlNota);

        var (_, htmlVacio) = await AgregarAsync(c, "", "");
        Assert.Contains("Escribe el código", htmlVacio);

        Assert.Equal(0, await ContarManualesAsync(app));
    }

    [Fact]
    public async Task RegistrarLaMismaMateriaEnElMismoPeriodoCambiaLaCalificacion()
    {
        using var app = await NuevaAsync();
        using var c = app.CreateClient();
        await AgregarAsync(c, M1.Codigo, "ENE-ABR 2026", "F");

        var (_, html) = await AgregarAsync(c, M1.Codigo, "ENE-ABR 2026", "B");

        Assert.Contains($"Actualizada: {M1.Codigo}, ENE-ABR 2026, con B.", html);
        Assert.Equal(1, await ContarManualesAsync(app));
        Assert.Equal(3.00m, (await EstadoAsync(app)).Indice.Global.Indice);
    }

    [Fact]
    public async Task UnaMateriaReprobadaYVueltaATomarSeGuardaEnDosPeriodos()
    {
        using var app = await NuevaAsync();
        using var c = app.CreateClient();
        await AgregarAsync(c, M1.Codigo, "ENE-ABR 2026", "F");
        await AgregarAsync(c, M1.Codigo, "MAY-AGO 2026", "A");

        var estado = await EstadoAsync(app);

        Assert.Equal(2, await ContarManualesAsync(app));
        Assert.Equal(2m, estado.Indice.Global.Indice);   // (0 + 4×4) / 8: las dos veces cuentan, como en Banner
        Assert.Equal(new[] { "ENE-ABR 2026", "MAY-AGO 2026" }, estado.Periodos.Select(p => p.Nombre));
    }

    [Fact]
    public async Task QuitarUnaMateriaLaSacaDeTodo()
    {
        using var app = await NuevaAsync();
        using var c = app.CreateClient();
        await AgregarAsync(c, M1.Codigo, "ENE-ABR 2026", "A");
        int id;
        using (var scope = app.Services.CreateScope()) id = (await scope.ServiceProvider.GetRequiredService<HistorialContext>().MateriasManuales.SingleAsync()).Id;

        var quitar = async () =>
        {
            var campos = new Dictionary<string, string> { ["id"] = id.ToString(), ["__RequestVerificationToken"] = await TokenAsync(c) };
            return WebUtility.HtmlDecode(await (await c.PostAsync("/MateriasManuales/Eliminar", new FormUrlEncodedContent(campos))).Content.ReadAsStringAsync());
        };

        Assert.Contains("Materia quitada.", await quitar());
        Assert.False((await EstadoAsync(app)).HayHistorico);
        Assert.Contains("ya no estaba registrada", await quitar());
    }

    // ── Importar un CSV ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ImportaUnCsvYLuegoLoCambiaSinDuplicar()
    {
        using var app = await NuevaAsync();
        using var c = app.CreateClient();

        var (_, primero) = await ImportarAsync(c, Utf8($"codigo,periodo,calificacion\n{M1.Codigo},ENE-ABR 2026,A\n{M2.Codigo},ENE-ABR 2026,B\n{M3.Codigo},MAY-AGO 2026,\n"));
        Assert.Contains("Importé el archivo: 3 materias nuevas.", primero);
        Assert.Equal(3, await ContarManualesAsync(app));
        var estado = await EstadoAsync(app);
        Assert.Equal(2, estado.Cursadas.Count());
        Assert.Single(estado.CursosEnProgreso);

        // Se vuelve a importar el mismo archivo con una nota distinta: cambia, no se duplica.
        var (_, segundo) = await ImportarAsync(c, Utf8($"codigo;periodo;calificacion\r\n{M1.Codigo};ENE-ABR 2026;C\r\n{M2.Codigo};ENE-ABR 2026;B\r\n"));
        Assert.Contains("0 materias nuevas, 1 con la calificación cambiada, 1 que ya estaban igual", segundo);
        Assert.Equal(3, await ContarManualesAsync(app));
        Assert.Contains((await EstadoAsync(app)).Cursadas, m => m.Codigo == M1.Codigo && m.Calificacion == "C");
    }

    [Fact]
    public async Task UnCsvConProblemasNoImportaNadaYDiceEnQueLinea()
    {
        using var app = await NuevaAsync();
        using var c = app.CreateClient();
        var csv = $"codigo,periodo,calificacion\n{M1.Codigo},ENE-ABR 2026,A\nZZZ999,ENE-ABR 2026,B\n{M2.Codigo},cuando sea,A\n{M3.Codigo},ENE-ABR 2026,Q\n{M3.Codigo}\n";

        var (r, html) = await ImportarAsync(c, Utf8(csv));

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Contains("No importé nada: hay 4 problemas", html);
        Assert.Contains("Línea 3: «ZZZ999» no está en tu pénsum", html);
        Assert.Contains("Línea 4:", html);
        Assert.Contains("Línea 5: La calificación «Q»", html);
        Assert.Contains("Línea 6: faltan columnas", html);
        Assert.Equal(0, await ContarManualesAsync(app));   // ni siquiera la fila buena
    }

    [Fact]
    public async Task ImportarSinArchivoOConUnArchivoDemasiadoGrandeSeExplica()
    {
        using var app = await NuevaAsync();
        using var c = app.CreateClient();

        Assert.Contains("Elige un archivo CSV", (await ImportarAsync(c, null)).Item2);
        Assert.Contains("Elige un archivo CSV", (await ImportarAsync(c, Array.Empty<byte>())).Item2);
        var grande = (await ImportarAsync(c, new byte[300 * 1024 + 1])).Item2;
        Assert.Contains("demasiado grande", grande);
        Assert.Equal(0, await ContarManualesAsync(app));
    }

    [Fact]
    public async Task ElCsvConMarcaUtf8YTildesSeLeeBien()
    {
        using var app = await NuevaAsync();
        using var c = app.CreateClient();
        var bytes = new UTF8Encoding(true).GetPreamble().Concat(Utf8($"Código;Período;Calificación\n{M1.Codigo};ene-abr 2026;a\n")).ToArray();

        var (_, html) = await ImportarAsync(c, bytes);

        Assert.Contains("Importé el archivo: 1 materia nueva.", html);
    }

    [Fact]
    public async Task LaPlantillaSeDescargaComoCsvConMarcaUtf8YSusEjemplosSeImportan()
    {
        using var app = await NuevaAsync();
        using var c = app.CreateClient();

        var r = await c.GetAsync("/MateriasManuales/Plantilla");
        var bytes = await r.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("text/csv", r.Content.Headers.ContentType!.MediaType);
        Assert.Equal("plantilla-materias.csv", (r.Content.Headers.ContentDisposition!.FileNameStar ?? r.Content.Headers.ContentDisposition.FileName)!.Trim('"'));
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray());   // para que Excel respete las tildes
        var texto = Encoding.UTF8.GetString(bytes);
        Assert.Contains(Pensum.OrderBy(m => m.Cuatrimestre).ThenBy(m => m.Codigo).First().Codigo, texto);   // con materias del pénsum de la persona

        var (_, html) = await ImportarAsync(c, bytes);   // la plantilla tal cual se puede importar
        Assert.Contains("Importé el archivo: 3 materias nuevas.", html);
    }

    // ── Banner y lo manual ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task BannerMandaEnSusPeriodosYLosAvisosDeDiferenciasNoCambian()
    {
        using var app = new AppConDatosFactory();
        await app.InitializeAsync();
        using var c = app.CreateClient();
        var antes = await EstadoAsync(app);
        var libre = antes.MateriasPensum.First(m => antes.Cursadas.All(x => x.Codigo != m.Codigo) && antes.CursosEnProgreso.All(x => x.Codigo != m.Codigo));
        var periodoDeBanner = antes.Periodos.First().Nombre;

        await AgregarAsync(c, libre.Codigo, periodoDeBanner, "A");                                   // lo trae Banner: no cuenta
        await AgregarAsync(c, libre.Codigo, "ENE-ABR 2000", "A");                                    // Banner no lo trae: cuenta

        Assert.Contains("Banner ya trae este período", WebUtility.HtmlDecode(await c.GetStringAsync("/MateriasManuales")));
        var despues = await EstadoAsync(app);
        Assert.Equal(antes.Periodos.Count + 1, despues.Periodos.Count);
        Assert.Equal("ENE-ABR 2000", despues.Periodos.First().Nombre);   // va primero: es más antiguo
        Assert.Equal(antes.Periodos.First().Materias.Count, despues.Periodos.Single(p => p.Nombre == periodoDeBanner).Materias.Count);
        Assert.Equal(antes.Advertencias, despues.Advertencias);   // Banner se compara solo con Banner
        Assert.True(despues.HayManuales);
        Assert.Contains(despues.Cursadas, m => m.Codigo == libre.Codigo && m.Calificacion == "A");
    }

    [Fact]
    public async Task ConectarBannerDespuesNoDuplicaNiBorraLoManual()
    {
        using var app = await NuevaAsync();
        using var c = app.CreateClient();
        // Lo mismo que trae el histórico sintético, registrado a mano antes de conectar Banner.
        var banner = Muestras.LeerSintetico();
        string periodoBanner;
        using (var scope = app.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<SincronizacionService>().AplicarHtmlAsync(banner);
            periodoBanner = (await scope.ServiceProvider.GetRequiredService<HistorialContext>().Periodos.OrderBy(p => p.Orden).FirstAsync()).Nombre;
        }
        var soloBanner = await EstadoAsync(app);
        var libre = soloBanner.MateriasPensum.First(m => soloBanner.Cursadas.All(x => x.Codigo != m.Codigo));
        await AgregarAsync(c, libre.Codigo, periodoBanner, "A");   // Banner ya trae ese período: no cuenta
        await AgregarAsync(c, libre.Codigo, "ENE-ABR 2000", "B");  // Banner no lo trae: cuenta
        var conBanner = await EstadoAsync(app);
        Assert.Equal(soloBanner.Cursadas.Count() + 1, conBanner.Cursadas.Count());   // la de Banner no se duplicó

        using (var scope = app.Services.CreateScope())
            Assert.True((await scope.ServiceProvider.GetRequiredService<SincronizacionService>().AplicarHtmlAsync(banner)).Exito);   // otra sincronización

        Assert.Equal(2, await ContarManualesAsync(app));   // siguen guardadas: sincronizar no las toca
        var despues = await EstadoAsync(app);
        Assert.Equal(conBanner.Cursadas.Count(), despues.Cursadas.Count());
        Assert.Equal(conBanner.Indice.Global.Indice, despues.Indice.Global.Indice);
    }

    [Fact]
    public async Task SiCambiasDeCarreraLoQueNoEstaEnElNuevoPensumQuedaGuardadoSinContar()
    {
        using var app = await NuevaAsync();
        using var c = app.CreateClient();
        await AgregarAsync(c, M1.Codigo, "ENE-ABR 2026", "A");
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HistorialContext>();
            await db.MateriasPensum.Where(m => m.Codigo == M1.Codigo).ExecuteDeleteAsync();   // el pénsum nuevo no tiene esta materia
        }

        var html = WebUtility.HtmlDecode(await c.GetStringAsync("/MateriasManuales"));

        Assert.Contains("No está en tu pénsum actual", html);
        Assert.Contains(M1.Codigo, html);   // sigue en la lista
        Assert.False((await EstadoAsync(app)).HayHistorico);
    }

    // ── Cada perfil, lo suyo ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task LasMateriasRegistradasSonDeCadaPerfil()
    {
        using var app = new AppConPerfilesFactory();
        using var ana = app.Cliente();
        using var beto = app.Cliente();
        await CrearAsync(ana, "Ana");
        await CrearAsync(beto, "Beto");
        foreach (var c in new[] { ana, beto })
        {
            var t = Regex.Match(await c.GetStringAsync("/Carrera"), "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
            await c.PostAsync("/Carrera/Activar", new FormUrlEncodedContent(new Dictionary<string, string> { ["clave"] = "unapec/ingenieria-software-11.json", ["__RequestVerificationToken"] = t }));
        }
        string codigo;
        using (var scope = app.Abrir(app.Gestor.Almacen.Listar().First().Id))
            codigo = (await scope.ServiceProvider.GetRequiredService<HistorialContext>().MateriasPensum.OrderBy(m => m.Cuatrimestre).ThenBy(m => m.Codigo).FirstAsync()).Codigo;

        var token = Regex.Match(await ana.GetStringAsync("/MateriasManuales"), "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        var r = await ana.PostAsync("/MateriasManuales/Agregar", new FormUrlEncodedContent(new Dictionary<string, string>
            { ["codigo"] = codigo, ["periodo"] = "ENE-ABR 2026", ["calificacion"] = "A", ["__RequestVerificationToken"] = token }));

        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);   // (los clientes de perfiles no siguen redirecciones)
        Assert.Contains("tabla-manuales", await ana.GetStringAsync("/MateriasManuales"));
        var deBeto = await beto.GetStringAsync("/MateriasManuales");
        Assert.DoesNotContain("tabla-manuales", deBeto);
        Assert.Contains("Todavía no registraste ninguna materia", deBeto);
    }
}
