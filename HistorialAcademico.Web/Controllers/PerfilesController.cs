using HistorialAcademico.Core.Perfiles;
using HistorialAcademico.Web.Models;
using HistorialAcademico.Web.Perfiles;
using Microsoft.AspNetCore.Mvc;

namespace HistorialAcademico.Web.Controllers;

/// <summary>
/// Quién usa la aplicación en este equipo: elegir perfil (con su PIN si lo tiene), crear uno y salir a cambiar de perfil.
/// Cada perfil tiene su base de datos y su sesión de Banner; los datos de uno nunca se ven desde otro.
/// </summary>
public class PerfilesController : Controller
{
    private readonly GestorPerfiles _gestor;

    public PerfilesController(GestorPerfiles gestor) => _gestor = gestor;

    private AlmacenPerfiles Almacen => _gestor.Almacen;

    /// <summary>«30 segundos», «1 minuto», «5 minutos»: la espera, redondeada hacia arriba, en lenguaje llano.</summary>
    public static string TextoEspera(TimeSpan espera)
    {
        var segundos = Math.Max(1, (int)Math.Ceiling(espera.TotalSeconds));
        if (segundos < 60) return segundos == 1 ? "1 segundo" : $"{segundos} segundos";
        var minutos = (int)Math.Ceiling(segundos / 60.0);
        return minutos == 1 ? "1 minuto" : $"{minutos} minutos";
    }

    private PerfilesIndexViewModel Lista(string? idConError = null, string? error = null)
    {
        var ahora = DateTime.UtcNow;
        return new PerfilesIndexViewModel
        {
            Perfiles = Almacen.Listar()
                .Select(p => new PerfilItem(p.Id, p.Nombre, p.TienePin, p.BloqueadoHasta is { } h && h > ahora ? h - ahora : null, p.Creado))
                .ToList(),
            IdConError = idConError,
            Error = error,
            Advertencia = Almacen.AdvertenciaAlCargar,
        };
    }

    [HttpGet]
    public IActionResult Index() => Almacen.Listar().Count == 0 ? Redirect("/Asistente/Bienvenida") : View(Lista());

    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult Entrar(string? id, string? pin)
    {
        var intento = Almacen.Intentar(id, pin);
        switch (intento.Resultado)
        {
            case ResultadoEntrada.Correcto:
                _gestor.EmitirCookie(HttpContext, id!);
                return RedirectToAction("Index", "Home");
            case ResultadoEntrada.NoExiste:
                return View(nameof(Index), Lista(null, "Ese perfil ya no existe."));
            case ResultadoEntrada.Bloqueado:
                return View(nameof(Index), Lista(id, $"Demasiados intentos con el PIN. Espera {TextoEspera(intento.Espera)} para volver a intentar."));
            default:
                var restantes = PoliticaBloqueo.FallosPermitidos - intento.FallosSeguidos;
                var mensaje = intento.Espera > TimeSpan.Zero
                    ? $"PIN incorrecto. Espera {TextoEspera(intento.Espera)} para volver a intentar."
                    : $"PIN incorrecto. Te quedan {restantes} {(restantes == 1 ? "intento" : "intentos")} antes de tener que esperar.";
                return View(nameof(Index), Lista(id, mensaje));
        }
    }

    /// <summary>Cierra el perfil: hay que elegirlo otra vez (y poner el PIN si lo tiene) para volver a entrar.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult Salir()
    {
        _gestor.BorrarCookie(HttpContext);
        return RedirectToAction(nameof(Index));
    }

    private DatosAnteriores Anteriores() => DatosAnteriores.Buscar(_gestor.BaseAnterior, _gestor.CarpetaAnterior);

    private CrearPerfilViewModel FormularioNuevo(CrearPerfilViewModel? m = null)
    {
        m ??= new CrearPerfilViewModel();
        m.EsElPrimero = Almacen.Listar().Count == 0;
        m.HayDatosAnteriores = !Almacen.DatosAnterioresAdoptados && Anteriores().Hay;
        return m;
    }

    [HttpGet]
    public IActionResult Crear() => View(FormularioNuevo());

    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult Crear(CrearPerfilViewModel m)
    {
        FormularioNuevo(m);
        m.ErrorNombre = Almacen.ErrorDeNombre(m.Nombre);
        if (!m.SinPin)
        {
            m.ErrorPin = PinHasher.ErrorDePin(m.Pin) ?? (m.Pin == m.ConfirmarPin ? null : "Los dos PIN no son iguales.");
        }
        var pin = m.SinPin ? null : m.Pin ?? "";
        // Nunca se devuelve un PIN a la página.
        m.Pin = m.ConfirmarPin = null;
        if (m.ErrorNombre is not null || m.ErrorPin is not null) return View(m);

        var (perfil, error) = Almacen.Crear(m.Nombre, pin, PasosAsistente.Carrera);
        if (perfil is null) { m.Error = error; return View(m); }

        if (m.HayDatosAnteriores && m.TraerAnteriores)
        {
            try
            {
                Anteriores().CopiarA(Almacen.RutaBase(perfil.Id), Almacen.CarpetaDe(perfil.Id));
                Almacen.MarcarDatosAnterioresAdoptados();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
            {
                // No se deja un perfil a medias: se quita y se explica qué pasó. Los datos originales no se tocaron.
                Almacen.Borrar(perfil.Id);
                m.Error = $"No pude traer los datos anteriores ({ex.Message}). Tus datos originales siguen donde estaban; el perfil no se creó.";
                return View(m);
            }
        }

        // El perfil sigue con el asistente: el paso 3 es elegir universidad y carrera.
        _gestor.EmitirCookie(HttpContext, perfil.Id);
        return RedirectToAction("Index", "Asistente");
    }
}
