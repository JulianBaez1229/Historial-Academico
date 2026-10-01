using HistorialAcademico.Core.Entities;
using HistorialAcademico.Core.Universidad;

namespace HistorialAcademico.Core.Pensum;

public enum EstadoMateria
{
    /// <summary>Calificación A, B, C o D (directa o por equivalencia).</summary>
    Aprobada,
    /// <summary>Calificación E: cuenta como aprobada, no entra en el índice.</summary>
    Exenta,
    EnCurso,
    /// <summary>Todos sus prerrequisitos (materias y porcentajes) están cumplidos.</summary>
    Disponible,
    Bloqueada,
}

public class EstadoMateriaPensum
{
    public required MateriaPensum Materia { get; init; }
    /// <summary>Hasta que el motor decide, una materia sin nada a favor es Bloqueada (nunca Aprobada por omisión).</summary>
    public EstadoMateria Estado { get; set; } = EstadoMateria.Bloqueada;
    public string? Calificacion { get; set; }
    public string? Periodo { get; set; }
    /// <summary>Código de Banner cuando la aprobación viene de una equivalencia (p. ej. ING701).</summary>
    public string? PorEquivalencia { get; set; }
    /// <summary>Qué le falta, si está bloqueada.</summary>
    public List<string> Bloqueos { get; } = new();

    public bool CuentaComoAprobada => Estado is EstadoMateria.Aprobada or EstadoMateria.Exenta;
}

public class ResultadoPensum
{
    public List<EstadoMateriaPensum> Materias { get; } = new();
    public int CreditosTotales { get; set; }
    /// <summary>Créditos de materias Aprobadas o Exentas.</summary>
    public int CreditosAprobados { get; set; }
    public int CreditosEnCurso { get; set; }
    public int CreditosFaltantes => CreditosTotales - CreditosAprobados - CreditosEnCurso;
    public decimal PorcentajeAprobado => CreditosTotales == 0 ? 0 : 100m * CreditosAprobados / CreditosTotales;

    public EstadoMateriaPensum? Buscar(string codigo) =>
        Materias.FirstOrDefault(m => string.Equals(m.Materia.Codigo, codigo, StringComparison.OrdinalIgnoreCase));

    private Dictionary<string, List<string>>? _dependientes;

    /// <summary>Códigos de las materias que tienen a esta como prerrequisito directo (las que desbloquea), por cuatrimestre y código.</summary>
    public IReadOnlyList<string> Desbloquea(string codigo)
    {
        _dependientes ??= Materias
            .OrderBy(m => m.Materia.Cuatrimestre).ThenBy(m => m.Materia.Codigo, StringComparer.OrdinalIgnoreCase)
            .SelectMany(m => PrerrequisitoParser.Parse(m.Materia.Prerrequisitos)
                .Where(r => r.Materia is not null)
                .Select(r => (Previa: r.Materia!, Dependiente: m.Materia.Codigo)))
            .GroupBy(x => x.Previa, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Dependiente).ToList(), StringComparer.OrdinalIgnoreCase);
        return _dependientes.TryGetValue(codigo, out var lista) ? lista : Array.Empty<string>();
    }

    /// <summary>Códigos de las materias que son prerrequisito directo de esta (sin contar las reglas de porcentaje).</summary>
    public IReadOnlyList<string> Requisitos(string codigo) =>
        PrerrequisitoParser.Parse(Buscar(codigo)?.Materia.Prerrequisitos)
            .Where(r => r.Materia is not null).Select(r => r.Materia!).ToList();
}

/// <summary>
/// Marca el estado de cada materia del pénsum a partir de lo cursado, lo que está en progreso y las equivalencias.
/// Una aprobación directa (la materia misma) tiene prioridad sobre una por equivalencia.
/// Los prerrequisitos solo se dan por cumplidos con materias Aprobadas/Exentas: una en curso todavía no cuenta.
/// </summary>
public static class MotorEstadoPensum
{
    public static ResultadoPensum Calcular(
        IEnumerable<MateriaPensum> pensum,
        IEnumerable<MateriaCursada> cursadas,
        IEnumerable<CursoEnProgreso> enProgreso,
        IEnumerable<Equivalencia> equivalencias,
        EscalaCalificaciones? escala = null)
    {
        escala ??= EscalaCalificaciones.Unapec;
        var materias = pensum.ToList();
        var cursos = cursadas.ToList();
        var progreso = enProgreso.ToList();
        var mapa = equivalencias
            .Where(e => e.CodigoPensum is not null)
            .GroupBy(e => e.CodigoBanner.ToUpperInvariant())
            .ToDictionary(g => g.Key, g => g.Select(e => e.CodigoPensum!.ToUpperInvariant()).ToHashSet());

        bool Equivale(string codigoBanner, string codigoPensum) =>
            mapa.TryGetValue(codigoBanner.ToUpperInvariant(), out var destinos) && destinos.Contains(codigoPensum.ToUpperInvariant());

        var r = new ResultadoPensum { CreditosTotales = materias.Sum(m => m.Creditos) };

        // ── Pasada 1: aprobada / exenta / en curso ─────────────────────────────────────────
        var pendientes = new List<EstadoMateriaPensum>();   // ni aprobadas ni en curso: se deciden en la pasada 2
        foreach (var m in materias)
        {
            var e = new EstadoMateriaPensum { Materia = m };

            var directa = Mejor(cursos.Where(c => Igual(c.Codigo, m.Codigo)), escala);
            var porEq = directa is null ? Mejor(cursos.Where(c => !Igual(c.Codigo, m.Codigo) && Equivale(c.Codigo, m.Codigo)), escala) : null;
            var ganada = directa ?? porEq;

            if (ganada is not null)
            {
                e.Estado = escala.EsExenta(ganada.Calificacion) ? EstadoMateria.Exenta : EstadoMateria.Aprobada;
                e.Calificacion = ganada.Calificacion;
                e.Periodo = ganada.Periodo?.Nombre;
                if (porEq is not null) e.PorEquivalencia = ganada.Codigo;
            }
            else if (progreso.FirstOrDefault(c => Igual(c.Codigo, m.Codigo) || Equivale(c.Codigo, m.Codigo)) is { } enCurso)
            {
                e.Estado = EstadoMateria.EnCurso;
                e.Periodo = enCurso.Periodo;
                if (!Igual(enCurso.Codigo, m.Codigo)) e.PorEquivalencia = enCurso.Codigo;
            }
            else pendientes.Add(e);
            r.Materias.Add(e);
        }

        r.CreditosAprobados = r.Materias.Where(e => e.CuentaComoAprobada).Sum(e => e.Materia.Creditos);
        r.CreditosEnCurso = r.Materias.Where(e => e.Estado == EstadoMateria.EnCurso).Sum(e => e.Materia.Creditos);

        // ── Pasada 2: disponible / bloqueada ───────────────────────────────────────────────
        var porCodigo = r.Materias.ToDictionary(e => e.Materia.Codigo, StringComparer.OrdinalIgnoreCase);
        foreach (var e in pendientes)
        {
            foreach (var req in PrerrequisitoParser.Parse(e.Materia.Prerrequisitos))
            {
                if (req.Materia is not null)
                {
                    if (!porCodigo.TryGetValue(req.Materia, out var previa))
                        e.Bloqueos.Add($"Falta {req.Materia} (no está en el pénsum).");
                    else if (!previa.CuentaComoAprobada)
                        e.Bloqueos.Add(previa.Estado == EstadoMateria.EnCurso
                            ? $"Falta aprobar {req.Materia} (en curso)."
                            : $"Falta aprobar {req.Materia}.");
                }
                else if (r.PorcentajeAprobado < req.Porcentaje!.Value)
                {
                    e.Bloqueos.Add(FormattableString.Invariant(
                        $"Requiere {req.Porcentaje}% de créditos aprobados (llevas {r.PorcentajeAprobado:0.0}%)."));
                }
            }
            e.Estado = e.Bloqueos.Count == 0 ? EstadoMateria.Disponible : EstadoMateria.Bloqueada;
        }
        return r;
    }

    private static bool Igual(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>Mejor intento que cuenta como aprobado: la mejor letra que aprueba; si solo hay una exenta, esa; si solo hay reprobadas, nada.</summary>
    private static MateriaCursada? Mejor(IEnumerable<MateriaCursada> intentos, EscalaCalificaciones escala)
    {
        MateriaCursada? mejor = null;
        foreach (var c in intentos.Where(c => escala.CuentaComoAprobada(c.Calificacion)))
            if (mejor is null || Valor(c, escala) > Valor(mejor, escala)) mejor = c;
        return mejor;
    }

    private static int Valor(MateriaCursada c, EscalaCalificaciones escala) => escala.PuntosPorLetra(c.Calificacion) ?? 0;
}
