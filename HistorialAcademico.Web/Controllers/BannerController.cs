using HistorialAcademico.Banner;
using HistorialAcademico.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace HistorialAcademico.Web.Controllers;

/// <summary>Endpoints de la Fase 1. Solo a demanda: no hay tareas en segundo plano ni reintentos.</summary>
[Route("banner")]
public class BannerController : Controller
{
    private readonly BannerClient _banner;

    public BannerController(BannerClient banner) => _banner = banner;

    [HttpGet("estado")]
    public IActionResult Estado() => Json(new { sesionGuardada = _banner.TieneSesionGuardada });

    /// <summary>Abre Chromium visible y espera a que inicies sesión manualmente (incluido MFA).</summary>
    [HttpPost("iniciar-sesion")]
    public async Task<IActionResult> IniciarSesion(CancellationToken ct)
    {
        try
        {
            await _banner.IniciarSesionAsync(ct);
            return Json(new { ok = true, mensaje = "Sesión de Banner guardada en .auth/banner.json." });
        }
        catch (BannerException ex)
        {
            return StatusCode(502, new { ok = false, mensaje = ex.Message });
        }
    }

    /// <summary>
    /// Captura el histórico con la sesión guardada y lo guarda en la base de datos (transaccional).
    /// Si la sesión expiró responde 401 con requiereLogin; si el parser falla o los totales no cuadran, la base queda intacta.
    /// </summary>
    [HttpPost("sincronizar")]
    public async Task<IActionResult> Sincronizar([FromServices] SincronizacionService sync, CancellationToken ct)
    {
        var r = await sync.SincronizarDesdeBannerAsync(ct);
        if (r.Exito) return Json(new { ok = true, mensaje = r.Mensaje });
        return StatusCode(r.RequiereLogin ? 401 : 422, new { ok = false, requiereLogin = r.RequiereLogin, mensaje = r.Mensaje });
    }

    /// <summary>
    /// Aplica el samples/historico.html ya capturado, sin conectarse a Banner. Pasa por la misma validación y
    /// transacción que la sincronización normal; sirve para trabajar sin sesión de Banner.
    /// </summary>
    [HttpPost("sincronizar-muestra")]
    public async Task<IActionResult> SincronizarMuestra([FromServices] SincronizacionService sync, [FromServices] BannerOptions opciones, CancellationToken ct)
    {
        var ruta = Path.Combine(opciones.CarpetaMuestras, "historico.html");
        if (!System.IO.File.Exists(ruta))
            return NotFound(new { ok = false, mensaje = "No existe samples/historico.html. Captura el histórico primero." });

        var r = await sync.AplicarHtmlAsync(await System.IO.File.ReadAllTextAsync(ruta, ct), ct);
        return r.Exito ? Json(new { ok = true, mensaje = r.Mensaje }) : StatusCode(422, new { ok = false, mensaje = r.Mensaje });
    }

    /// <summary>
    /// Herramienta de captura de la E01-A: explora "Consultar programación académica" y guarda en samples/horarios/ el HTML y
    /// las respuestas JSON reales para construir el parser de horarios. Si la sesión caducó abre el login y captura de inmediato.
    /// </summary>
    [HttpPost("explorar-horarios")]
    public async Task<IActionResult> ExplorarHorarios([FromQuery] string materia = "ISO", CancellationToken ct = default)
    {
        try
        {
            (string Carpeta, IReadOnlyList<string> Pasos) r;
            try { r = await _banner.ExplorarHorariosAsync(materia, ct); }
            catch (BannerSesionExpiradaException)
            {
                await _banner.IniciarSesionAsync(ct);   // la sesión dura pocos minutos: se captura enseguida
                r = await _banner.ExplorarHorariosAsync(materia, ct);
            }
            return Json(new { ok = true, carpeta = r.Carpeta, pasos = r.Pasos });
        }
        catch (BannerException ex)
        {
            return StatusCode(502, new { ok = false, mensaje = ex.Message });
        }
    }

    /// <summary>Con la sesión guardada, entra al Histórico Académico y guarda el HTML en samples/.</summary>
    [HttpPost("capturar-historico")]
    public async Task<IActionResult> CapturarHistorico([FromQuery] bool visible = false, CancellationToken ct = default)
    {
        try
        {
            var ruta = await _banner.CapturarHistoricoAsync(visible, ct);
            return Json(new { ok = true, mensaje = "Histórico guardado.", ruta });
        }
        catch (BannerSesionExpiradaException ex)
        {
            return StatusCode(401, new { ok = false, requiereLogin = true, mensaje = ex.Message });
        }
        catch (BannerException ex)
        {
            return StatusCode(502, new { ok = false, mensaje = ex.Message });
        }
    }
}
