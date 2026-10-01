using HistorialAcademico.Core.Perfiles;
using HistorialAcademico.Web.Data;
using HistorialAcademico.Web.Models;
using HistorialAcademico.Web.Perfiles;
using HistorialAcademico.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HistorialAcademico.Web.Controllers;

/// <summary>
/// Tus datos son tuyos: ver qué hay guardado y dónde, llevarte una copia en JSON y borrarlo todo (base de datos, sesión de Banner y perfil)
/// en cualquier momento, con una confirmación explícita.
/// </summary>
public class MisDatosController : Controller
{
    private readonly GestorPerfiles _gestor;
    private readonly PerfilActual _perfil;
    private readonly ExportadorDatosService _exportador;
    private readonly ConsultaMasivaService _masiva;
    private readonly HistorialContext _db;
    private readonly ActualizacionesService _actualizaciones;

    public MisDatosController(GestorPerfiles gestor, PerfilActual perfil, ExportadorDatosService exportador, ConsultaMasivaService masiva, HistorialContext db,
        ActualizacionesService actualizaciones)
    {
        _actualizaciones = actualizaciones;
        _gestor = gestor;
        _perfil = perfil;
        _exportador = exportador;
        _masiva = masiva;
        _db = db;
    }

    private string? Carpeta => _perfil.CarpetaDatos;
    private string? CarpetaSesion => Carpeta is null ? null : Path.Combine(Carpeta, ".auth");
    private string? CarpetaCapturas => Carpeta is null ? null : Path.Combine(Carpeta, "samples");

    private async Task<MisDatosViewModel> ArmarAsync(string? mensaje = null, string? error = null, CancellationToken ct = default)
    {
        var baseAnterior = _gestor.BaseAnterior;
        return new MisDatosViewModel
        {
            Actualizaciones = new InfoActualizaciones(_actualizaciones.Actual, _actualizaciones.Repositorio, _actualizaciones.Disponible, _actualizaciones.Habilitada),
            NombrePerfil = _perfil.Perfil?.Nombre,
            TienePin = _perfil.Perfil?.TienePin ?? false,
            Carpeta = Carpeta ?? "",
            BytesBase = _perfil.RutaBase is { } ruta && System.IO.File.Exists(ruta) ? new FileInfo(ruta).Length : 0,
            HaySesionBanner = CarpetaSesion is not null && Directory.Exists(CarpetaSesion) && Directory.EnumerateFileSystemEntries(CarpetaSesion).Any(),
            HayCapturasBanner = CarpetaCapturas is not null && Directory.Exists(CarpetaCapturas) && Directory.EnumerateFileSystemEntries(CarpetaCapturas).Any(),
            Conteo = await ContarAsync(ct),
            BaseAnterior = System.IO.File.Exists(baseAnterior) && !string.Equals(baseAnterior, _perfil.RutaBase, StringComparison.OrdinalIgnoreCase) ? baseAnterior : null,
            Mensaje = mensaje ?? TempData["Mensaje"] as string,
            Error = error ?? TempData["Error"] as string,
        };
    }

    /// <summary>Cuántos datos hay de cada cosa. Si la base no se puede abrir (justo lo que pasa cuando algo la tiene tomada) se muestra vacío, no una página rota.</summary>
    private async Task<ConteoDatos> ContarAsync(CancellationToken ct)
    {
        try
        {
            return new ConteoDatos(
                await _db.Periodos.CountAsync(ct), await _db.MateriasCursadas.CountAsync(ct), await _db.CursosEnProgreso.CountAsync(ct),
                await _db.MateriasManuales.CountAsync(ct), await _db.MateriasPensum.CountAsync(ct), await _db.PlanesEstudio.CountAsync(ct),
                await _db.HorariosTentativos.CountAsync(ct), await _db.Equivalencias.CountAsync(ct));
        }
        catch (SqliteException) { return new ConteoDatos(0, 0, 0, 0, 0, 0, 0, 0); }
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct) => View(await ArmarAsync(ct: ct));

    /// <summary>Descarga todo lo tuyo en un archivo JSON. Es una copia sin cifrar: quien tenga el archivo puede leer tus calificaciones.</summary>
    [HttpGet]
    public async Task<IActionResult> Exportar(CancellationToken ct)
    {
        var ahora = DateTime.UtcNow;
        var bytes = await _exportador.ExportarAsync(ahora, ct);
        return File(bytes, "application/json; charset=utf-8", _exportador.NombreDeArchivo(ahora.ToLocalTime()));
    }

    /// <summary>Borra la sesión de Banner guardada (y las capturas de sus páginas), sin tocar tus datos: la próxima vez tendrás que iniciar sesión otra vez.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult BorrarSesion()
    {
        if (CarpetaSesion is null || CarpetaCapturas is null) { TempData["Error"] = "No hay un perfil abierto."; return RedirectToAction(nameof(Index)); }
        try
        {
            foreach (var carpeta in new[] { CarpetaSesion, CarpetaCapturas })
                if (Directory.Exists(carpeta)) Directory.Delete(carpeta, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TempData["Error"] = $"No pude borrar la sesión de Banner ({ex.Message}). Cierra la ventana de Chromium si sigue abierta e inténtalo otra vez.";
            return RedirectToAction(nameof(Index));
        }
        TempData["Mensaje"] = "Borré la sesión de Banner y las capturas de sus páginas. La próxima vez que conectes Banner tendrás que iniciar sesión otra vez.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// Borra el perfil completo: su base de datos, la sesión de Banner y el perfil mismo. No se puede deshacer, por eso hay que escribir el
    /// nombre del perfil (y el PIN, si tiene). Solo se borra la carpeta de este perfil; nada fuera de ella.
    /// </summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> BorrarTodo(string? confirmacion, string? pin, CancellationToken ct)
    {
        if (_perfil.Perfil is not { } perfil)
        {
            TempData["Error"] = "No hay un perfil abierto que borrar.";
            return RedirectToAction(nameof(Index));
        }
        if (!string.Equals(confirmacion?.Trim(), perfil.Nombre, StringComparison.CurrentCultureIgnoreCase))
            return View(nameof(Index), await ArmarAsync(error: $"No borré nada: para confirmar, escribe el nombre de tu perfil («{perfil.Nombre}»).", ct: ct));

        if (perfil.TienePin)
        {
            var intento = _gestor.Almacen.Intentar(perfil.Id, pin);
            if (intento.Resultado != ResultadoEntrada.Correcto)
            {
                var motivo = intento.Resultado == ResultadoEntrada.Bloqueado
                    ? $"Demasiados intentos con el PIN: espera {PerfilesController.TextoEspera(intento.Espera)}."
                    : "El PIN no es correcto.";
                return View(nameof(Index), await ArmarAsync(error: $"No borré nada. {motivo}", ct: ct));
            }
        }

        // Una consulta a Banner en segundo plano tiene la base abierta: se detiene antes de borrar.
        if (!await _masiva.DetenerYOlvidarAsync(_perfil, TimeSpan.FromSeconds(15)))
            return View(nameof(Index), await ArmarAsync(error: "No borré nada: hay una consulta a Banner que todavía no termina. Espera un momento e inténtalo otra vez.", ct: ct));

        var rutaBase = _perfil.RutaBase;
        SqliteConnection.ClearAllPools();   // suelta los archivos de la base para poder borrarlos
        try { _gestor.Almacen.Borrar(perfil.Id); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Puede que ya se hayan borrado algunos archivos (la base, por ejemplo): la próxima vez que se abra el perfil se vuelve a crear vacía.
            // Se sale a la pantalla de perfiles, que no depende de la base, en vez de dibujar una página que la lea.
            if (rutaBase is not null) _gestor.OlvidarBase(rutaBase);
            TempData["Error"] = $"No pude borrarlo todo ({ex.Message}). Cierra lo que tenga abiertos los archivos de tu perfil e inténtalo otra vez: " +
                                "tu perfil sigue en la lista y pudo haber perdido parte de sus datos.";
            return Redirect("/Perfiles");
        }
        if (rutaBase is not null) _gestor.OlvidarBase(rutaBase);
        _gestor.BorrarCookie(HttpContext);

        TempData["Mensaje"] = $"Borré todos los datos de «{perfil.Nombre}»: su base de datos, la sesión de Banner y el perfil.";
        return Redirect("/Perfiles");
    }
}
