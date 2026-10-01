using HistorialAcademico.Core.Entities;
using HistorialAcademico.Core.Pensum;

namespace HistorialAcademico.Core.Planificacion;

public class PrioridadMateria
{
    public required EstadoMateriaPensum Estado { get; init; }
    public MateriaPensum Materia => Estado.Materia;

    /// <summary>La cadena más larga de materias que dependen de esta, empezando por ella misma (A → B → C).</summary>
    public required List<string> Cadena { get; init; }
    public int LongitudCadena => Cadena.Count;

    /// <summary>Materias que pasan a estar disponibles al aprobar esta.</summary>
    public required List<string> Desbloquea { get; init; }

    /// <summary>Su cuatrimestre del pénsum es menor que el nivel actual.</summary>
    public bool Rezagada { get; init; }

    /// <summary>Porcentaje de créditos que exige (el mayor), si tiene una regla de porcentaje.</summary>
    public int? PorcentajeRequerido { get; init; }
    /// <summary>Tiene una regla de porcentaje que todavía no se cumple (no se puede adelantar).</summary>
    public bool BloqueadaPorPorcentaje { get; init; }

    public decimal Puntos { get; init; }
    public required List<string> Etiquetas { get; init; }
    public required string Motivo { get; init; }
    /// <summary>Nivel de prioridad para explicarlo: alta, media o baja.</summary>
    public required string Nivel { get; init; }

    /// <summary>Cuándo se cumple el porcentaje ("Ya cumplido", "ENE-ABR 2027"…). Se completa con un plan.</summary>
    public string? PeriodoPorcentaje { get; set; }
}

public class ResultadoPrioridades
{
    public int NivelActual { get; init; }
    /// <summary>Ordenadas de mayor a menor prioridad.</summary>
    public List<PrioridadMateria> Materias { get; } = new();
    public PrioridadMateria? Buscar(string codigo) =>
        Materias.FirstOrDefault(m => string.Equals(m.Materia.Codigo, codigo, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Puntuación de prioridad de cada materia por planificar. Cada componente se explica en el motivo:
/// ruta crítica (10 puntos por cada materia que depende de ella en cadena), materias que desbloquea (3 puntos c/u)
/// y rezagada (+15). Las que tienen un porcentaje pendiente no se pueden adelantar y se marcan.
/// </summary>
public static class CalculadoraPrioridades
{
    public const int PuntosPorPasoDeCadena = 10;
    public const int PuntosPorDesbloqueo = 3;
    public const int PuntosRezagada = 15;

    public static ResultadoPrioridades Calcular(ContextoPlan ctx)
    {
        var pendientes = ctx.Pendientes;

        // Dependientes directos (solo por prerrequisito de materia) entre las materias pendientes.
        var dependientes = pendientes.Keys.ToDictionary(k => k, _ => new List<string>(), StringComparer.OrdinalIgnoreCase);
        foreach (var (codigo, _) in pendientes)
            foreach (var req in ctx.Requisitos(codigo).Where(r => r.Materia is not null))
                if (dependientes.TryGetValue(req.Materia!, out var directos)) directos.Add(codigo);

        var memo = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        List<string> MejorCadena(string codigo, HashSet<string> enCurso)
        {
            if (memo.TryGetValue(codigo, out var hecha)) return hecha;
            List<string> mejor = new();
            if (enCurso.Add(codigo))   // protege contra ciclos en el CSV
            {
                foreach (var d in dependientes[codigo].OrderBy(c => c, StringComparer.OrdinalIgnoreCase))
                {
                    var cadena = MejorCadena(d, enCurso);
                    if (cadena.Count > mejor.Count) mejor = cadena;
                }
                enCurso.Remove(codigo);
            }
            var resultado = new List<string> { codigo };
            resultado.AddRange(mejor);
            return memo[codigo] = resultado;
        }

        var pctBase = ctx.Porcentaje(ctx.CreditosAprobados);
        var r = new ResultadoPrioridades { NivelActual = ctx.NivelActual };
        var lista = new List<PrioridadMateria>();

        foreach (var (codigo, estado) in pendientes)
        {
            var m = estado.Materia;
            var cadena = MejorCadena(codigo, new(StringComparer.OrdinalIgnoreCase));

            // Materias que se desbloquean al aprobarla (con las reglas de planificación: en curso aprobadas si se asume).
            var conEsta = new HashSet<string>(ctx.Aprobadas, StringComparer.OrdinalIgnoreCase) { codigo };
            var creditosConEsta = ctx.CreditosAprobados + m.Creditos;
            var desbloquea = pendientes.Keys
                .Where(o => !string.Equals(o, codigo, StringComparison.OrdinalIgnoreCase)
                         && !ctx.EstaDisponible(o, ctx.Aprobadas, ctx.CreditosAprobados)
                         && ctx.EstaDisponible(o, conEsta, creditosConEsta))
                .OrderBy(o => o, StringComparer.OrdinalIgnoreCase).ToList();

            var rezagada = m.Cuatrimestre < ctx.NivelActual;
            var porcentajes = ctx.Requisitos(codigo).Where(q => q.Porcentaje is not null).Select(q => q.Porcentaje!.Value).ToList();
            int? pct = porcentajes.Count > 0 ? porcentajes.Max() : null;
            var bloqueadaPct = pct is not null && pctBase < pct.Value;

            var puntos = PuntosPorPasoDeCadena * (cadena.Count - 1) + PuntosPorDesbloqueo * desbloquea.Count + (rezagada ? PuntosRezagada : 0);

            var etiquetas = new List<string>();
            if (rezagada) etiquetas.Add("Rezagada");
            if (cadena.Count >= 3) etiquetas.Add("Ruta crítica");
            if (bloqueadaPct) etiquetas.Add("Bloqueada por %");

            var nivel = cadena.Count >= 3 ? "alta" : (cadena.Count == 2 || desbloquea.Count > 0 || rezagada) ? "media" : "baja";

            var partes = new List<string>();
            if (cadena.Count >= 2) partes.Add($"Inicia la cadena {string.Join(" → ", cadena)}");
            if (rezagada) partes.Add($"Rezagada: es del cuatrimestre {m.Cuatrimestre} y ya vas en el {ctx.NivelActual}");
            if (desbloquea.Count > 0) partes.Add($"Desbloquea {desbloquea.Count} materia{(desbloquea.Count == 1 ? "" : "s")}: {string.Join(", ", desbloquea)}");
            if (pct is not null) partes.Add(bloqueadaPct
                ? $"Requiere {pct}% de los créditos: no se puede adelantar"
                : $"Requiere {pct}% de los créditos (ya cumplido)");
            if (partes.Count == 0) partes.Add("Sin dependientes: sirve para completar la carga del período");

            lista.Add(new PrioridadMateria
            {
                Estado = estado, Cadena = cadena, Desbloquea = desbloquea, Rezagada = rezagada,
                PorcentajeRequerido = pct, BloqueadaPorPorcentaje = bloqueadaPct,
                Puntos = puntos, Etiquetas = etiquetas, Nivel = nivel,
                Motivo = $"Prioridad {nivel}: " + string.Join(" · ", partes),
            });
        }

        r.Materias.AddRange(lista.OrderByDescending(p => p.Puntos).ThenBy(p => p.Materia.Cuatrimestre).ThenBy(p => p.Materia.Codigo, StringComparer.OrdinalIgnoreCase));
        return r;
    }

    /// <summary>Con un plan evaluado, indica en qué período se cumple el porcentaje que exige cada materia.</summary>
    public static void CompletarPorcentajes(ResultadoPrioridades prioridades, ContextoPlan ctx, EvaluacionPlan plan)
    {
        foreach (var p in prioridades.Materias.Where(p => p.PorcentajeRequerido is not null))
        {
            var pct = p.PorcentajeRequerido!.Value;
            if (ctx.Porcentaje(ctx.CreditosAprobados) >= pct)
            {
                // Si solo se cumple contando los cursos en progreso, se aclara: hoy todavía no está cumplido.
                p.PeriodoPorcentaje = ctx.Porcentaje(ctx.Base.CreditosAprobados) >= pct ? "Ya cumplido" : "Se cumple al aprobar los cursos en curso";
                continue;
            }
            var cuando = plan.Periodos.FirstOrDefault(e => e.PorcentajeAlInicio >= pct);
            p.PeriodoPorcentaje = cuando is null ? "No se alcanza con este plan" : $"Se estima en {cuando.Periodo.Nombre}";
        }
    }
}
