using HistorialAcademico.Web.Data;
using HistorialAcademico.Web.Models;
using HistorialAcademico.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HistorialAcademico.Web.Controllers;

/// <summary>Historial de sincronizaciones y el botón "Actualizar desde Banner" (solo a demanda).</summary>
public class SincronizacionesController : Controller
{
    private readonly HistorialContext _db;
    private readonly SincronizacionService _sync;
    private readonly PlanificadorService _planificador;

    public SincronizacionesController(HistorialContext db, SincronizacionService sync, PlanificadorService planificador)
    {
        _db = db;
        _sync = sync;
        _planificador = planificador;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct) => View(new SincronizacionesViewModel
    {
        Historial = (await _db.Sincronizaciones.AsNoTracking().ToListAsync(ct)).OrderByDescending(s => s.Id).Take(100).ToList(),
        Mensaje = TempData["Mensaje"] as string,
        Error = TempData["Error"] as string,
    });

    /// <summary>
    /// Captura el histórico y lo guarda. Si la sesión de Banner caducó, abre Chromium para que inicies sesión y
    /// captura de inmediato. La petición dura mientras esperas el login (hasta 5 minutos).
    /// </summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Actualizar(string? volverA, CancellationToken ct)
    {
        var r = await _sync.ActualizarAsync(permitirLogin: true, ct);
        var mensaje = r.Mensaje;
        if (r.Exito) mensaje += await CambiosDelPlanAsync(ct);
        TempData[r.Exito ? "Mensaje" : "Error"] = mensaje;

        // Solo se vuelve a una ruta local (evita redirecciones abiertas).
        return !string.IsNullOrEmpty(volverA) && Url.IsLocalUrl(volverA) ? LocalRedirect(volverA) : RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// Tras sincronizar, los planes se ajustan a lo que pasó (lo aprobado sale del plan, lo reprobado vuelve a «Por planificar») y se cuenta qué cambió.
    /// Un fallo aquí nunca debe estropear una sincronización que ya salió bien: el planificador vuelve a ajustar el plan la próxima vez que se abra.
    /// </summary>
    private async Task<string> CambiosDelPlanAsync(CancellationToken ct)
    {
        try { return await _planificador.ReconciliarTrasCambioAsync(ct) is { } cambio ? " Tu plan cambió: " + cambio : ""; }
        catch (Exception ex) when (ex is not OperationCanceledException) { return ""; }
    }
}
