using HistorialAcademico.Core.Entities;
using HistorialAcademico.Core.Indice;
using HistorialAcademico.Core.Pensum;
using HistorialAcademico.Core.Planificacion;
using HistorialAcademico.Core.Universidad;
using HistorialAcademico.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace HistorialAcademico.Web.Services;

public record ResultadoOp(bool Ok, string Mensaje, int? Id = null);

/// <summary>Cuatrimestres que faltan y período estimado de graduación según el plan sugerido (0 y null si no falta nada).</summary>
public record EstimacionGraduacion(int Cuatrimestres, PeriodoAcademico? Graduacion, string? PlanActivo = null);

/// <summary>Una materia del simulador: todavía sin nota real (en curso o planificada) y con la letra que se espera sacar.</summary>
public record FilaSimuladorVista(string Codigo, string Nombre, decimal Creditos, string Grupo, string? Letra);

/// <summary>Lo que necesita el simulador de índice para dibujarse y recalcular en el navegador.</summary>
public class DatosSimulador
{
    public required List<FilaSimuladorVista> Filas { get; init; }
    /// <summary>Lo acumulado hasta hoy: horas que cuentan para el índice y puntos de calidad.</summary>
    public required TotalesIndice Actual { get; init; }
    /// <summary>Créditos de las materias que faltan y ni están en curso ni en el plan (cuentan para «lo que falta»).</summary>
    public decimal CreditosSinFila { get; init; }
    public int MateriasSinFila { get; init; }
    public decimal Objetivo { get; init; }
    public required ResultadoSimulacion Resultado { get; init; }
    public required HistorialAcademico.Core.Universidad.EscalaCalificaciones Escala { get; init; }
}

/// <summary>Cómo quedaría un plan sugerido con cada carga: créditos por período, cuatrimestres y graduación estimada.</summary>
public record PrevisionCarga(CargaDeseada Carga, int Limite, int Cuatrimestres, PeriodoAcademico? Graduacion);

/// <summary>Todo lo que la pantalla del planificador necesita para un escenario.</summary>
public class EstadoPlanificador
{
    public required EstadoAcademico Estado { get; init; }
    public required ConfigPlanificador Config { get; init; }
    public required List<PlanEstudio> Planes { get; init; }
    public PlanEstudio? Actual { get; init; }

    /// <summary>Si no se puede planificar (falta el histórico, el pénsum o nada por planificar), el motivo.</summary>
    public string? NoPuedePlanificar { get; init; }
    public ContextoPlan? Contexto { get; init; }
    public ResultadoPrioridades? Prioridades { get; init; }

    public List<PlanPeriodo> Plan { get; init; } = new();
    public EvaluacionPlan? Evaluacion { get; init; }
    /// <summary>Motivo de cada materia del plan sugerido (código → texto).</summary>
    public Dictionary<string, string> Razones { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Períodos que se muestran como columnas: del primero planificable hasta uno vacío después del último usado.</summary>
    public List<PeriodoAcademico> Columnas { get; init; } = new();
    /// <summary>Materias que faltan y todavía no están en este plan, por prioridad.</summary>
    public List<PrioridadMateria> PorPlanificar { get; init; } = new();

    /// <summary>
    /// Dónde no puede ir cada materia (o dónde iría con un aviso): código → período → qué pasaría. Solo trae lo que NO es libre;
    /// lo que falta aquí puede ir sin problema. Es lo que atenúa las columnas mientras se arrastra una tarjeta.
    /// </summary>
    public Dictionary<string, Dictionary<string, ColocacionInfo>> Colocaciones { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Lo que cambió en los planes por sí solo (tras sincronizar) y todavía no se ha descartado, lo más reciente primero.</summary>
    public List<AvisoPlan> Avisos { get; init; } = new();

    /// <summary>El simulador de índice con las notas que se espera sacar (null si no se puede planificar).</summary>
    public DatosSimulador? Simulador { get; init; }

    /// <summary>Riesgos del plan con su botón para resolverlos (vacío si no hay plan o no hay riesgos).</summary>
    public List<AlertaPlan> Alertas { get; init; } = new();

    /// <summary>Lo que resultaría de generar el plan con cada carga (para la pregunta previa).</summary>
    public List<PrevisionCarga> Previsiones { get; init; } = new();

    /// <summary>Los próximos períodos que se ofrecen para marcar «no voy a estudiar».</summary>
    public List<PeriodoAcademico> PeriodosOmitibles { get; init; } = new();

    /// <summary><see cref="Colocaciones"/> en JSON compacto para el tablero: {"ISO200":{"ENE-ABR 2027":{"m":["motivo"],"a":"aviso"}}}.</summary>
    public string ColocacionesJson => System.Text.Json.JsonSerializer.Serialize(
        Colocaciones.ToDictionary(c => c.Key, c => c.Value.ToDictionary(p => p.Key, p => new { m = p.Value.Motivos, a = p.Value.Aviso })),
        new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
}

public class PlanificadorService
{
    private readonly HistorialContext _db;
    private readonly AcademicoService _academico;

    public PlanificadorService(HistorialContext db, AcademicoService academico)
    {
        _db = db;
        _academico = academico;
    }

    // ── Configuración ─────────────────────────────────────────────────────────────────────

    /// <summary>La configuración guardada; si nunca se guardó una, los límites de la universidad activa (universidad.json).</summary>
    public async Task<ConfigPlanificador> ObtenerConfigAsync(CancellationToken ct = default) =>
        (await _db.ConfiguracionPlanificador.AsNoTracking().FirstOrDefaultAsync(ct))?.ToConfig() ?? _academico.Reglas.ConfigPorDefecto;

    public async Task<ResultadoOp> GuardarConfigAsync(ConfigPlanificador c, CancellationToken ct = default)
    {
        if (c.LimiteBase is < 1 or > 60) return new(false, "El límite de créditos debe estar entre 1 y 60.");
        if (c.LimiteAlto < c.LimiteBase || c.LimiteAlto > 60) return new(false, "El límite con índice alto debe ser mayor o igual al límite normal (y como máximo 60).");
        var maximoIndice = _academico.Reglas.Escala.PuntosMaximos;
        if (c.UmbralIndice < 0 || c.UmbralIndice > maximoIndice) return new(false, $"El índice mínimo para el límite alto debe estar entre 0 y {maximoIndice}.");
        if (c.Minimo < 0 || c.Minimo > c.LimiteBase) return new(false, "El mínimo de créditos no puede ser negativo ni mayor que el límite.");

        var fila = await _db.ConfiguracionPlanificador.FirstOrDefaultAsync(ct);
        if (fila is null) _db.ConfiguracionPlanificador.Add(fila = new ConfiguracionPlanificador());
        fila.Aplicar(c);
        await _db.SaveChangesAsync(ct);
        return new(true, "Configuración guardada.");
    }

    // ── Estado de un escenario ────────────────────────────────────────────────────────────

    public async Task<EstadoPlanificador> ObtenerAsync(int? planId = null, CancellationToken ct = default)
    {
        var estado = await _academico.ObtenerAsync(ct);
        var config = await ObtenerConfigAsync(ct);

        if (!await _db.PlanesEstudio.AnyAsync(ct))
        {
            _db.PlanesEstudio.Add(new PlanEstudio { Nombre = "Plan 1", Creado = DateTime.UtcNow, Actualizado = DateTime.UtcNow });
            await _db.SaveChangesAsync(ct);
        }
        var ctx = CrearContexto(estado, config, out var motivo);
        // Antes de mostrar nada se ajustan los planes a lo que pasó (una sincronización, por ejemplo): lo aprobado sale del plan.
        if (ctx is not null) await ReconciliarPlanesAsync(estado, ctx, ct);

        var planes = await _db.PlanesEstudio.AsNoTracking().Include(p => p.Periodos).ThenInclude(p => p.Materias).OrderBy(p => p.Id).ToListAsync(ct);
        var actual = planes.FirstOrDefault(p => p.Id == planId) ?? planes[0];
        var avisos = await _db.AvisosPlan.AsNoTracking().Where(a => !a.Leido).ToListAsync(ct);

        if (ctx is null)
            return new EstadoPlanificador { Estado = estado, Config = config, Planes = planes, Actual = actual, NoPuedePlanificar = motivo, Avisos = avisos.OrderByDescending(a => a.Id).ToList() };

        var prioridades = CalculadoraPrioridades.Calcular(ctx);
        // Con el plan sugerido se estima cuándo se cumple cada porcentaje.
        var sugerido = GeneradorPlan.Generar(ctx, prioridades);
        CalculadoraPrioridades.CompletarPorcentajes(prioridades, ctx, EvaluadorPlan.Evaluar(ctx, sugerido.Periodos));

        var plan = PeriodosDe(actual, estado.Reglas.Periodos);
        var evaluacion = EvaluadorPlan.Evaluar(ctx, plan);
        var enPlan = plan.SelectMany(p => p.Codigos).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var razones = actual.Periodos.SelectMany(p => p.Materias).Where(m => m.Razon is not null)
            .GroupBy(m => m.Codigo, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Razon!, StringComparer.OrdinalIgnoreCase);

        return new EstadoPlanificador
        {
            Estado = estado, Config = config, Planes = planes, Actual = actual,
            Contexto = ctx, Prioridades = prioridades,
            Plan = plan, Evaluacion = evaluacion, Razones = razones,
            Columnas = CalcularColumnas(ctx, plan),
            Colocaciones = CalcularColocaciones(ctx, plan, CalcularColumnas(ctx, plan)),
            Avisos = avisos.OrderByDescending(a => a.Id).ToList(),
            Simulador = await ConstruirSimuladorAsync(estado, ctx, plan, ct),
            Alertas = AlertasPlan.Calcular(ctx, plan, evaluacion, prioridades),
            Previsiones = ctx.Pendientes.Count == 0 ? new() : Previsionar(ctx, prioridades),
            PeriodosOmitibles = Enumerable.Range(0, 6).Select(k => ctx.Primero.Avanzar(k)).ToList(),
            PorPlanificar = prioridades.Materias.Where(m => !enPlan.Contains(m.Materia.Codigo)).ToList(),
            NoPuedePlanificar = ctx.Pendientes.Count == 0 ? "No te falta ninguna materia por planificar." : null,
        };
    }

    /// <summary>
    /// Cuánto falta para graduarte según el plan sugerido con la configuración actual. No crea ni modifica planes,
    /// así que se puede llamar desde el inicio. Devuelve null si todavía no hay histórico o pénsum.
    /// </summary>
    public async Task<EstimacionGraduacion?> EstimarAsync(EstadoAcademico estado, CancellationToken ct = default)
    {
        var ctx = CrearContexto(estado, await ObtenerConfigAsync(ct), out _);
        if (ctx is null) return null;
        if (ctx.Pendientes.Count == 0) return new EstimacionGraduacion(0, null);

        // Si la persona eligió un «Plan activo» con materias, la fecha es la de ese plan; si no, la del plan sugerido.
        var activo = await _db.PlanesEstudio.AsNoTracking().Include(p => p.Periodos).ThenInclude(p => p.Materias).FirstOrDefaultAsync(p => p.Activo, ct);
        if (activo is not null)
        {
            var delActivo = PeriodosDe(activo, estado.Reglas.Periodos);
            if (delActivo.Any(p => p.Codigos.Count > 0))
            {
                var evActivo = EvaluadorPlan.Evaluar(ctx, delActivo);
                return new EstimacionGraduacion(evActivo.CuatrimestresPlanificados, evActivo.Graduacion, activo.Nombre);
            }
        }

        var sugerido = GeneradorPlan.Generar(ctx, CalculadoraPrioridades.Calcular(ctx));
        var ev = EvaluadorPlan.Evaluar(ctx, sugerido.Periodos);
        return new EstimacionGraduacion(ev.CuatrimestresPlanificados, ev.Graduacion);
    }

    // ── Ajustar los planes a lo que pasó ──────────────────────────────────────────────────

    /// <summary>
    /// Ajusta todos los escenarios a la situación de ahora: las materias que ya no faltan (aprobadas o en curso) salen del plan, y si algo
    /// cambió respecto a la última vez que se miró (materias que vuelven a faltar, otro primer período, la graduación) se deja un aviso que
    /// dice qué pasó. Es idempotente: sin cambios no toca nada. Devuelve los avisos nuevos.
    /// </summary>
    private async Task<List<AvisoPlan>> ReconciliarPlanesAsync(EstadoAcademico estado, ContextoPlan ctx, CancellationToken ct)
    {
        var nuevos = new List<AvisoPlan>();
        var planes = await _db.PlanesEstudio.Include(p => p.Periodos).ThenInclude(p => p.Materias).OrderBy(p => p.Id).ToListAsync(ct);
        if (planes.Count == 0) return nuevos;

        var pensum = (await _db.PensumActivo.AsNoTracking().FirstOrDefaultAsync(ct))?.Clave ?? "csv";
        var pendientes = ctx.Pendientes.Keys.OrderBy(c => c, StringComparer.OrdinalIgnoreCase).ToList();
        var huboCambios = false;

        foreach (var plan in planes)
        {
            // Lo que ya no falta sale del plan (y no se guardan períodos vacíos).
            var salieron = new List<string>();
            foreach (var periodo in plan.Periodos.ToList())
            {
                foreach (var m in periodo.Materias.Where(m => ctx.PorCodigo.ContainsKey(m.Codigo) && !ctx.Pendientes.ContainsKey(m.Codigo)).ToList())
                {
                    salieron.Add(m.Codigo.ToUpperInvariant());
                    periodo.Materias.Remove(m);
                }
                if (periodo.Materias.Count == 0) plan.Periodos.Remove(periodo);
            }
            if (salieron.Count > 0) plan.Actualizado = DateTime.UtcNow;

            var evaluacion = EvaluadorPlan.Evaluar(ctx, PeriodosDe(plan, estado.Reglas.Periodos));
            var ahora = new InstantaneaPlan(pensum, ctx.Config.AsumirEnCurso, pendientes, ctx.Primero.Nombre, evaluacion.Graduacion?.Nombre);
            var cambios = ReconciliadorPlan.Comparar(InstantaneaPlan.DeTexto(plan.Instantanea), ahora, salieron);

            if (cambios.Hay)
            {
                var aviso = new AvisoPlan { Fecha = DateTime.UtcNow, PlanNombre = plan.Nombre, Texto = string.Join(" ", cambios.Frases()) };
                if (aviso.Texto.Length > 1000) aviso.Texto = aviso.Texto[..997] + "…";
                _db.AvisosPlan.Add(aviso);
                nuevos.Add(aviso);
            }

            var texto = ahora.ATexto();
            if (texto != plan.Instantanea) { plan.Instantanea = texto; huboCambios = true; }
            if (salieron.Count > 0) huboCambios = true;
        }

        if (huboCambios || nuevos.Count > 0) await _db.SaveChangesAsync(ct);
        return nuevos;
    }

    /// <summary>
    /// Se llama después de sincronizar con Banner (o de cambiar las materias a mano): ajusta los planes y devuelve, en pocas palabras, qué
    /// cambió (null si no cambió nada o todavía no se puede planificar).
    /// </summary>
    public async Task<string?> ReconciliarTrasCambioAsync(CancellationToken ct = default)
    {
        var estado = await _academico.ObtenerAsync(ct);
        var ctx = CrearContexto(estado, await ObtenerConfigAsync(ct), out _);
        if (ctx is null) return null;
        var avisos = await ReconciliarPlanesAsync(estado, ctx, ct);
        if (avisos.Count == 0) return null;

        var texto = string.Join(" ", avisos.Select(a => avisos.Count > 1 ? $"«{a.PlanNombre}»: {a.Texto}" : a.Texto));
        return texto.Length > 600 ? texto[..597] + "…" : texto;
    }

    /// <summary>Marca todos los avisos como leídos («Entendido»).</summary>
    public async Task DescartarAvisosAsync(CancellationToken ct = default) =>
        await _db.AvisosPlan.Where(a => !a.Leido).ExecuteUpdateAsync(s => s.SetProperty(a => a.Leido, true), ct);

    // ── Simulador de índice ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Las materias con nota por definir son las que están en curso y las del plan; las demás que faltan cuentan como «el resto» (sin fila).
    /// Con lo que la persona espera sacar se calcula el índice proyectado y el promedio que necesita para llegar al objetivo.
    /// </summary>
    private async Task<DatosSimulador> ConstruirSimuladorAsync(EstadoAcademico estado, ContextoPlan ctx, List<PlanPeriodo> plan, CancellationToken ct)
    {
        var notas = (await _db.NotasEsperadas.AsNoTracking().ToListAsync(ct)).ToDictionary(n => n.Codigo, n => n.Letra, StringComparer.OrdinalIgnoreCase);
        var filas = new List<FilaSimuladorVista>();
        var codigosConFila = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // En curso (no están en el plan): su nota todavía no existe.
        var enPlan = plan.SelectMany(p => p.Codigos).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var m in ctx.Base.Materias.Where(m => m.Estado == EstadoMateria.EnCurso && !enPlan.Contains(m.Materia.Codigo)).OrderBy(m => m.Materia.Codigo, StringComparer.OrdinalIgnoreCase))
        {
            var periodo = estado.CursosEnProgreso.FirstOrDefault(c => string.Equals(c.Codigo, m.Materia.Codigo, StringComparison.OrdinalIgnoreCase))?.Periodo;
            filas.Add(new FilaSimuladorVista(m.Materia.Codigo, m.Materia.Nombre, m.Materia.Creditos, periodo is null ? "En curso" : $"En curso · {periodo}", notas.GetValueOrDefault(m.Materia.Codigo)));
            codigosConFila.Add(m.Materia.Codigo);
        }

        // Las del plan, período por período.
        foreach (var p in plan.Where(p => p.Codigos.Count > 0).OrderBy(p => p.Periodo))
            foreach (var codigo in p.Codigos.Where(c => ctx.Pendientes.ContainsKey(c)))
            {
                var m = ctx.PorCodigo[codigo].Materia;
                if (!codigosConFila.Add(m.Codigo)) continue;
                filas.Add(new FilaSimuladorVista(m.Codigo, m.Nombre, m.Creditos, p.Periodo.Nombre, notas.GetValueOrDefault(m.Codigo)));
            }

        var resto = ctx.Pendientes.Values.Where(m => !codigosConFila.Contains(m.Materia.Codigo) && m.Materia.Creditos > 0).ToList();
        var escala = estado.Reglas.Escala;
        var objetivo = Math.Min(3.50m, escala.PuntosMaximos);
        var creditosSinFila = resto.Sum(m => (decimal)m.Materia.Creditos);
        var resultado = SimuladorIndice.Calcular(estado.Indice.Global, filas.Select(f => new FilaSimulacion(f.Codigo, f.Creditos, f.Letra)), creditosSinFila, objetivo, escala);

        return new DatosSimulador
        {
            Filas = filas, Actual = estado.Indice.Global, CreditosSinFila = creditosSinFila, MateriasSinFila = resto.Count,
            Objetivo = objetivo, Resultado = resultado, Escala = escala,
        };
    }

    /// <summary>Guarda (o quita, si la letra viene vacía) la nota que se espera sacar en una materia.</summary>
    public async Task<ResultadoOp> GuardarNotaEsperadaAsync(string? codigo, string? letra, CancellationToken ct = default)
    {
        var cod = codigo?.Trim().ToUpperInvariant() ?? "";
        if (cod.Length == 0 || !await _db.MateriasPensum.AnyAsync(m => m.Codigo == cod, ct)) return new(false, $"La materia «{codigo}» no existe en el pénsum.");

        var existente = await _db.NotasEsperadas.FirstOrDefaultAsync(n => n.Codigo == cod, ct);
        if (string.IsNullOrWhiteSpace(letra))
        {
            if (existente is not null) _db.NotasEsperadas.Remove(existente);
            await _db.SaveChangesAsync(ct);
            return new(true, $"{cod}: sin nota esperada.");
        }

        var escala = _academico.Reglas.Escala;
        var encontrada = escala.Buscar(letra);
        if (encontrada is null) return new(false, $"«{letra.Trim()}» no es una calificación de esta universidad ({string.Join(", ", escala.Letras.Select(l => l.Letra))}).");

        if (existente is null) _db.NotasEsperadas.Add(new NotaEsperada { Codigo = cod, Letra = encontrada.Letra });
        else existente.Letra = encontrada.Letra;
        await _db.SaveChangesAsync(ct);
        return new(true, $"{cod}: se espera {encontrada.Letra}.");
    }

    // ── Plan activo ───────────────────────────────────────────────────────────────────────

    /// <summary>Marca un escenario como «Plan activo» (y desmarca el anterior). Si ya lo era, lo desmarca: puede no haber ninguno.</summary>
    public async Task<ResultadoOp> AlternarActivoAsync(int id, CancellationToken ct = default)
    {
        var planes = await _db.PlanesEstudio.ToListAsync(ct);
        var plan = planes.FirstOrDefault(p => p.Id == id);
        if (plan is null) return new(false, "El plan no existe.");

        var eraActivo = plan.Activo;
        foreach (var p in planes) p.Activo = false;
        plan.Activo = !eraActivo;
        await _db.SaveChangesAsync(ct);
        return new(true, plan.Activo ? $"«{plan.Nombre}» es ahora tu plan activo: la fecha de graduación del inicio sale de él." : $"«{plan.Nombre}» ya no es el plan activo.", id);
    }

    /// <summary>Evalúa todos los escenarios con las mismas reglas, para compararlos.</summary>
    public async Task<(EstadoPlanificador Base, List<(PlanEstudio Plan, EvaluacionPlan Evaluacion)> Escenarios)> CompararAsync(CancellationToken ct = default)
    {
        var st = await ObtenerAsync(null, ct);
        var lista = new List<(PlanEstudio, EvaluacionPlan)>();
        if (st.Contexto is not null)
            foreach (var p in st.Planes)
                lista.Add((p, EvaluadorPlan.Evaluar(st.Contexto, PeriodosDe(p, st.Estado.Reglas.Periodos))));
        return (st, lista);
    }

    private static ContextoPlan? CrearContexto(EstadoAcademico e, ConfigPlanificador config, out string? motivo)
    {
        motivo = null;
        if (!e.HayHistorico) { motivo = "Primero sincroniza tu histórico con Banner (botón «Actualizar desde Banner») o registra tus materias en «Materias a mano»."; return null; }
        if (!e.HayPensum) { motivo = "Primero carga el pénsum desde el CSV."; return null; }

        var secuencia = e.Reglas.Periodos;
        var enProgreso = e.CursosEnProgreso.Select(c => c.Periodo).Where(p => PeriodoAcademico.TryParse(p, secuencia, out _))
            .Select(p => PeriodoAcademico.Parse(p, secuencia)).DefaultIfEmpty(new PeriodoAcademico(0, 0, secuencia)).Max();
        var hayEnProgreso = e.CursosEnProgreso.Any(c => PeriodoAcademico.TryParse(c.Periodo, secuencia, out _));
        var ultimoCerrado = e.Periodos.OrderBy(p => p.Orden).LastOrDefault()?.Nombre;

        var primero = PeriodoAcademico.Primero(hayEnProgreso ? enProgreso.Nombre : null, ultimoCerrado, config.AsumirEnCurso, secuencia);
        if (primero is null) { motivo = "No pude determinar el primer período planificable."; return null; }

        return ContextoPlan.Crear(e.Pensum, config, e.Indice.Global.Indice, primero.Value);
    }

    /// <summary>Cómo quedaría el plan sugerido con cada carga (ligera, normal y pesada), sin guardar nada.</summary>
    private static List<PrevisionCarga> Previsionar(ContextoPlan ctx, ResultadoPrioridades prioridades)
    {
        var mayor = ctx.Pendientes.Values.Select(m => m.Materia.Creditos).DefaultIfEmpty(0).Max();
        return Enum.GetValues<CargaDeseada>().Select(carga =>
        {
            var generado = GeneradorPlan.Generar(ctx, prioridades, new OpcionesGeneracion(carga));
            var ev = EvaluadorPlan.Evaluar(ctx, generado.Periodos);
            return new PrevisionCarga(carga, OpcionesGeneracion.LimiteDeCarga(carga, ctx.Limite, mayor), ev.CuatrimestresPlanificados, ev.Graduacion);
        }).ToList();
    }

    /// <summary>Para cada materia por planificar o ya planificada y cada columna: si no puede ir (con el motivo) o si iría con un aviso.</summary>
    public static Dictionary<string, Dictionary<string, ColocacionInfo>> CalcularColocaciones(ContextoPlan ctx, IReadOnlyList<PlanPeriodo> plan, IReadOnlyList<PeriodoAcademico> columnas)
    {
        var resultado = new Dictionary<string, Dictionary<string, ColocacionInfo>>(StringComparer.OrdinalIgnoreCase);
        var codigos = ctx.Pendientes.Keys.Concat(plan.SelectMany(p => p.Codigos)).Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var codigo in codigos)
        {
            var porPeriodo = new Dictionary<string, ColocacionInfo>();
            foreach (var col in columnas)
            {
                var info = ValidadorColocacion.Evaluar(ctx, plan, codigo, col);
                if (!info.Permitida || info.Aviso is not null) porPeriodo[col.Nombre] = info;
            }
            if (porPeriodo.Count > 0) resultado[codigo.ToUpperInvariant()] = porPeriodo;
        }
        return resultado;
    }

    private static List<PeriodoAcademico> CalcularColumnas(ContextoPlan ctx, List<PlanPeriodo> plan)
    {
        var usados = plan.Where(p => p.Codigos.Count > 0).Select(p => p.Periodo).ToList();
        var desde = usados.Count > 0 ? new[] { ctx.Primero, usados.Min() }.Min() : ctx.Primero;
        var hasta = usados.Count > 0 ? new[] { usados.Max().Siguiente(), ctx.Primero.Avanzar(2) }.Max() : ctx.Primero.Avanzar(2);
        var columnas = new List<PeriodoAcademico>();
        for (var p = desde; p <= hasta; p = p.Siguiente()) columnas.Add(p);
        return columnas;
    }

    public static List<PlanPeriodo> PeriodosDe(PlanEstudio plan, SecuenciaPeriodos? secuencia = null)
    {
        secuencia ??= SecuenciaPeriodos.Unapec;
        return plan.Periodos.Where(p => PeriodoAcademico.TryParse(p.Nombre, secuencia, out _))
            .Select(p => new PlanPeriodo { Periodo = PeriodoAcademico.Parse(p.Nombre, secuencia), Codigos = p.Materias.Select(m => m.Codigo).ToList() })
            .OrderBy(p => p.Periodo).ToList();
    }

    // ── Escenarios ────────────────────────────────────────────────────────────────────────

    public async Task<ResultadoOp> CrearPlanAsync(string? nombre, int? copiarDe = null, CancellationToken ct = default)
    {
        var error = await ValidarNombreAsync(nombre, null, ct);
        if (error is not null) return new(false, error);

        var plan = new PlanEstudio { Nombre = nombre!.Trim(), Creado = DateTime.UtcNow, Actualizado = DateTime.UtcNow };
        if (copiarDe is not null)
        {
            var origen = await _db.PlanesEstudio.AsNoTracking().Include(p => p.Periodos).ThenInclude(p => p.Materias)
                .FirstOrDefaultAsync(p => p.Id == copiarDe, ct);
            if (origen is null) return new(false, "El plan que quieres copiar no existe.");
            plan.Periodos = origen.Periodos.Select(p => new PeriodoPlanificado
            {
                Nombre = p.Nombre,
                Materias = p.Materias.Select(m => new MateriaPlanificada { Codigo = m.Codigo, Razon = m.Razon }).ToList(),
            }).ToList();
        }
        _db.PlanesEstudio.Add(plan);
        await _db.SaveChangesAsync(ct);
        return new(true, copiarDe is null ? $"Plan «{plan.Nombre}» creado." : $"Plan «{plan.Nombre}» guardado como escenario.", plan.Id);
    }

    public async Task<ResultadoOp> RenombrarAsync(int id, string? nombre, CancellationToken ct = default)
    {
        var plan = await _db.PlanesEstudio.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (plan is null) return new(false, "El plan no existe.");
        var error = await ValidarNombreAsync(nombre, id, ct);
        if (error is not null) return new(false, error);
        plan.Nombre = nombre!.Trim();
        plan.Actualizado = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return new(true, "Plan renombrado.", id);
    }

    public async Task<ResultadoOp> EliminarAsync(int id, CancellationToken ct = default)
    {
        if (await _db.PlanesEstudio.CountAsync(ct) <= 1) return new(false, "Debe quedar al menos un plan.");
        var plan = await _db.PlanesEstudio.Include(p => p.Periodos).ThenInclude(p => p.Materias).FirstOrDefaultAsync(p => p.Id == id, ct);
        if (plan is null) return new(false, "El plan ya no existe.");
        _db.PlanesEstudio.Remove(plan);
        await _db.SaveChangesAsync(ct);
        return new(true, $"Plan «{plan.Nombre}» eliminado.");
    }

    private async Task<string?> ValidarNombreAsync(string? nombre, int? idActual, CancellationToken ct)
    {
        var n = nombre?.Trim();
        if (string.IsNullOrEmpty(n)) return "Escribe un nombre para el plan.";
        if (n.Length > 60) return "El nombre puede tener como máximo 60 caracteres.";
        var existentes = await _db.PlanesEstudio.AsNoTracking().Select(p => new { p.Id, p.Nombre }).ToListAsync(ct);
        return existentes.Any(p => p.Id != idActual && string.Equals(p.Nombre, n, StringComparison.OrdinalIgnoreCase))
            ? $"Ya existe un plan llamado «{n}»." : null;
    }

    // ── Edición del plan ──────────────────────────────────────────────────────────────────

    /// <summary>Pone la materia en un período (o la quita del plan si <paramref name="periodo"/> es vacío). Mover = asignar de nuevo.</summary>
    public async Task<ResultadoOp> AsignarAsync(int planId, string? codigo, string? periodo, CancellationToken ct = default)
    {
        var cod = codigo?.Trim().ToUpperInvariant() ?? "";
        if (cod.Length == 0 || !await _db.MateriasPensum.AnyAsync(m => m.Codigo == cod, ct)) return new(false, $"La materia «{codigo}» no existe en el pénsum.");

        PeriodoAcademico? destino = null;
        if (!string.IsNullOrWhiteSpace(periodo))
        {
            if (!PeriodoAcademico.TryParse(periodo, _academico.Reglas.Periodos, out var p)) return new(false, $"Período no válido: «{periodo}».");
            destino = p;
        }

        var plan = await _db.PlanesEstudio.Include(p => p.Periodos).ThenInclude(p => p.Materias).FirstOrDefaultAsync(p => p.Id == planId, ct);
        if (plan is null) return new(false, "El plan no existe.");

        foreach (var per in plan.Periodos.ToList())
        {
            foreach (var m in per.Materias.Where(m => string.Equals(m.Codigo, cod, StringComparison.OrdinalIgnoreCase)).ToList())
                per.Materias.Remove(m);
            if (per.Materias.Count == 0) plan.Periodos.Remove(per);   // no se guardan períodos vacíos
        }

        if (destino is not null)
        {
            var fila = plan.Periodos.FirstOrDefault(p => p.Nombre == destino.Value.Nombre);
            if (fila is null) plan.Periodos.Add(fila = new PeriodoPlanificado { Nombre = destino.Value.Nombre });
            fila.Materias.Add(new MateriaPlanificada { Codigo = cod });   // movida a mano: sin razón del plan sugerido
        }

        plan.Actualizado = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return new(true, destino is null ? $"{cod} quitada del plan." : $"{cod} → {destino.Value.Nombre}.", planId);
    }

    public async Task<ResultadoOp> LimpiarAsync(int planId, CancellationToken ct = default)
    {
        var plan = await _db.PlanesEstudio.Include(p => p.Periodos).ThenInclude(p => p.Materias).FirstOrDefaultAsync(p => p.Id == planId, ct);
        if (plan is null) return new(false, "El plan no existe.");
        plan.Periodos.Clear();
        plan.Actualizado = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return new(true, "Plan vaciado.", planId);
    }

    /// <summary>Reemplaza el contenido del plan por el plan sugerido (con prerrequisitos, porcentajes y límite de créditos).</summary>
    public async Task<ResultadoOp> GenerarAsync(int planId, CancellationToken ct = default) => await GenerarAsync(planId, OpcionesGeneracion.Ninguna, ct);

    /// <summary>Igual, con la carga que eligió la persona y los períodos en los que no va a estudiar.</summary>
    public async Task<ResultadoOp> GenerarAsync(int planId, OpcionesGeneracion opciones, CancellationToken ct = default)
    {
        var st = await ObtenerAsync(planId, ct);
        if (st.Contexto is null || st.Prioridades is null) return new(false, st.NoPuedePlanificar ?? "No se puede planificar todavía.");

        var gen = GeneradorPlan.Generar(st.Contexto, st.Prioridades, opciones);
        var plan = await _db.PlanesEstudio.Include(p => p.Periodos).ThenInclude(p => p.Materias).FirstAsync(p => p.Id == planId, ct);
        plan.Periodos.Clear();
        foreach (var p in gen.Periodos.Where(p => p.Codigos.Count > 0))
            plan.Periodos.Add(new PeriodoPlanificado
            {
                Nombre = p.Periodo.Nombre,
                Materias = p.Codigos.Select(c => new MateriaPlanificada { Codigo = c, Razon = gen.Razones.GetValueOrDefault(c) }).ToList(),
            });
        plan.Actualizado = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        var ev = EvaluadorPlan.Evaluar(st.Contexto, gen.Periodos);
        var grad = ev.Graduacion is { } g ? $" Graduación estimada: {g.Nombre}." : "";
        var sin = gen.NoPlanificadas.Count > 0 ? $" No se pudieron ubicar: {string.Join(", ", gen.NoPlanificadas)}." : "";
        var carga = opciones.Carga switch { CargaDeseada.Ligera => "carga ligera", CargaDeseada.Normal => "carga normal", _ => "carga pesada" };
        var omitidos = opciones.Omitidos is { Count: > 0 } o ? $" Sin estudiar en {string.Join(", ", o.OrderBy(x => x).Select(x => x.Nombre))}." : "";
        return new(true, $"Plan sugerido generado ({carga}): {ev.CuatrimestresPlanificados} cuatrimestres.{grad}{omitidos}{sin}", planId);
    }
}
