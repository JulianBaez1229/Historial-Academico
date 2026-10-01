namespace HistorialAcademico.Core.Planificacion;

/// <summary>
/// Qué pasaría si una materia se pone en un período: <see cref="Motivos"/> vacío = puede ir; con motivos no puede (prerrequisito o porcentaje
/// que no se cumple, período pasado…). <see cref="Aviso"/> es algo que se puede hacer pero conviene saber (pasar el límite de créditos).
/// </summary>
public record ColocacionInfo(IReadOnlyList<string> Motivos, string? Aviso = null)
{
    public bool Permitida => Motivos.Count == 0;
}

/// <summary>
/// Responde, para el tablero del planificador, dónde SÍ y dónde NO puede ir una materia (con el motivo), usando las mismas reglas que
/// <see cref="EvaluadorPlan"/>: los prerrequisitos y porcentajes se comprueban con lo aprobado y lo planificado en períodos anteriores
/// (nunca el mismo). Además se cuida lo inverso: una materia no puede quedar en el mismo período que otra que la necesita, ni después.
/// </summary>
public static class ValidadorColocacion
{
    /// <param name="plan">El plan tal como está ahora (la materia puede estar en él: se ignora su lugar actual).</param>
    public static ColocacionInfo Evaluar(ContextoPlan ctx, IEnumerable<PlanPeriodo> plan, string codigo, PeriodoAcademico periodo)
    {
        var motivos = new List<string>();
        var otros = plan
            .Select(p => (p.Periodo, Codigos: p.Codigos.Where(c => !string.Equals(c, codigo, StringComparison.OrdinalIgnoreCase)).ToList()))
            .ToList();

        if (periodo < ctx.Primero)
            motivos.Add($"Este período ya pasó o está en curso (el primero planificable es {ctx.Primero.Nombre}).");

        // Lo aprobado y lo planificado ANTES de este período.
        var aprobadas = new HashSet<string>(ctx.Aprobadas, StringComparer.OrdinalIgnoreCase);
        var creditos = ctx.CreditosAprobados;
        var ubicacion = new Dictionary<string, PeriodoAcademico>(StringComparer.OrdinalIgnoreCase);
        foreach (var (p, codigos) in otros)
            foreach (var c in codigos)
            {
                ubicacion.TryAdd(c, p);
                if (p >= periodo) continue;
                if (aprobadas.Add(c) && ctx.PorCodigo.TryGetValue(c, out var e)) creditos += e.Materia.Creditos;
            }

        var porcentaje = ctx.Porcentaje(creditos);
        foreach (var req in ctx.Requisitos(codigo))
        {
            if (ctx.Cumple(req, aprobadas, creditos)) continue;
            if (req.Materia is not null)
            {
                motivos.Add(ubicacion.TryGetValue(req.Materia, out var donde)
                    ? donde == periodo
                        ? $"{req.Materia} está planificada en este mismo período y debe ir en uno anterior."
                        : donde > periodo
                            ? $"{req.Materia} está planificada después ({donde.Nombre}); debe ir antes."
                            : $"Falta aprobar {req.Materia}."
                    : $"Falta {req.Materia}: no está aprobada ni planificada antes.");
            }
            else
            {
                motivos.Add(FormattableString.Invariant(
                    $"Requiere {req.Porcentaje}% de los créditos; al inicio de {periodo.Nombre} llevarás {porcentaje:0.0}%."));
            }
        }

        // Lo inverso: las materias ya planificadas que la necesitan no pueden quedar en este período ni antes.
        foreach (var (p, codigos) in otros.Where(o => o.Periodo <= periodo))
            foreach (var d in codigos.Where(d => ctx.Requisitos(d).Any(r => r.Materia is not null && string.Equals(r.Materia, codigo, StringComparison.OrdinalIgnoreCase))))
                motivos.Add(p == periodo
                    ? $"{d} está planificada en este período y la necesita: debe ir después de {codigo}."
                    : $"{d} ({p.Nombre}) la necesita y quedaría antes que {codigo}.");

        // El TFG cierra el plan.
        if (ctx.PorCodigo.TryGetValue(codigo, out var estado) && ContextoPlan.EsTfg(estado.Materia)
            && otros.Any(o => o.Periodo > periodo && o.Codigos.Count > 0))
            motivos.Add("El TFG debe ir en el último período del plan.");

        string? aviso = null;
        if (motivos.Count == 0 && estado is not null)
        {
            var usados = otros.Where(o => o.Periodo == periodo).SelectMany(o => o.Codigos)
                .Sum(c => ctx.PorCodigo.TryGetValue(c, out var x) ? x.Materia.Creditos : 0);
            if (usados + estado.Materia.Creditos > ctx.Limite)
                aviso = $"Pasaría el límite: {usados + estado.Materia.Creditos} créditos y el máximo es {ctx.Limite}.";
        }
        return new ColocacionInfo(motivos, aviso);
    }
}
