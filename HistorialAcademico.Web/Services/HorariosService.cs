using System.Text.Json;
using HistorialAcademico.Banner;
using HistorialAcademico.Core.Entities;
using HistorialAcademico.Core.Horarios;
using HistorialAcademico.Core.Planificacion;
using HistorialAcademico.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace HistorialAcademico.Web.Services;

public record ResultadoHorarios(bool Exito, string Mensaje, bool RequiereLogin = false);

/// <summary>Lo guardado de una materia en un período: las secciones y cuándo se consultó (null = nunca).</summary>
public class VistaSecciones
{
    public string Codigo { get; init; } = "";
    public string Periodo { get; init; } = "";
    public string PeriodoNombre { get; init; } = "";
    public List<SeccionBanner> Secciones { get; init; } = new();
    public DateTime? Consultada { get; init; }
    /// <summary>Motivo por el que esa materia no se consulta por horario (TFG, pasantía…).</summary>
    public string? NoConsultable { get; init; }

    public bool YaConsultada => Consultada is not null;
    /// <summary>Se consultó y Banner no tiene secciones publicadas por ahora: un estado normal, no un error.</summary>
    public bool SinSecciones => YaConsultada && Secciones.Count == 0;
}

/// <summary>Un período anterior en que la materia sí tuvo secciones publicadas.</summary>
public record OfertaPrevia(string Periodo, string PeriodoNombre, int Secciones);

/// <summary>Un profesor que ha dado la materia y los períodos (del más reciente al más antiguo) en que aparece en lo consultado.</summary>
public record ProfesorDeMateria(string Nombre, List<string> Periodos);

public enum EstadoPanorama { SinConsultar, SinSecciones, ConSecciones }

/// <summary>Una materia disponible en el panorama de un período: si se consultó y qué se encontró.</summary>
public record FilaPanorama(string Codigo, string Nombre, EstadoPanorama Estado, int Secciones, DateTime? Consultada);

public enum ResultadoItem { Pendiente, ConSecciones, SinSecciones, Error, Omitida }

/// <summary>El resultado de consultar una materia dentro de un lote.</summary>
public record ItemLote(string Codigo, string Nombre, ResultadoItem Resultado, int Secciones = 0, string? Mensaje = null);

/// <summary>Cómo terminó una consulta por lote.</summary>
public record ResultadoLoteGuardado(bool Exito, string Mensaje, bool RequiereLogin = false, bool Cancelada = false);

/// <summary>
/// Consulta a Banner las secciones (profesor, horario, aula, cupos) de las materias pendientes y las guarda en SQLite.
/// Solo a demanda, de una consulta a la vez y con pausa entre peticiones. No guarda datos personales de terceros:
/// del profesor solo el nombre que Banner publica.
/// </summary>
public class HorariosService
{
    private static readonly SemaphoreSlim Candado = new(1, 1);
    private static readonly JsonSerializerOptions Json = new();

    /// <summary>Cuántos períodos hacia atrás se revisan para saber cuándo se ofreció una materia por última vez.</summary>
    public const int PeriodosAnterioresARevisar = 3;

    private readonly HistorialContext _db;
    private readonly BannerClient _banner;

    public HorariosService(HistorialContext db, BannerClient banner)
    {
        _db = db;
        _banner = banner;
    }

    public static string NombreDePeriodo(string codigo) =>
        MapeoBanner.TryPeriodoDeCodigo(codigo, out var p) ? p.Nombre : codigo;

    // ── Consultar a Banner ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Consulta la materia (código del pénsum) en el período (código de Banner) y reemplaza lo guardado de esa materia en
    /// ese período. Si la sesión caducó y se permite, abre Chromium visible para iniciar sesión y reintenta una vez.
    /// </summary>
    public async Task<ResultadoHorarios> ConsultarAsync(string codigoPensum, string periodo, bool permitirLogin, CancellationToken ct = default)
    {
        var (consultas, motivo) = MapeoBanner.ConsultasDe(codigoPensum);
        if (consultas.Count == 0) return new ResultadoHorarios(false, motivo ?? "No hay nada que consultar.");
        if (!MapeoBanner.TryPeriodoDeCodigo(periodo, out _))
            return new ResultadoHorarios(false, $"El período «{periodo}» no es de Grado (usa ENE-ABR, MAY-AGO o SEP-DIC).");

        if (!await Candado.WaitAsync(0, ct)) return Ocupado;
        try { return await ConsultarSinCandadoAsync(codigoPensum, periodo, consultas, permitirLogin, ct); }
        finally { Candado.Release(); }
    }

    private static readonly ResultadoHorarios Ocupado =
        new(false, "Ya hay una consulta a Banner en curso. Espera a que termine (o cancélala) y vuelve a intentarlo.");

    private async Task<ResultadoHorarios> ConsultarSinCandadoAsync(
        string codigoPensum, string periodo, List<ConsultaBanner> consultas, bool permitirLogin, CancellationToken ct)
    {
        var r = await IntentarAsync(periodo, consultas, ct);
        if (!r.RequiereLogin || !permitirLogin) return Resumir(r, codigoPensum, periodo);

        try { await _banner.IniciarSesionAsync(ct); }
        catch (BannerException ex) { return new ResultadoHorarios(false, $"No se pudo iniciar sesión en Banner: {ex.Message}"); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ResultadoHorarios(false, $"No se pudo iniciar sesión en Banner: {ex.GetType().Name}: {ex.Message}");
        }
        return Resumir(await IntentarAsync(periodo, consultas, ct), codigoPensum, periodo);
    }

    private record Intento(ResultadoBusqueda? Busqueda, string? Error, bool RequiereLogin);

    private async Task<Intento> IntentarAsync(string periodo, List<ConsultaBanner> consultas, CancellationToken ct)
    {
        ResultadoBusqueda busqueda;
        try { busqueda = await _banner.ConsultarSeccionesAsync(periodo, consultas, ct); }
        catch (BannerSesionExpiradaException ex) { return new Intento(null, ex.Message, RequiereLogin: true); }
        catch (BannerException ex) { return new Intento(null, ex.Message, false); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new Intento(null, $"Error inesperado al consultar Banner: {ex.GetType().Name}: {ex.Message}", false);
        }

        try { await GuardarAsync(periodo, consultas, busqueda, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new Intento(null, $"Error al guardar las secciones: {ex.GetBaseException().Message}", false);
        }
        return new Intento(busqueda, null, false);
    }

    private static ResultadoHorarios Resumir(Intento i, string codigoPensum, string periodo)
    {
        if (i.Busqueda is null) return new ResultadoHorarios(false, i.Error ?? "No se pudo consultar Banner.", i.RequiereLogin);
        var nombre = NombreDePeriodo(periodo);
        return i.Busqueda.SinSecciones
            ? new ResultadoHorarios(true, $"Banner aún no tiene secciones publicadas de {codigoPensum} para {nombre}.")
            : new ResultadoHorarios(true, $"{i.Busqueda.Secciones.Count} secciones de {codigoPensum} en {nombre}.");
    }

    /// <summary>Reemplaza, en una transacción, lo guardado de esas materias en ese período y anota que se consultó.</summary>
    private async Task GuardarAsync(string periodo, List<ConsultaBanner> consultas, ResultadoBusqueda busqueda, CancellationToken ct)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        _db.ChangeTracker.Clear();

        var previas = (await _db.SeccionesOfertadas.Where(s => s.Periodo == periodo).ToListAsync(ct))
            .Where(s => consultas.Any(c => Coincide(c, s.Codigo))).ToList();
        _db.SeccionesOfertadas.RemoveRange(previas);
        await _db.SaveChangesAsync(ct);   // primero se borra: el índice (período, NRC) es único

        var ahora = DateTime.UtcNow;
        foreach (var s in busqueda.Secciones.Where(s => s.Periodo == periodo))
            _db.SeccionesOfertadas.Add(Mapear(s, ahora));

        foreach (var c in consultas)
        {
            var cuantas = busqueda.Secciones.Count(s => Coincide(c, s.Codigo));
            var registro = await _db.ConsultasSecciones.FirstOrDefaultAsync(x => x.Periodo == periodo && x.Codigo == c.Codigo, ct);
            if (registro is null) _db.ConsultasSecciones.Add(new ConsultaSecciones { Periodo = periodo, Codigo = c.Codigo, Secciones = cuantas, Fecha = ahora });
            else { registro.Secciones = cuantas; registro.Fecha = ahora; }
        }

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private static bool Coincide(ConsultaBanner consulta, string codigo) =>
        consulta.Curso is null
            ? codigo.StartsWith(consulta.Materia, StringComparison.OrdinalIgnoreCase)
            : string.Equals(codigo, consulta.Codigo, StringComparison.OrdinalIgnoreCase);

    // ── Períodos anteriores ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Revisa los períodos anteriores a <paramref name="periodo"/> que todavía no se hayan consultado, del más reciente al
    /// más antiguo, dejando una pausa entre uno y otro. Sirve para saber cuándo se ofreció por última vez una materia
    /// que aún no tiene secciones publicadas.
    /// </summary>
    public async Task<ResultadoHorarios> BuscarAnterioresAsync(string codigoPensum, string periodo, bool permitirLogin, CancellationToken ct = default)
    {
        var (consultas, motivo) = MapeoBanner.ConsultasDe(codigoPensum);
        if (consultas.Count == 0) return new ResultadoHorarios(false, motivo ?? "No hay nada que consultar.");
        if (!MapeoBanner.TryPeriodoDeCodigo(periodo, out var actual))
            return new ResultadoHorarios(false, $"El período «{periodo}» no es de Grado.");

        if (!await Candado.WaitAsync(0, ct)) return Ocupado;
        try
        {
            var consultados = 0;
            foreach (var anterior in MapeoBanner.Anteriores(actual, PeriodosAnterioresARevisar))
            {
                var codigo = MapeoBanner.CodigoDePeriodo(anterior);
                var codigos = consultas.Select(c => c.Codigo).ToList();
                var ya = await _db.ConsultasSecciones.Where(c => c.Periodo == codigo && codigos.Contains(c.Codigo)).CountAsync(ct);
                if (ya == codigos.Count) continue;   // ya se consultó: no se vuelve a preguntar a Banner

                if (consultados++ > 0) await Task.Delay(1500, ct);   // pausa entre períodos
                var r = await ConsultarSinCandadoAsync(codigoPensum, codigo, consultas, permitirLogin, ct);
                if (!r.Exito) return r;
            }
            return await ResumenOfertaAsync(codigoPensum, consultados, ct);
        }
        finally { Candado.Release(); }
    }

    private async Task<ResultadoHorarios> ResumenOfertaAsync(string codigoPensum, int consultados, CancellationToken ct)
    {
        var oferta = await OfertaPreviaAsync(codigoPensum, ct);
        var revisado = consultados == 0 ? "Ya tenía consultados los períodos anteriores. " : "";
        return oferta.Count == 0
            ? new ResultadoHorarios(true, revisado + $"{codigoPensum} no aparece con secciones en los últimos {PeriodosAnterioresARevisar} períodos.")
            : new ResultadoHorarios(true, revisado + $"{codigoPensum} se ofreció en {string.Join(", ", oferta.Select(o => o.PeriodoNombre))}.");
    }

    // ── Varias materias a la vez ──────────────────────────────────────────────────────────

    /// <summary>
    /// Consulta varias materias en un período con una sola sesión de Banner, con pausa entre una y otra. Cada materia se
    /// guarda apenas llega, así que si se cancela o falla a la mitad lo ya consultado queda guardado. Una materia que falla
    /// se anota y se sigue con las demás; una sesión caducada se resuelve con un inicio de sesión (una sola vez) si se permite.
    /// </summary>
    /// <param name="fase">Texto de lo que se está haciendo (para mostrar el avance).</param>
    /// <param name="alTerminarItem">Se llama por cada materia, incluidas las que se omiten.</param>
    public async Task<ResultadoLoteGuardado> ConsultarVariasAsync(
        IReadOnlyList<(string Codigo, string Nombre)> materias, string periodo, bool omitirConsultadas, bool permitirLogin,
        Action<string> fase, Action<ItemLote> alTerminarItem, CancellationToken ct = default)
    {
        if (!MapeoBanner.TryPeriodoDeCodigo(periodo, out _))
            return new ResultadoLoteGuardado(false, $"El período «{periodo}» no es de Grado (usa ENE-ABR, MAY-AGO o SEP-DIC).");
        if (!await Candado.WaitAsync(0, ct)) return new ResultadoLoteGuardado(false, Ocupado.Mensaje);

        try
        {
            var yaConsultadas = omitirConsultadas
                ? (await _db.ConsultasSecciones.AsNoTracking().Where(c => c.Periodo == periodo).Select(c => c.Codigo).ToListAsync(ct)).ToHashSet()
                : new HashSet<string>();

            var pendientes = new List<(string Codigo, string Nombre, List<ConsultaBanner> Consultas)>();
            foreach (var (codigo, nombre) in materias)
            {
                var (consultas, motivo) = MapeoBanner.ConsultasDe(codigo);
                if (consultas.Count == 0) alTerminarItem(new ItemLote(codigo, nombre, ResultadoItem.Omitida, 0, motivo));
                else if (omitirConsultadas && consultas.All(c => yaConsultadas.Contains(c.Codigo)))
                    alTerminarItem(new ItemLote(codigo, nombre, ResultadoItem.Omitida, 0, "Ya estaba consultada en este período."));
                else pendientes.Add((codigo, nombre, consultas));
            }
            if (pendientes.Count == 0) return new ResultadoLoteGuardado(true, "No había nada nuevo que consultar.");

            var hechas = new HashSet<string>();
            for (var intento = 1; ; intento++)
            {
                var lote = pendientes.Where(p => !hechas.Contains(p.Codigo)).Select(p => new LoteConsulta(p.Codigo, p.Consultas)).ToList();
                try
                {
                    fase("Consultando Banner…");
                    await _banner.ConsultarLoteAsync(periodo, lote, async (item, resultado) =>
                    {
                        var (codigo, nombre, consultas) = pendientes.First(p => p.Codigo == item.Clave);
                        if (resultado.Busqueda is null)
                            alTerminarItem(new ItemLote(codigo, nombre, ResultadoItem.Error, 0, resultado.Error));
                        else
                        {
                            // Sin el token de cancelación: la materia que ya llegó de Banner se guarda aunque se acabe de cancelar.
                            await GuardarAsync(periodo, consultas, resultado.Busqueda, CancellationToken.None);
                            var cuantas = resultado.Busqueda.Secciones.Count;
                            alTerminarItem(new ItemLote(codigo, nombre, cuantas > 0 ? ResultadoItem.ConSecciones : ResultadoItem.SinSecciones, cuantas));
                        }
                        hechas.Add(codigo);
                    }, ct);
                    return new ResultadoLoteGuardado(true, "Consulta terminada.");
                }
                catch (BannerSesionExpiradaException ex)
                {
                    if (intento > 1 || !permitirLogin) return new ResultadoLoteGuardado(false, ex.Message, RequiereLogin: true);
                    try
                    {
                        fase("Esperando a que inicies sesión en Banner (se abrió una ventana)…");
                        await _banner.IniciarSesionAsync(ct);
                    }
                    catch (BannerException e) { return new ResultadoLoteGuardado(false, $"No se pudo iniciar sesión en Banner: {e.Message}"); }
                    catch (Exception e) when (e is not OperationCanceledException)
                    {
                        return new ResultadoLoteGuardado(false, $"No se pudo iniciar sesión en Banner: {e.GetType().Name}: {e.Message}");
                    }
                }
                catch (BannerException ex) { return new ResultadoLoteGuardado(false, ex.Message); }
                catch (OperationCanceledException) { return new ResultadoLoteGuardado(false, "Consulta cancelada.", Cancelada: true); }
                catch (Exception ex)
                {
                    return new ResultadoLoteGuardado(false, $"Error inesperado al consultar Banner: {ex.GetType().Name}: {ex.Message}");
                }
            }
        }
        finally { Candado.Release(); }
    }

    /// <summary>
    /// Para cada materia (que se pueda consultar por horario), qué se sabe de ella en el período según lo guardado:
    /// sin consultar, sin secciones publicadas o con N secciones. No va a Banner.
    /// </summary>
    public async Task<List<FilaPanorama>> PanoramaAsync(IEnumerable<(string Codigo, string Nombre)> materias, string periodo, CancellationToken ct = default)
    {
        var registros = await _db.ConsultasSecciones.AsNoTracking().Where(c => c.Periodo == periodo).ToListAsync(ct);
        var filas = new List<FilaPanorama>();
        foreach (var (codigo, nombre) in materias)
        {
            var (consultas, _) = MapeoBanner.ConsultasDe(codigo);
            if (consultas.Count == 0) continue;
            var propios = registros.Where(r => consultas.Any(c => c.Codigo == r.Codigo)).ToList();
            if (propios.Count < consultas.Count) { filas.Add(new FilaPanorama(codigo, nombre, EstadoPanorama.SinConsultar, 0, null)); continue; }
            var cuantas = propios.Sum(r => r.Secciones);
            filas.Add(new FilaPanorama(codigo, nombre, cuantas > 0 ? EstadoPanorama.ConSecciones : EstadoPanorama.SinSecciones, cuantas, propios.Max(r => r.Fecha)));
        }
        return filas;
    }

    // ── Lo guardado ───────────────────────────────────────────────────────────────────────

    /// <summary>Las secciones guardadas de una materia (código del pénsum) en un período, sin ir a Banner.</summary>
    public async Task<VistaSecciones> VerAsync(string codigoPensum, string periodo, CancellationToken ct = default)
    {
        var (consultas, motivo) = MapeoBanner.ConsultasDe(codigoPensum);
        var basico = new VistaSecciones { Codigo = codigoPensum, Periodo = periodo, PeriodoNombre = NombreDePeriodo(periodo), NoConsultable = consultas.Count == 0 ? motivo : null };
        if (consultas.Count == 0) return basico;

        var codigos = consultas.Select(c => c.Codigo).ToList();
        var registros = await _db.ConsultasSecciones.AsNoTracking().Where(c => c.Periodo == periodo && codigos.Contains(c.Codigo)).ToListAsync(ct);
        var filas = (await _db.SeccionesOfertadas.AsNoTracking().Where(s => s.Periodo == periodo).ToListAsync(ct))
            .Where(s => consultas.Any(c => Coincide(c, s.Codigo)))
            .OrderBy(s => s.Codigo).ThenBy(s => s.Seccion, StringComparer.Ordinal).ToList();

        return new VistaSecciones
        {
            Codigo = codigoPensum, Periodo = periodo, PeriodoNombre = basico.PeriodoNombre,
            Secciones = filas.Select(Reconstruir).ToList(),
            // Se considera consultada cuando se consultó cada una de las materias que la componen.
            Consultada = registros.Count == codigos.Count ? registros.Max(r => r.Fecha) : null,
        };
    }

    /// <summary>Períodos (los ya consultados) en que la materia tuvo secciones, del más reciente al más antiguo.</summary>
    public async Task<List<OfertaPrevia>> OfertaPreviaAsync(string codigoPensum, CancellationToken ct = default)
    {
        var (consultas, _) = MapeoBanner.ConsultasDe(codigoPensum);
        if (consultas.Count == 0) return new();
        var codigos = consultas.Select(c => c.Codigo).ToList();
        var filas = await _db.ConsultasSecciones.AsNoTracking().Where(c => codigos.Contains(c.Codigo) && c.Secciones > 0).ToListAsync(ct);
        return filas.GroupBy(f => f.Periodo)
            .Select(g => new OfertaPrevia(g.Key, NombreDePeriodo(g.Key), g.Sum(x => x.Secciones)))
            .OrderByDescending(o => o.Periodo, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Qué profesores han dado la materia según lo consultado en Banner, del que la dio más recientemente al más antiguo.
    /// Solo el nombre publicado y los períodos en que aparece; nada de calificaciones ni opiniones.
    /// </summary>
    public async Task<List<ProfesorDeMateria>> ProfesoresDeAsync(string codigoPensum, CancellationToken ct = default)
    {
        var (consultas, _) = MapeoBanner.ConsultasDe(codigoPensum);
        if (consultas.Count == 0) return new();
        var filas = (await _db.SeccionesOfertadas.AsNoTracking().Where(s => s.Profesor != "").ToListAsync(ct))
            .Where(s => consultas.Any(c => Coincide(c, s.Codigo))).ToList();

        var porProfesor = filas.SelectMany(f => f.Profesor.Split("; ").Select(p => (Nombre: p, f.Periodo)))
            .GroupBy(x => x.Nombre)
            .Select(g => (Nombre: g.Key, Periodos: g.Select(x => x.Periodo).Distinct().OrderByDescending(p => p, StringComparer.Ordinal).ToList()))
            .ToList();
        return porProfesor.OrderByDescending(p => p.Periodos[0], StringComparer.Ordinal).ThenBy(p => p.Nombre, StringComparer.CurrentCultureIgnoreCase)
            .Select(p => new ProfesorDeMateria(p.Nombre, p.Periodos.Select(NombreDePeriodo).ToList())).ToList();
    }

    // ── Mapeo ─────────────────────────────────────────────────────────────────────────────

    private static SeccionOfertada Mapear(SeccionBanner s, DateTime ahora) => new()
    {
        Periodo = s.Periodo, Nrc = s.Nrc, Codigo = s.Codigo, Titulo = s.Titulo, Seccion = s.Seccion, Creditos = s.Creditos,
        Campus = s.Campus, Metodo = s.Metodo,
        Profesor = string.Join("; ", s.Profesores),   // solo nombres: el parser ya descartó correo y matrícula
        CupoMaximo = s.CupoMaximo, Inscritos = s.Inscritos, CuposDisponibles = s.CuposDisponibles, Abierta = s.Abierta,
        BloquesJson = JsonSerializer.Serialize(s.Bloques, Json), Consultada = ahora,
    };

    /// <summary>La sección guardada de vuelta como sección de Banner (con sus bloques de horario).</summary>
    public static SeccionBanner Reconstruir(SeccionOfertada f) => new()
    {
        Periodo = f.Periodo,
        PeriodoDescripcion = NombreDePeriodo(f.Periodo),
        Nrc = f.Nrc, Codigo = f.Codigo, Titulo = f.Titulo, Seccion = f.Seccion, Creditos = f.Creditos,
        Campus = f.Campus, Metodo = f.Metodo,
        Profesores = f.Profesor.Length == 0 ? new() : f.Profesor.Split("; ").ToList(),
        CupoMaximo = f.CupoMaximo, Inscritos = f.Inscritos, CuposDisponibles = f.CuposDisponibles, Abierta = f.Abierta,
        Bloques = JsonSerializer.Deserialize<List<BloqueBanner>>(f.BloquesJson, Json) ?? new(),
    };
}
