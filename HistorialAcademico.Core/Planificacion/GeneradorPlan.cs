using HistorialAcademico.Core.Pensum;

namespace HistorialAcademico.Core.Planificacion;

/// <summary>Cuánto quiere cargar la persona en cada período: ligera (~60 % del límite), normal (~80 %) o pesada (hasta el límite).</summary>
public enum CargaDeseada { Ligera, Normal, Pesada }

/// <summary>Lo que se le pregunta a la persona antes de generar el plan: la carga y los períodos en los que no va a estudiar.</summary>
public record OpcionesGeneracion(CargaDeseada Carga = CargaDeseada.Pesada, IReadOnlySet<PeriodoAcademico>? Omitidos = null)
{
    public static readonly OpcionesGeneracion Ninguna = new();

    /// <summary>
    /// Créditos por período de la carga elegida. Nunca baja de lo que pesa la materia más grande (si no, esa materia no cabría en ningún
    /// período) ni pasa del límite.
    /// </summary>
    public static int LimiteDeCarga(CargaDeseada carga, int limite, int creditosMateriaMayor)
    {
        var deseado = carga switch
        {
            CargaDeseada.Ligera => (int)Math.Round(limite * 0.6, MidpointRounding.AwayFromZero),
            CargaDeseada.Normal => (int)Math.Round(limite * 0.8, MidpointRounding.AwayFromZero),
            _ => limite,
        };
        return Math.Min(limite, Math.Max(deseado, creditosMateriaMayor));
    }
}

public class PlanGenerado
{
    public List<PlanPeriodo> Periodos { get; } = new();
    /// <summary>Una línea corta por materia con el motivo de su período.</summary>
    public Dictionary<string, string> Razones { get; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Materias que no se pudieron ubicar (prerrequisito imposible o más créditos que el límite).</summary>
    public List<string> NoPlanificadas { get; } = new();
}

/// <summary>
/// Plan sugerido: período a período llena el límite de créditos con las materias que ya cumplen sus prerrequisitos
/// y porcentajes (con lo planificado en períodos anteriores), por orden de prioridad.
/// El TFG va siempre en el último período y la pasantía en el cuatrimestre donde está en el pénsum.
/// </summary>
public static class GeneradorPlan
{
    private const int MaximoPeriodos = 40;

    /// <summary>
    /// Genera el plan probando varios desempates entre materias de igual prioridad (por cuatrimestre, por más créditos primero,
    /// por menos créditos primero…) y se queda con el que necesita menos períodos. Así un crédito de diferencia al llenar un
    /// período no retrasa, por ejemplo, el porcentaje que exige el Seminario de Grado.
    /// </summary>
    public static PlanGenerado Generar(ContextoPlan ctx, ResultadoPrioridades prioridades) => Generar(ctx, prioridades, OpcionesGeneracion.Ninguna);

    /// <summary>
    /// Igual, respetando la carga que la persona eligió (menos créditos por período) y saltándose los períodos en los que no va a estudiar:
    /// esos quedan vacíos y el plan sigue en el siguiente.
    /// </summary>
    public static PlanGenerado Generar(ContextoPlan ctx, ResultadoPrioridades prioridades, OpcionesGeneracion opciones)
    {
        var mayor = ctx.Pendientes.Values.Select(m => m.Materia.Creditos).DefaultIfEmpty(0).Max();
        var limite = OpcionesGeneracion.LimiteDeCarga(opciones.Carga, ctx.Limite, mayor);
        if (limite != ctx.Limite) ctx = ctx.ConLimite(limite);
        var omitidos = opciones.Omitidos ?? new HashSet<PeriodoAcademico>();

        var regulares = prioridades.Materias
            .Where(p => !ContextoPlan.EsTfg(p.Materia) && !ContextoPlan.EsPasantia(p.Materia))
            .ToList();   // ya vienen ordenadas por prioridad (variante 0)

        var variantes = new List<List<PrioridadMateria>>
        {
            regulares,
            regulares.OrderByDescending(p => p.Puntos).ThenByDescending(p => p.Materia.Creditos).ThenBy(p => p.Materia.Cuatrimestre).ThenBy(p => p.Materia.Codigo, StringComparer.OrdinalIgnoreCase).ToList(),
            regulares.OrderByDescending(p => p.Puntos).ThenBy(p => p.Materia.Creditos).ThenBy(p => p.Materia.Cuatrimestre).ThenBy(p => p.Materia.Codigo, StringComparer.OrdinalIgnoreCase).ToList(),
            regulares.OrderByDescending(p => p.Puntos).ThenByDescending(p => p.Materia.Cuatrimestre).ThenBy(p => p.Materia.Codigo, StringComparer.OrdinalIgnoreCase).ToList(),
        };

        PlanGenerado? mejor = null;
        foreach (var orden in variantes)
        {
            var candidato = GenerarConOrden(ctx, orden, omitidos);
            // Menos períodos primero; a igualdad, menos materias sin ubicar; a igualdad se conserva la variante anterior (la de prioridad pura).
            if (mejor is null
                || candidato.NoPlanificadas.Count < mejor.NoPlanificadas.Count
                || (candidato.NoPlanificadas.Count == mejor.NoPlanificadas.Count && candidato.Periodos.Count < mejor.Periodos.Count))
                mejor = candidato;
        }
        return mejor!;
    }

    /// <summary>El primer período a partir de <paramref name="desde"/> en el que la persona sí va a estudiar.</summary>
    private static PeriodoAcademico PrimeroActivo(PeriodoAcademico desde, IReadOnlySet<PeriodoAcademico> omitidos)
    {
        var p = desde;
        for (var i = 0; i < MaximoPeriodos && omitidos.Contains(p); i++) p = p.Siguiente();
        return p;
    }

    private static PlanGenerado GenerarConOrden(ContextoPlan ctx, List<PrioridadMateria> regulares, IReadOnlySet<PeriodoAcademico> omitidos)
    {
        var plan = new PlanGenerado();

        var aprobadas = new HashSet<string>(ctx.Aprobadas, StringComparer.OrdinalIgnoreCase);
        var creditos = ctx.CreditosAprobados;
        var colocadas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var candidataDesde = new Dictionary<string, (int Periodo, List<string> Esperaba)>(StringComparer.OrdinalIgnoreCase);
        var indice = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        HashSet<string>? aprobadasPrevias = null;
        var creditosPrevios = 0;

        var j = 0;   // cuántos períodos en los que sí se estudia van; k cuenta también los omitidos
        for (var k = 0; k < MaximoPeriodos && colocadas.Count < regulares.Count; k++)
        {
            if (omitidos.Contains(ctx.Primero.Avanzar(k))) continue;

            var candidatas = regulares
                .Where(p => !colocadas.Contains(p.Materia.Codigo) && ctx.EstaDisponible(p.Materia.Codigo, aprobadas, creditos))
                .ToList();

            foreach (var c in candidatas.Where(c => !candidataDesde.ContainsKey(c.Materia.Codigo)))
                candidataDesde[c.Materia.Codigo] = (j, aprobadasPrevias is null
                    ? new List<string>()
                    : ctx.Faltan(c.Materia.Codigo, aprobadasPrevias, creditosPrevios));

            var periodo = new PlanPeriodo { Periodo = ctx.Primero.Avanzar(k) };
            var incluidas = new List<PrioridadMateria>();
            var excluidas = new List<PrioridadMateria>();
            var usado = 0;
            foreach (var c in candidatas)
            {
                var cr = c.Materia.Creditos;
                if (cr != 0 && usado + cr > ctx.Limite) { excluidas.Add(c); continue; }   // no cabe: se prueba con las siguientes
                incluidas.Add(c);
                usado += cr;
            }
            MejorarRelleno(incluidas, excluidas, usado, ctx.Limite);
            periodo.Codigos.AddRange(candidatas.Where(incluidas.Contains).Select(c => c.Materia.Codigo));
            if (periodo.Codigos.Count == 0) break;   // lo que queda no se puede desbloquear con este plan

            plan.Periodos.Add(periodo);
            aprobadasPrevias = new HashSet<string>(aprobadas, StringComparer.OrdinalIgnoreCase);
            creditosPrevios = creditos;
            foreach (var codigo in periodo.Codigos)
            {
                colocadas.Add(codigo);
                aprobadas.Add(codigo);
                creditos += ctx.PorCodigo[codigo].Materia.Creditos;
                indice[codigo] = j;
            }
            j++;
        }

        // TFG: siempre en el último período; si no cabe por el límite, en uno nuevo a continuación.
        var tfg = ctx.Pendientes.Values.FirstOrDefault(m => ContextoPlan.EsTfg(m.Materia));
        if (tfg is not null)
        {
            if (plan.Periodos.Count == 0) plan.Periodos.Add(new PlanPeriodo { Periodo = PrimeroActivo(ctx.Primero, omitidos) });
            var ultimo = plan.Periodos[^1];
            var usadoUltimo = ultimo.Codigos.Sum(c => ctx.PorCodigo[c].Materia.Creditos);
            if (usadoUltimo + tfg.Materia.Creditos > ctx.Limite)
            {
                ultimo = new PlanPeriodo { Periodo = PrimeroActivo(ultimo.Periodo.Siguiente(), omitidos) };
                plan.Periodos.Add(ultimo);
            }
            ultimo.Codigos.Add(tfg.Materia.Codigo);
            plan.Razones[tfg.Materia.Codigo] = "TFG: siempre va en el último período del plan";
        }

        // Pasantía: sin prerrequisitos; se ubica en el cuatrimestre del pénsum donde está (nivel actual + posición del período).
        foreach (var pas in ctx.Pendientes.Values.Where(m => ContextoPlan.EsPasantia(m.Materia)))
        {
            if (plan.Periodos.Count == 0) plan.Periodos.Add(new PlanPeriodo { Periodo = PrimeroActivo(ctx.Primero, omitidos) });
            var nivelPrimero = ctx.NivelActual + (ctx.Config.AsumirEnCurso ? 1 : 0);   // cuatrimestre que corresponde al primer período
            var destino = Math.Clamp(pas.Materia.Cuatrimestre - nivelPrimero, 0, plan.Periodos.Count - 1);
            plan.Periodos[destino].Codigos.Add(pas.Materia.Codigo);
            plan.Razones[pas.Materia.Codigo] =
                $"Pasantía: se planifica en el cuatrimestre {pas.Materia.Cuatrimestre} del pénsum (no tiene prerrequisitos)";
        }

        AsignarRazones(plan, regulares, indice, candidataDesde);
        return plan;
    }

    /// <summary>
    /// Acerca el período al límite de créditos cambiando una materia incluida por otra excluida con más créditos que
    /// quepa y que no tenga menos prioridad. Nunca baja la prioridad de lo que entra al período.
    /// </summary>
    private static void MejorarRelleno(List<PrioridadMateria> incluidas, List<PrioridadMateria> excluidas, int usado, int limite)
    {
        bool mejoro;
        do
        {
            mejoro = false;
            foreach (var salida in incluidas.Where(i => i.Materia.Creditos > 0).OrderBy(i => i.Puntos).ToList())
            {
                var entrada = excluidas
                    .Where(e => e.Materia.Creditos > salida.Materia.Creditos
                             && usado - salida.Materia.Creditos + e.Materia.Creditos <= limite
                             && e.Puntos >= salida.Puntos)
                    .OrderByDescending(e => e.Materia.Creditos).ThenByDescending(e => e.Puntos).FirstOrDefault();
                if (entrada is null) continue;

                usado += entrada.Materia.Creditos - salida.Materia.Creditos;
                incluidas.Remove(salida); excluidas.Remove(entrada);
                incluidas.Add(entrada); excluidas.Add(salida);
                mejoro = true;
                break;
            }
        } while (mejoro);
    }

    private static void AsignarRazones(PlanGenerado plan, List<PrioridadMateria> regulares, Dictionary<string, int> indice,
        Dictionary<string, (int Periodo, List<string> Esperaba)> candidataDesde)
    {
        // Razones de las demás materias.
        foreach (var p in regulares)
        {
            var codigo = p.Materia.Codigo;
            if (!indice.TryGetValue(codigo, out var k)) { plan.NoPlanificadas.Add(codigo); continue; }

            var texto = p.Motivo;
            if (candidataDesde.TryGetValue(codigo, out var desde))
            {
                if (k > desde.Periodo)
                    texto += " · Pospuesta por el límite de créditos";
                else if (k > 0 && desde.Esperaba.Count > 0)
                    texto += $" · Espera a cumplir {string.Join(" y ", desde.Esperaba)}";
            }
            plan.Razones[codigo] = texto;
        }
    }
}
