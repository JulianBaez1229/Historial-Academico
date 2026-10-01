namespace HistorialAcademico.Core.Planificacion;

public enum TipoAlerta { RutaCritica, Rezagada, SobreLimite, ElectivaPorcentaje, ConProblema }

/// <summary>Lo que hace el botón de una alerta: poner la materia en un período, o sacarla del plan (<see cref="Periodo"/> nulo).</summary>
public record AccionAlerta(string Texto, string Codigo, string? Periodo);

/// <param name="Grave">Bloquea o retrasa el plan (una materia que no puede cursarse donde está, una de ruta crítica sin planificar).</param>
public record AlertaPlan(TipoAlerta Tipo, string Mensaje, AccionAlerta? Accion, bool Grave);

/// <summary>
/// Riesgos de un plan, dichos en claro y con un botón para resolverlos cuando hay una salida que no rompe otra regla:
/// una materia de ruta crítica que podría ir ya y no está, rezagadas sin planificar, períodos por encima del límite de créditos,
/// electivas que todavía no alcanzan el porcentaje que piden, y materias que no pueden cursarse donde están.
/// Cada botón se ofrece solo si el destino pasó por <see cref="ValidadorColocacion"/>: resolver una alerta nunca crea otra.
/// </summary>
public static class AlertasPlan
{
    /// <summary>Cuántos períodos más allá del último usado se buscan como destino.</summary>
    private const int Holgura = 4;

    public static List<AlertaPlan> Calcular(ContextoPlan ctx, IReadOnlyList<PlanPeriodo> plan, EvaluacionPlan evaluacion, ResultadoPrioridades prioridades)
    {
        var alertas = new List<AlertaPlan>();
        if (plan.All(p => p.Codigos.Count == 0)) return alertas;   // sin plan todavía no hay nada que avisar

        var enPlan = new Dictionary<string, PeriodoAcademico>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in plan) foreach (var c in p.Codigos) enPlan.TryAdd(c, p.Periodo);
        var ultimo = plan.Where(p => p.Codigos.Count > 0).Max(p => p.Periodo);
        var hasta = new[] { ultimo, ctx.Primero }.Max().Avanzar(Holgura);
        var avisadas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        IEnumerable<PeriodoAcademico> Periodos(PeriodoAcademico desde)
        {
            for (var p = desde < ctx.Primero ? ctx.Primero : desde; p <= hasta; p = p.Siguiente()) yield return p;
        }

        // El primer período desde el que la materia puede ir, sin pasar el límite (o pasándolo, si no hay otro).
        PeriodoAcademico? Destino(string codigo, PeriodoAcademico desde, bool permitirAviso = false)
        {
            PeriodoAcademico? conAviso = null;
            foreach (var p in Periodos(desde))
            {
                var info = ValidadorColocacion.Evaluar(ctx, plan, codigo, p);
                if (!info.Permitida) continue;
                if (info.Aviso is null) return p;
                conAviso ??= p;
            }
            return permitirAviso ? conAviso : null;
        }

        // 1. Ruta crítica que ya se puede cursar y no está en el primer período.
        foreach (var m in prioridades.Materias.Where(m => m.Etiquetas.Contains("Ruta crítica")))
        {
            var codigo = m.Materia.Codigo;
            if (!ctx.EstaDisponible(codigo, ctx.Aprobadas, ctx.CreditosAprobados)) continue;
            var planificada = enPlan.TryGetValue(codigo, out var donde);
            if (planificada && donde == ctx.Primero) continue;

            var info = ValidadorColocacion.Evaluar(ctx, plan, codigo, ctx.Primero);
            if (planificada && (!info.Permitida || info.Aviso is not null)) continue;   // ya está planificada y no cabe antes: no es un riesgo nuevo

            var cuando = planificada ? $"está en {donde.Nombre}" : "no está en el plan";
            var lleno = info.Permitida && info.Aviso is not null ? $" Ese período está lleno: {info.Aviso}" : "";
            alertas.Add(new AlertaPlan(TipoAlerta.RutaCritica,
                $"{codigo} ({m.Materia.Nombre}) es de ruta crítica y ya la puedes cursar, pero {cuando} y no en {ctx.Primero.Nombre}, el siguiente período.{lleno}",
                info.Permitida ? new AccionAlerta($"{(planificada ? "Mover" : "Agregar")} a {ctx.Primero.Nombre}", codigo, ctx.Primero.Nombre) : null,
                Grave: true));
            avisadas.Add(codigo);
        }

        // 2. Rezagadas sin planificar.
        foreach (var m in prioridades.Materias.Where(m => m.Rezagada && !enPlan.ContainsKey(m.Materia.Codigo) && !avisadas.Contains(m.Materia.Codigo)))
        {
            var codigo = m.Materia.Codigo;
            var destino = Destino(codigo, ctx.Primero, permitirAviso: true);
            alertas.Add(new AlertaPlan(TipoAlerta.Rezagada,
                $"{codigo} ({m.Materia.Nombre}) está rezagada (es del cuatrimestre {m.Materia.Cuatrimestre} y ya vas en el {ctx.NivelActual}) y no está en el plan.",
                destino is null ? null : new AccionAlerta($"Agregar a {destino.Value.Nombre}", codigo, destino.Value.Nombre),
                Grave: false));
            avisadas.Add(codigo);
        }

        // 3. Períodos por encima del límite de créditos: se sugiere mover la materia menos prioritaria que quepa en otro período.
        foreach (var per in evaluacion.Periodos.Where(p => p.SobreLimite))
        {
            var exceso = per.Creditos - per.Limite;
            var candidatas = per.Materias.Where(m => m.EsValida && m.Creditos > 0)
                .OrderBy(m => m.Creditos >= exceso ? 0 : 1)
                .ThenBy(m => prioridades.Buscar(m.Codigo)?.Puntos ?? 0)
                .ThenByDescending(m => m.Creditos);
            AccionAlerta? accion = null;
            foreach (var m in candidatas)
            {
                var destino = Destino(m.Codigo, per.Periodo.Siguiente());
                if (destino is null) continue;
                accion = new AccionAlerta($"Mover {m.Codigo} a {destino.Value.Nombre}", m.Codigo, destino.Value.Nombre);
                break;
            }
            alertas.Add(new AlertaPlan(TipoAlerta.SobreLimite,
                $"{per.Periodo.Nombre} pasa el límite: {per.Creditos} créditos y el máximo es {per.Limite} (sobran {exceso}).", accion, Grave: true));
        }

        // 4 y 5. Materias que no pueden cursarse donde están: electivas que no alcanzan el porcentaje y las demás.
        foreach (var per in evaluacion.Periodos)
            foreach (var m in per.Materias.Where(m => !m.EsValida))
            {
                var esElectiva = ctx.PorCodigo.TryGetValue(m.Codigo, out var estado) && estado.Materia.EsElectiva;
                var porcentaje = m.Problemas.FirstOrDefault(p => p.StartsWith("Requiere ", StringComparison.Ordinal));
                var yaHecha = m.Problemas.Any(p => p.StartsWith("Ya ", StringComparison.Ordinal) || p.StartsWith("No existe", StringComparison.Ordinal));

                AccionAlerta? accion;
                if (yaHecha) accion = new AccionAlerta("Quitar del plan", m.Codigo, null);
                else
                {
                    var destino = Destino(m.Codigo, per.Periodo.Siguiente());
                    accion = destino is null ? null : new AccionAlerta($"Mover a {destino.Value.Nombre}", m.Codigo, destino.Value.Nombre);
                }

                if (esElectiva && porcentaje is not null)
                    alertas.Add(new AlertaPlan(TipoAlerta.ElectivaPorcentaje,
                        $"{m.Codigo} (electiva) todavía no alcanza el porcentaje que pide para {per.Periodo.Nombre}. {porcentaje}", accion, Grave: false));
                else
                    alertas.Add(new AlertaPlan(TipoAlerta.ConProblema,
                        $"{m.Codigo} no puede cursarse en {per.Periodo.Nombre}: {string.Join(" ", m.Problemas)}", accion, Grave: true));
            }

        return alertas.OrderByDescending(a => a.Grave).ThenBy(a => a.Tipo).ToList();
    }
}
