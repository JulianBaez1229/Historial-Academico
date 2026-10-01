using HistorialAcademico.Core.Pensum;

namespace HistorialAcademico.Core.Planificacion;

/// <summary>Materias asignadas a un período futuro.</summary>
public class PlanPeriodo
{
    public PeriodoAcademico Periodo { get; init; }
    public List<string> Codigos { get; init; } = new();
}

public class EvaluacionMateria
{
    public required string Codigo { get; init; }
    public required string Nombre { get; init; }
    public int Creditos { get; init; }
    /// <summary>Por qué no se puede cursar en este período (vacío si todo está bien).</summary>
    public List<string> Problemas { get; } = new();
    public bool EsValida => Problemas.Count == 0;
}

public class EvaluacionPeriodo
{
    public PeriodoAcademico Periodo { get; init; }
    public List<EvaluacionMateria> Materias { get; } = new();
    public int Creditos { get; set; }
    public int Limite { get; init; }
    public bool SobreLimite { get; set; }
    public bool BajoMinimo { get; set; }
    /// <summary>Porcentaje de créditos del pénsum aprobados proyectado al inicio de este período.</summary>
    public decimal PorcentajeAlInicio { get; set; }
    /// <summary>Alertas del período completo (límite, mínimo, período ya pasado…).</summary>
    public List<string> Alertas { get; } = new();
    public bool EsValido => Alertas.Count == 0 && Materias.All(m => m.EsValida);
}

public class EvaluacionPlan
{
    public List<EvaluacionPeriodo> Periodos { get; } = new();
    /// <summary>Problemas del plan que no son de un período en concreto (materias repetidas, TFG fuera del último período…).</summary>
    public List<string> Errores { get; } = new();

    public List<string> SinPlanificar { get; } = new();
    public int CreditosFaltantes { get; set; }
    public int CreditosSinPlanificar { get; set; }
    public int CuatrimestresPlanificados { get; set; }
    public PeriodoAcademico? Graduacion { get; set; }
    /// <summary>La graduación es una estimación porque todavía hay materias sin planificar.</summary>
    public bool GraduacionEstimada { get; set; }
    public bool TodoPlanificado => SinPlanificar.Count == 0;

    /// <summary>Períodos con materias (los vacíos no cuentan para los promedios).</summary>
    private IEnumerable<EvaluacionPeriodo> ConMaterias => Periodos.Where(p => p.Materias.Count > 0);

    /// <summary>Créditos por período, promediando solo los períodos que tienen materias (0 si el plan está vacío).</summary>
    public decimal PromedioCreditos => ConMaterias.Any() ? Math.Round((decimal)ConMaterias.Average(p => p.Creditos), 1, MidpointRounding.AwayFromZero) : 0;

    /// <summary>El período con más créditos (el primero si hay empate); null si el plan está vacío.</summary>
    public EvaluacionPeriodo? PeriodoMasPesado => ConMaterias.OrderByDescending(p => p.Creditos).ThenBy(p => p.Periodo).FirstOrDefault();
    public bool EsValido => Errores.Count == 0 && Periodos.All(p => p.EsValido);
    public int Problemas => Errores.Count + Periodos.Sum(p => p.Alertas.Count + p.Materias.Sum(m => m.Problemas.Count));
}

/// <summary>
/// Valida un plan en tiempo real. Los prerrequisitos y porcentajes de cada materia se comprueban con lo aprobado,
/// lo que está en curso (si se asume que se aprueba) y lo planificado en períodos <b>anteriores</b>: nunca el mismo período.
/// </summary>
public static class EvaluadorPlan
{
    public static EvaluacionPlan Evaluar(ContextoPlan ctx, IEnumerable<PlanPeriodo> plan)
    {
        var periodos = plan.OrderBy(p => p.Periodo).ToList();
        var resultado = new EvaluacionPlan { CreditosFaltantes = ctx.Pendientes.Values.Sum(m => m.Materia.Creditos) };

        // Dónde está planificada cada materia (para explicar "está en este mismo período / después").
        var ubicacion = new Dictionary<string, PeriodoAcademico>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in periodos)
            foreach (var c in p.Codigos)
                ubicacion.TryAdd(c, p.Periodo);

        var aprobadas = new HashSet<string>(ctx.Aprobadas, StringComparer.OrdinalIgnoreCase);
        var creditosAprobados = ctx.CreditosAprobados;
        var colocadas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ultimoConMaterias = periodos.LastOrDefault(p => p.Codigos.Count > 0);

        foreach (var p in periodos)
        {
            var ev = new EvaluacionPeriodo
            {
                Periodo = p.Periodo,
                Limite = ctx.Limite,
                PorcentajeAlInicio = ctx.Porcentaje(creditosAprobados),
            };
            if (p.Periodo < ctx.Primero)
                ev.Alertas.Add($"Este período ya pasó o está en curso (el primero planificable es {ctx.Primero.Nombre}).");

            var validas = new List<EstadoMateriaPensum>();
            foreach (var codigo in p.Codigos)
            {
                ctx.PorCodigo.TryGetValue(codigo, out var estado);
                var em = new EvaluacionMateria
                {
                    Codigo = codigo.ToUpperInvariant(),
                    Nombre = estado?.Materia.Nombre ?? "(no existe en el pénsum)",
                    Creditos = estado?.Materia.Creditos ?? 0,
                };
                ev.Materias.Add(em);

                if (estado is null) { em.Problemas.Add("No existe en el pénsum."); continue; }
                if (!ctx.Pendientes.ContainsKey(codigo))
                {
                    em.Problemas.Add(estado.Estado == EstadoMateria.EnCurso ? "Ya la estás cursando." : "Ya está aprobada.");
                    continue;
                }
                if (!colocadas.Add(codigo))
                {
                    em.Problemas.Add("Está repetida en el plan.");
                    continue;
                }

                foreach (var req in ctx.Requisitos(codigo))
                {
                    if (ctx.Cumple(req, aprobadas, creditosAprobados)) continue;
                    if (req.Materia is not null)
                    {
                        em.Problemas.Add(ubicacion.TryGetValue(req.Materia, out var donde)
                            ? donde == p.Periodo
                                ? $"{req.Materia} está en este mismo período y debe ir en uno anterior."
                                : donde > p.Periodo
                                    ? $"{req.Materia} está planificada después ({donde.Nombre}); debe ir antes."
                                    : $"Falta aprobar {req.Materia}."
                            : $"Falta {req.Materia}: no está aprobada ni planificada antes.");
                    }
                    else
                    {
                        em.Problemas.Add(FormattableString.Invariant(
                            $"Requiere {req.Porcentaje}% de los créditos; al inicio de este período llevarás {ev.PorcentajeAlInicio:0.0}%."));
                    }
                }
                validas.Add(estado);
            }

            ev.Creditos = validas.Sum(m => m.Materia.Creditos);
            if (ev.Creditos > ctx.Limite)
            {
                ev.SobreLimite = true;
                ev.Alertas.Add($"Pasa el límite: {ev.Creditos} créditos y el máximo es {ctx.Limite}.");
            }
            if (ctx.Config.Minimo > 0 && p.Codigos.Count > 0 && ev.Creditos < ctx.Config.Minimo && p != ultimoConMaterias)
            {
                ev.BajoMinimo = true;
                ev.Alertas.Add($"Queda por debajo del mínimo: {ev.Creditos} créditos y el mínimo es {ctx.Config.Minimo}.");
            }

            resultado.Periodos.Add(ev);
            foreach (var m in validas) aprobadas.Add(m.Materia.Codigo);
            creditosAprobados += ev.Creditos;
        }

        // TFG: siempre en el último período con materias.
        if (ultimoConMaterias is not null)
            foreach (var p in periodos.Where(p => p != ultimoConMaterias && p.Codigos.Any(c => string.Equals(c, "TFG", StringComparison.OrdinalIgnoreCase))))
                resultado.Errores.Add($"El TFG debe ir en el último período del plan, no en {p.Periodo.Nombre}.");

        resultado.SinPlanificar.AddRange(ctx.Pendientes.Keys
            .Where(c => !colocadas.Contains(c))
            .OrderBy(c => ctx.PorCodigo[c].Materia.Cuatrimestre).ThenBy(c => c, StringComparer.OrdinalIgnoreCase));
        resultado.CreditosSinPlanificar = resultado.SinPlanificar.Sum(c => ctx.PorCodigo[c].Materia.Creditos);
        resultado.CuatrimestresPlanificados = periodos.Count(p => p.Codigos.Count > 0);

        if (ctx.Pendientes.Count > 0)
        {
            var ultimo = ultimoConMaterias?.Periodo;
            if (resultado.SinPlanificar.Count == 0 && ultimo is not null)
            {
                resultado.Graduacion = ultimo;
            }
            else
            {
                // Estimación: lo que falta por planificar, al ritmo del límite de créditos, a continuación del plan.
                var extra = Math.Max(1, (int)Math.Ceiling(resultado.CreditosSinPlanificar / (double)Math.Max(1, ctx.Limite)));
                resultado.Graduacion = (ultimo ?? ctx.Primero.Avanzar(-1)).Avanzar(extra);
                resultado.GraduacionEstimada = true;
            }
        }
        return resultado;
    }
}
