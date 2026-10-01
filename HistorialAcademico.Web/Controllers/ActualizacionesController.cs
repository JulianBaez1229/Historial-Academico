using HistorialAcademico.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace HistorialAcademico.Web.Controllers;

/// <summary>Los avisos de versiones nuevas: apagarlos o encenderlos, ignorar una versión y buscar a mano. Todo vuelve a «Mis datos».</summary>
public class ActualizacionesController : Controller
{
    private readonly ActualizacionesService _actualizaciones;

    public ActualizacionesController(ActualizacionesService actualizaciones) => _actualizaciones = actualizaciones;

    private IActionResult Volver(string? volverA, string? mensaje, bool error = false)
    {
        if (mensaje is not null) TempData[error ? "Error" : "Mensaje"] = mensaje;
        // Solo se vuelve a una ruta de esta misma aplicación.
        return volverA is { Length: > 0 } && Url.IsLocalUrl(volverA) ? LocalRedirect(volverA) : RedirectToAction("Index", "MisDatos");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Preferencia(bool avisar, CancellationToken ct)
    {
        if (!_actualizaciones.Avisar(avisar))
            return Volver(null, "No pude guardar tu preferencia: la carpeta de datos no se puede escribir.", error: true);
        if (avisar) await _actualizaciones.ConsultarAsync(ct: ct);   // al encenderlos se mira una vez (no hace falta reiniciar)
        return Volver(null, avisar
            ? "Listo: al abrir la aplicación se consultará si hay una versión nueva."
            : "Listo: la aplicación ya no consulta si hay versiones nuevas. No se conecta a ningún lado por su cuenta.");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult Ignorar(string? version, string? volverA)
    {
        if (string.IsNullOrWhiteSpace(version) || version.Length > 40) return Volver(volverA, null);
        _actualizaciones.Ignorar(version.Trim());   // sin mensaje: la franja simplemente desaparece
        return Volver(volverA, null);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> BuscarAhora(CancellationToken ct)
    {
        if (!_actualizaciones.Habilitada)
            return Volver(null, "Los avisos están desactivados o este programa no sabe de dónde salió: no consulté nada.", error: true);
        await _actualizaciones.ConsultarAsync(forzar: true, ct);
        return _actualizaciones.Estado switch
        {
            EstadoConsulta.Nueva => Volver(null, $"Hay una versión nueva: {_actualizaciones.Aviso?.Version ?? "más reciente"}. Mira el aviso de arriba."),
            EstadoConsulta.AlDia => Volver(null, $"Estás al día: la versión {_actualizaciones.Actual} es la última."),
            _ => Volver(null, _actualizaciones.UltimoError ?? "No pude consultar si hay una versión nueva.", error: true),
        };
    }
}
