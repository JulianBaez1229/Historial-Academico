using HistorialAcademico.Core.Planificacion;
using HistorialAcademico.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace HistorialAcademico.Web.Controllers;

/// <summary>Planificador de cuatrimestres: escenarios editables, prioridades y plan sugerido.</summary>
public class PlanificadorController : Controller
{
    private readonly PlanificadorService _plan;
    private readonly ReglasUniversidadService _reglas;
    private readonly VistaATexto _vista;
    private readonly ExportadorPlanService _exportador;

    public PlanificadorController(PlanificadorService plan, ReglasUniversidadService reglas, VistaATexto vista, ExportadorPlanService exportador)
    {
        _plan = plan;
        _reglas = reglas;
        _vista = vista;
        _exportador = exportador;
    }

    [HttpGet]
    public async Task<IActionResult> Index(int? id, CancellationToken ct) => View(await _plan.ObtenerAsync(id, ct));

    /// <summary>La hoja del plan lista para imprimir (una página, sin menú). También es lo que se convierte en PDF o imagen.</summary>
    [HttpGet]
    public async Task<IActionResult> Imprimible(int? id, CancellationToken ct)
    {
        var st = await _plan.ObtenerAsync(id, ct);
        if (st.Contexto is null || st.Evaluacion is null)
        {
            TempData["Error"] = st.NoPuedePlanificar ?? "Todavía no se puede armar un plan.";
            return RedirectToAction(nameof(Index), new { id });
        }
        return View(st);
    }

    /// <summary>Descarga el plan como PDF (una página, apaisada) o como imagen PNG. Se dibuja con el Chromium de Playwright, sin salir a internet.</summary>
    [HttpGet]
    public async Task<IActionResult> Exportar(int? id, string? formato, CancellationToken ct)
    {
        var pdf = !string.Equals(formato, "png", StringComparison.OrdinalIgnoreCase);
        var st = await _plan.ObtenerAsync(id, ct);
        if (st.Contexto is null || st.Evaluacion is null)
        {
            TempData["Error"] = st.NoPuedePlanificar ?? "Todavía no se puede armar un plan.";
            return RedirectToAction(nameof(Index), new { id });
        }

        try
        {
            var html = await _vista.RenderizarAsync(ControllerContext, "Imprimible", st);
            var bytes = pdf ? await _exportador.PdfAsync(html) : await _exportador.PngAsync(html);
            return File(bytes, pdf ? "application/pdf" : "image/png", ExportadorPlanService.NombreDeArchivo(st.Actual!.Nombre, DateTime.Now, pdf ? "pdf" : "png"));
        }
        catch (ExportacionNoDisponibleException ex)
        {
            TempData["Error"] = ex.Message;
            return RedirectToAction(nameof(Index), new { id = st.Actual!.Id });
        }
    }

    /// <summary>"Qué priorizar": todas las materias faltantes ordenadas por prioridad, con el motivo.</summary>
    [HttpGet]
    public async Task<IActionResult> Prioridades(CancellationToken ct) => View(await _plan.ObtenerAsync(null, ct));

    /// <summary>Escenarios lado a lado: cantidad de cuatrimestres y fecha de graduación.</summary>
    [HttpGet]
    public async Task<IActionResult> Comparar(CancellationToken ct)
    {
        var (baseEstado, escenarios) = await _plan.CompararAsync(ct);
        return View(new CompararViewModel(baseEstado, escenarios));
    }

    // ── Acciones (todas POST con token antifalsificación) ─────────────────────────────────

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Crear(string? nombre, CancellationToken ct)
    {
        var r = await _plan.CrearPlanAsync(nombre, null, ct);
        return Resultado(r, r.Id);
    }

    /// <summary>Guarda una copia del plan actual como un escenario con nombre.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> GuardarComo(int id, string? nombre, CancellationToken ct)
    {
        var r = await _plan.CrearPlanAsync(nombre, copiarDe: id, ct);
        return Resultado(r, r.Ok ? r.Id : id);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Renombrar(int id, string? nombre, CancellationToken ct) =>
        Resultado(await _plan.RenombrarAsync(id, nombre, ct), id);

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Eliminar(int id, CancellationToken ct) =>
        Resultado(await _plan.EliminarAsync(id, ct), null);

    /// <summary>Pone una materia en un período (o la quita si <c>periodo</c> viene vacío). También la usa el arrastrar y soltar.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Asignar(int id, string? codigo, string? periodo, bool avisar, CancellationToken ct)
    {
        var r = await _plan.AsignarAsync(id, codigo, periodo, ct);
        if (Request.Headers.XRequestedWith == "XMLHttpRequest")
            return r.Ok ? Json(new { ok = true, mensaje = r.Mensaje }) : BadRequest(new { ok = false, mensaje = r.Mensaje });
        return Resultado(r, id, silencioso: r.Ok && !avisar);   // al resolver una alerta sí se dice qué se hizo
    }

    /// <summary>
    /// Genera el plan sugerido. Antes se le pregunta a la persona la carga que quiere (<c>carga</c>: ligera, normal o pesada; sin decir nada es
    /// pesada, hasta el límite de créditos) y los períodos en los que no va a estudiar (<c>omitir</c>, uno por cada período marcado).
    /// </summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Generar(int id, string? carga, string[]? omitir, CancellationToken ct)
    {
        var elegida = Enum.TryParse<CargaDeseada>(carga, ignoreCase: true, out var c) ? c : CargaDeseada.Pesada;
        var secuencia = _reglas.Activa.Periodos;
        var omitidos = new HashSet<PeriodoAcademico>();
        foreach (var texto in omitir ?? Array.Empty<string>())
            if (PeriodoAcademico.TryParse(texto, secuencia, out var p)) omitidos.Add(p);
        return Resultado(await _plan.GenerarAsync(id, new OpcionesGeneracion(elegida, omitidos), ct), id);
    }

    /// <summary>Marca (o desmarca) un escenario como «Plan activo». <c>desde=comparar</c> vuelve a la comparación.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Activar(int id, string? desde, CancellationToken ct)
    {
        var r = await _plan.AlternarActivoAsync(id, ct);
        TempData[r.Ok ? "Mensaje" : "Error"] = r.Mensaje;
        return desde == "comparar" ? RedirectToAction(nameof(Comparar)) : RedirectToAction(nameof(Index), new { id });
    }

    /// <summary>«Entendido»: los avisos de cambios del plan (tras sincronizar) dejan de mostrarse.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DescartarAvisos(int? id, CancellationToken ct)
    {
        await _plan.DescartarAvisosAsync(ct);
        return RedirectToAction(nameof(Index), new { id });
    }

    /// <summary>La nota que se espera sacar en una materia, para el simulador de índice (la guarda al cambiar; vacía la quita).</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> NotaEsperada(string? codigo, string? nota, CancellationToken ct)
    {
        var r = await _plan.GuardarNotaEsperadaAsync(codigo, nota, ct);
        return r.Ok ? Json(new { ok = true, mensaje = r.Mensaje }) : BadRequest(new { ok = false, mensaje = r.Mensaje });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Limpiar(int id, CancellationToken ct) => Resultado(await _plan.LimpiarAsync(id, ct), id);

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Configurar(int id, int limiteBase, int limiteAlto, decimal umbralIndice, int minimo, bool asumirEnCurso, CancellationToken ct)
    {
        var r = await _plan.GuardarConfigAsync(new ConfigPlanificador(limiteBase, limiteAlto, umbralIndice, minimo, asumirEnCurso), ct);
        return Resultado(r, id);
    }

    private IActionResult Resultado(ResultadoOp r, int? planId, bool silencioso = false)
    {
        if (!silencioso) TempData[r.Ok ? "Mensaje" : "Error"] = r.Mensaje;
        return RedirectToAction(nameof(Index), new { id = planId });
    }
}

public record CompararViewModel(EstadoPlanificador Estado, List<(HistorialAcademico.Core.Entities.PlanEstudio Plan, EvaluacionPlan Evaluacion)> Escenarios);
