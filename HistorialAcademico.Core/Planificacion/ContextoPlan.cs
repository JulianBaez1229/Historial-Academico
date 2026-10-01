using HistorialAcademico.Core.Entities;
using HistorialAcademico.Core.Pensum;

namespace HistorialAcademico.Core.Planificacion;

/// <summary>
/// Foto de partida del planificador: qué está aprobado, qué falta y con qué reglas se planifica.
/// Con "asumir que apruebo los cursos en progreso" (por defecto) esos cursos cuentan como aprobados desde el primer
/// período planificable; sin esa opción se tratan como pendientes.
/// </summary>
public class ContextoPlan
{
    public required ResultadoPensum Base { get; init; }
    public required ConfigPlanificador Config { get; init; }
    public required PeriodoAcademico Primero { get; init; }
    public decimal IndiceActual { get; init; }
    public int Limite { get; init; }

    /// <summary>Cuatrimestre más alto con la mayoría de sus materias aprobadas, exentas o en curso.</summary>
    public int NivelActual { get; init; }

    public required HashSet<string> Aprobadas { get; init; }
    public int CreditosAprobados { get; init; }
    public int CreditosTotales => Base.CreditosTotales;

    /// <summary>Materias por planificar, por código.</summary>
    public required Dictionary<string, EstadoMateriaPensum> Pendientes { get; init; }
    public required Dictionary<string, EstadoMateriaPensum> PorCodigo { get; init; }
    private readonly Dictionary<string, List<Requisito>> _requisitos = new(StringComparer.OrdinalIgnoreCase);

    public static ContextoPlan Crear(ResultadoPensum r, ConfigPlanificador config, decimal indiceActual, PeriodoAcademico primero)
    {
        bool CuentaComoAprobada(EstadoMateriaPensum m) =>
            m.CuentaComoAprobada || (config.AsumirEnCurso && m.Estado == EstadoMateria.EnCurso);

        var aprobadas = r.Materias.Where(CuentaComoAprobada).Select(m => m.Materia.Codigo).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ctx = new ContextoPlan
        {
            Base = r,
            Config = config,
            Primero = primero,
            IndiceActual = indiceActual,
            Limite = config.LimiteEfectivo(indiceActual),
            NivelActual = CalcularNivel(r),
            Aprobadas = aprobadas,
            CreditosAprobados = r.Materias.Where(CuentaComoAprobada).Sum(m => m.Materia.Creditos),
            Pendientes = r.Materias.Where(m => !CuentaComoAprobada(m)).ToDictionary(m => m.Materia.Codigo, StringComparer.OrdinalIgnoreCase),
            PorCodigo = r.Materias.ToDictionary(m => m.Materia.Codigo, StringComparer.OrdinalIgnoreCase),
        };
        foreach (var m in r.Materias) ctx._requisitos[m.Materia.Codigo] = PrerrequisitoParser.Parse(m.Materia.Prerrequisitos);
        return ctx;
    }

    /// <summary>El mismo punto de partida pero con otro límite de créditos por período (para planes de carga ligera o normal).</summary>
    public ContextoPlan ConLimite(int limite) => Crear(Base, Config with { LimiteBase = limite, LimiteAlto = limite }, IndiceActual, Primero);

    /// <summary>Nivel actual: el cuatrimestre más alto donde más de la mitad de las materias están aprobadas, exentas o en curso.</summary>
    private static int CalcularNivel(ResultadoPensum r)
    {
        var nivel = 0;
        foreach (var g in r.Materias.GroupBy(m => m.Materia.Cuatrimestre).OrderBy(g => g.Key))
        {
            var hechas = g.Count(m => m.CuentaComoAprobada || m.Estado == EstadoMateria.EnCurso);
            if (hechas * 2 > g.Count()) nivel = g.Key;
        }
        return nivel;
    }

    public List<Requisito> Requisitos(string codigo) => _requisitos.TryGetValue(codigo, out var r) ? r : new();

    public decimal Porcentaje(int creditosAprobados) => CreditosTotales == 0 ? 0 : 100m * creditosAprobados / CreditosTotales;

    public bool Cumple(Requisito req, HashSet<string> aprobadas, int creditosAprobados) =>
        req.Materia is not null ? aprobadas.Contains(req.Materia) : Porcentaje(creditosAprobados) >= req.Porcentaje!.Value;

    public bool EstaDisponible(string codigo, HashSet<string> aprobadas, int creditosAprobados) =>
        Requisitos(codigo).All(r => Cumple(r, aprobadas, creditosAprobados));

    /// <summary>Requisitos que faltan en ese momento, en texto ("ISO720", "67% de créditos").</summary>
    public List<string> Faltan(string codigo, HashSet<string> aprobadas, int creditosAprobados) =>
        Requisitos(codigo).Where(r => !Cumple(r, aprobadas, creditosAprobados))
            .Select(r => r.Materia ?? $"{r.Porcentaje}% de créditos").ToList();

    public static bool EsTfg(MateriaPensum m) => string.Equals(m.Codigo, "TFG", StringComparison.OrdinalIgnoreCase);

    /// <summary>La pasantía (0 créditos, sin prerrequisitos): se ubica en el cuatrimestre donde está en el pénsum.</summary>
    public static bool EsPasantia(MateriaPensum m) =>
        m.Creditos == 0 && m.Nombre.Contains("Pasant", StringComparison.OrdinalIgnoreCase);
}
