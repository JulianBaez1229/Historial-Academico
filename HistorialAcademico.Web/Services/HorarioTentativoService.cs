using HistorialAcademico.Core.Entities;
using HistorialAcademico.Core.Horarios;
using HistorialAcademico.Web.Data;
using HistorialAcademico.Web.Helpers;
using Microsoft.EntityFrameworkCore;

namespace HistorialAcademico.Web.Services;

/// <summary>Una sección que se puede agregar al horario, con lo que se sabe de sus choques.</summary>
public class OpcionSeccion
{
    public required SeccionBanner Seccion { get; init; }
    public bool Elegida { get; init; }
    /// <summary>Choca con alguna franja en la que no puedo tomar clases: se muestra atenuada y al final de la lista.</summary>
    public bool ChocaConNoDisponible { get; init; }
    /// <summary>Etiquetas de las secciones ya elegidas (de otras materias) con las que choca.</summary>
    public List<string> ChocaCon { get; init; } = new();
}

public record GrupoOpciones(string Codigo, string Nombre, List<OpcionSeccion> Secciones)
{
    public bool TieneElegida => Secciones.Any(s => s.Elegida);
}

/// <summary>Todo lo que la pantalla del horario tentativo necesita.</summary>
public class VistaHorario
{
    public required HorarioTentativo Horario { get; init; }
    public string PeriodoNombre { get; init; } = "";
    public string? PlanNombre { get; init; }
    public List<SeccionBanner> Elegidas { get; init; } = new();
    /// <summary>Secciones elegidas que ya no aparecen en lo consultado de Banner (cambiaron o se quitaron).</summary>
    public List<SeccionElegida> Perdidas { get; init; } = new();
    public required GrillaSemanal Grilla { get; init; }
    public decimal Creditos { get; init; }
    public List<GrupoOpciones> Opciones { get; init; } = new();
    public List<BloqueNoDisponible> NoDisponibles { get; init; } = new();
    public bool HayChoques => Grilla.Choques.Count > 0;
}

/// <summary>Horarios semanales armados a mano con secciones de Banner y las franjas en las que no puedo tomar clases.</summary>
public class HorarioTentativoService
{
    public const int MaxNombre = 60;
    /// <summary>Filas de la cuadrícula de preferencias: de 6:00 a 23:00 (la celda de la hora h cubre h:00 a h+1:00).</summary>
    public const int HoraPrimeraCelda = 6, HoraUltimaCelda = 22;

    private readonly HistorialContext _db;

    public HorarioTentativoService(HistorialContext db) => _db = db;

    // ── Horarios ──────────────────────────────────────────────────────────────────────────

    public Task<List<HorarioTentativo>> ListarAsync(CancellationToken ct = default) =>
        _db.HorariosTentativos.AsNoTracking().Include(h => h.Secciones).OrderBy(h => h.Nombre).ToListAsync(ct);

    /// <summary>Escenarios del planificador a los que se puede asociar un horario.</summary>
    public async Task<List<(int Id, string Nombre)>> PlanesAsync(CancellationToken ct = default) =>
        (await _db.PlanesEstudio.AsNoTracking().OrderBy(p => p.Nombre).Select(p => new { p.Id, p.Nombre }).ToListAsync(ct))
            .Select(p => (p.Id, p.Nombre)).ToList();

    /// <summary>Horarios asociados a un escenario del planificador.</summary>
    public Task<List<HorarioTentativo>> DelPlanAsync(int planId, CancellationToken ct = default) =>
        _db.HorariosTentativos.AsNoTracking().Include(h => h.Secciones).Where(h => h.PlanEstudioId == planId).OrderBy(h => h.Nombre).ToListAsync(ct);

    public async Task<ResultadoOp> CrearAsync(string? nombre, string? periodo, CancellationToken ct = default)
    {
        var error = await ValidarNombreAsync(nombre, null, ct);
        if (error is not null) return new(false, error);
        if (!MapeoBanner.TryPeriodoDeCodigo(periodo, out var p)) return new(false, "Elige un período de Grado para el horario.");

        var h = new HorarioTentativo { Nombre = nombre!.Trim(), Periodo = periodo!, Creado = DateTime.UtcNow, Actualizado = DateTime.UtcNow };
        _db.HorariosTentativos.Add(h);
        await _db.SaveChangesAsync(ct);
        return new(true, $"Horario «{h.Nombre}» creado para {p.Nombre}.", h.Id);
    }

    public async Task<ResultadoOp> RenombrarAsync(int id, string? nombre, CancellationToken ct = default)
    {
        var h = await _db.HorariosTentativos.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (h is null) return new(false, "El horario ya no existe.");
        var error = await ValidarNombreAsync(nombre, id, ct);
        if (error is not null) return new(false, error);
        h.Nombre = nombre!.Trim();
        h.Actualizado = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return new(true, "Nombre actualizado.", id);
    }

    public async Task<ResultadoOp> EliminarAsync(int id, CancellationToken ct = default)
    {
        var h = await _db.HorariosTentativos.Include(x => x.Secciones).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (h is null) return new(false, "El horario ya no existe.");
        _db.HorariosTentativos.Remove(h);
        await _db.SaveChangesAsync(ct);
        return new(true, $"Horario «{h.Nombre}» eliminado.");
    }

    /// <summary>Asocia el horario a un escenario del planificador (o lo desasocia con <c>null</c>).</summary>
    public async Task<ResultadoOp> AsociarPlanAsync(int id, int? planId, CancellationToken ct = default)
    {
        var h = await _db.HorariosTentativos.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (h is null) return new(false, "El horario ya no existe.");
        if (planId is not null)
        {
            var plan = await _db.PlanesEstudio.AsNoTracking().FirstOrDefaultAsync(p => p.Id == planId, ct);
            if (plan is null) return new(false, "El escenario del planificador ya no existe.");
            h.PlanEstudioId = planId;
            h.Actualizado = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            return new(true, $"Horario asociado al escenario «{plan.Nombre}».", id);
        }
        h.PlanEstudioId = null;
        h.Actualizado = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return new(true, "Horario desasociado del escenario.", id);
    }

    private async Task<string?> ValidarNombreAsync(string? nombre, int? idActual, CancellationToken ct)
    {
        var n = nombre?.Trim();
        if (string.IsNullOrEmpty(n)) return "Escribe un nombre para el horario.";
        if (n.Length > MaxNombre) return $"El nombre puede tener como máximo {MaxNombre} caracteres.";
        var existentes = await _db.HorariosTentativos.AsNoTracking().Select(h => new { h.Id, h.Nombre }).ToListAsync(ct);
        return existentes.Any(h => h.Id != idActual && string.Equals(h.Nombre, n, StringComparison.OrdinalIgnoreCase))
            ? $"Ya existe un horario llamado «{n}»." : null;
    }

    // ── Secciones del horario ─────────────────────────────────────────────────────────────

    /// <summary>Agrega la sección (NRC) al horario. Si ya había otra sección de la misma materia, la reemplaza.</summary>
    public async Task<ResultadoOp> AgregarAsync(int id, string? nrc, CancellationToken ct = default)
    {
        var h = await _db.HorariosTentativos.Include(x => x.Secciones).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (h is null) return new(false, "El horario ya no existe.");
        var fila = await _db.SeccionesOfertadas.AsNoTracking().FirstOrDefaultAsync(s => s.Periodo == h.Periodo && s.Nrc == nrc, ct);
        if (fila is null) return new(false, "Esa sección no está entre las consultadas de este período. Consulta la materia en Banner primero.");

        var etiqueta = $"{fila.Codigo}-{fila.Seccion}";
        var previa = h.Secciones.FirstOrDefault(s => s.Codigo == fila.Codigo);
        string mensaje;
        if (previa is null)
        {
            h.Secciones.Add(new SeccionElegida { Nrc = fila.Nrc, Codigo = fila.Codigo, Etiqueta = etiqueta });
            mensaje = $"Agregada {etiqueta}.";
        }
        else if (previa.Nrc == fila.Nrc) return new(true, $"{etiqueta} ya estaba en el horario.", id);
        else
        {
            mensaje = $"Reemplazada {previa.Etiqueta} por {etiqueta}.";
            previa.Nrc = fila.Nrc;
            previa.Etiqueta = etiqueta;
        }
        h.Actualizado = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return new(true, mensaje, id);
    }

    public async Task<ResultadoOp> QuitarAsync(int id, string? nrc, CancellationToken ct = default)
    {
        var h = await _db.HorariosTentativos.Include(x => x.Secciones).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (h is null) return new(false, "El horario ya no existe.");
        var s = h.Secciones.FirstOrDefault(x => x.Nrc == nrc);
        if (s is null) return new(false, "Esa sección ya no estaba en el horario.", id);
        h.Secciones.Remove(s);
        _db.SeccionesElegidas.Remove(s);
        h.Actualizado = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return new(true, $"Quitada {s.Etiqueta}.", id);
    }

    /// <summary>El horario armado: secciones, grilla, choques y las secciones que se pueden agregar (compatibles primero).</summary>
    public async Task<VistaHorario?> ObtenerAsync(int id, CancellationToken ct = default)
    {
        var h = await _db.HorariosTentativos.AsNoTracking().Include(x => x.Secciones).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (h is null) return null;

        var noDisponibles = await NoDisponiblesAsync(ct);
        var filas = await _db.SeccionesOfertadas.AsNoTracking().Where(s => s.Periodo == h.Periodo).ToListAsync(ct);
        var todas = filas.Select(HorariosService.Reconstruir).ToList();

        var elegidas = new List<SeccionBanner>();
        var perdidas = new List<SeccionElegida>();
        foreach (var e in h.Secciones.OrderBy(s => s.Codigo))
        {
            var s = todas.FirstOrDefault(x => x.Nrc == e.Nrc);
            if (s is null) perdidas.Add(e); else elegidas.Add(s);
        }

        var pensum = await _db.MateriasPensum.AsNoTracking().ToDictionaryAsync(m => m.Codigo, m => m.Nombre, StringComparer.OrdinalIgnoreCase, ct);
        var nrcElegidos = elegidas.Select(e => e.Nrc).ToHashSet();

        var grupos = todas.GroupBy(s => s.Codigo).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g =>
        {
            var otras = elegidas.Where(e => e.Codigo != g.Key).ToList();   // lo elegido de las demás materias
            var opciones = g.Select(s => new OpcionSeccion
            {
                Seccion = s,
                Elegida = nrcElegidos.Contains(s.Nrc),
                ChocaConNoDisponible = DetectorChoques.ChocaConNoDisponibles(s, noDisponibles),
                ChocaCon = otras.Where(o => DetectorChoques.Entre(new[] { s, o }).Count > 0).Select(o => $"{o.Codigo}-{o.Seccion}").ToList(),
            })
            // Las compatibles primero; las que chocan con mis horas no disponibles, atenuadas y al final.
            .OrderBy(o => o.ChocaConNoDisponible).ThenBy(o => o.Seccion.Seccion, StringComparer.Ordinal).ToList();
            var nombre = pensum.TryGetValue(g.Key, out var n) ? n : Ui.TituloBonito(g.First().Titulo);
            return new GrupoOpciones(g.Key, nombre, opciones);
        }).ToList();

        string? plan = null;
        if (h.PlanEstudioId is not null)
            plan = await _db.PlanesEstudio.AsNoTracking().Where(p => p.Id == h.PlanEstudioId).Select(p => p.Nombre).FirstOrDefaultAsync(ct);

        return new VistaHorario
        {
            Horario = h,
            PeriodoNombre = HorariosService.NombreDePeriodo(h.Periodo),
            PlanNombre = plan,
            Elegidas = elegidas,
            Perdidas = perdidas,
            Grilla = GrillaSemanal.Construir(elegidas, noDisponibles),
            Creditos = elegidas.Sum(e => e.Creditos),
            Opciones = grupos,
            NoDisponibles = noDisponibles,
        };
    }

    // ── Horas no disponibles ──────────────────────────────────────────────────────────────

    public Task<List<BloqueNoDisponible>> NoDisponiblesAsync(CancellationToken ct = default) =>
        _db.BloquesNoDisponibles.AsNoTracking().OrderBy(b => b.Dia).ThenBy(b => b.DesdeMin).ToListAsync(ct);

    /// <summary>Las celdas (día y hora) marcadas: cada hora cubierta por una franja guardada.</summary>
    public static HashSet<(DiasSemana Dia, int Hora)> Celdas(IEnumerable<BloqueNoDisponible> bloques)
    {
        var celdas = new HashSet<(DiasSemana, int)>();
        foreach (var b in bloques)
            for (var hora = b.DesdeMin / 60; hora * 60 < b.HastaMin; hora++) celdas.Add((b.Dia, hora));
        return celdas;
    }

    /// <summary>Junta las celdas marcadas en franjas: 8, 9 y 10 de un día son una sola franja de 8:00 a 11:00.</summary>
    public static List<BloqueNoDisponible> Franjas(IEnumerable<(DiasSemana Dia, int Hora)> celdas)
    {
        var resultado = new List<BloqueNoDisponible>();
        foreach (var dia in celdas.Select(c => c.Dia).Distinct().OrderBy(d => d))
        {
            var horas = celdas.Where(c => c.Dia == dia).Select(c => c.Hora).Distinct().OrderBy(h => h).ToList();
            var inicio = horas[0];
            var anterior = inicio;
            foreach (var h in horas.Skip(1).Append(int.MaxValue))
            {
                if (h == anterior + 1) { anterior = h; continue; }
                resultado.Add(new BloqueNoDisponible { Dia = dia, DesdeMin = inicio * 60, HastaMin = (anterior + 1) * 60 });
                inicio = anterior = h;
            }
        }
        return resultado;
    }

    /// <summary>Reemplaza las franjas no disponibles por las que se marcaron (una celda = una hora de un día, lunes a domingo).</summary>
    public async Task<ResultadoOp> GuardarNoDisponiblesAsync(IEnumerable<(DiasSemana Dia, int Hora)> celdas, CancellationToken ct = default)
    {
        var validas = celdas.Where(c => c.Hora is >= 0 and <= 23 && c.Dia != DiasSemana.Ninguno && BitOperationsCount(c.Dia) == 1).Distinct().ToList();
        var franjas = Franjas(validas);

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        await _db.BloquesNoDisponibles.ExecuteDeleteAsync(ct);
        _db.BloquesNoDisponibles.AddRange(franjas);
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new(true, franjas.Count == 0 ? "Ya no tienes horas marcadas como no disponibles." : $"Guardadas {validas.Count} horas no disponibles.");
    }

    private static int BitOperationsCount(DiasSemana d) => System.Numerics.BitOperations.PopCount((uint)d);
}
