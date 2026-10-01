using HistorialAcademico.Banner;
using HistorialAcademico.Core.Perfiles;
using HistorialAcademico.Web.Data;
using HistorialAcademico.Web.Models;
using HistorialAcademico.Web.Perfiles;
using HistorialAcademico.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HistorialAcademico.Web.Controllers;

/// <summary>
/// El asistente de primer uso, en cuatro pasos: 1) bienvenida y privacidad, 2) perfil (ver <see cref="PerfilesController"/>),
/// 3) universidad y carrera, 4) conectar Banner (opcional). Se puede ir atrás, el paso en que vas se guarda en el perfil y
/// todo lo que falte se puede omitir y hacer después desde el menú.
/// </summary>
public class AsistenteController : Controller
{
    private readonly GestorPerfiles _gestor;
    private readonly PerfilActual _perfil;
    private readonly CarreraService _carrera;
    private readonly ReglasUniversidadService _reglas;
    private readonly SincronizacionService _sync;
    private readonly BannerClient _banner;
    private readonly BannerOptions _opcionesBanner;
    private readonly HistorialContext _db;

    public AsistenteController(GestorPerfiles gestor, PerfilActual perfil, CarreraService carrera, ReglasUniversidadService reglas,
        SincronizacionService sync, BannerClient banner, BannerOptions opcionesBanner, HistorialContext db)
    {
        _gestor = gestor;
        _perfil = perfil;
        _carrera = carrera;
        _reglas = reglas;
        _sync = sync;
        _banner = banner;
        _opcionesBanner = opcionesBanner;
        _db = db;
    }

    private bool EnAsistente => _perfil.Perfil is { } p && p.PasoAsistente != PasosAsistente.Terminado;

    /// <summary>Recuerda el paso en que va el perfil (solo mientras está en el asistente).</summary>
    private void Guardar(int paso)
    {
        if (_perfil.Perfil is not { } perfil) return;
        _gestor.Almacen.GuardarPaso(perfil.Id, paso);
        perfil.PasoAsistente = paso;
    }

    /// <summary>Al visitar un paso mientras se está en el asistente, ese es el paso en que se queda.</summary>
    private void Visitar(int paso)
    {
        if (EnAsistente) Guardar(paso);
    }

    private IActionResult TerminarYVolver(string mensaje)
    {
        Guardar(PasosAsistente.Terminado);
        TempData["Mensaje"] = mensaje;
        return RedirectToAction("Index", "Home");
    }

    // ── Dónde va cada quien ───────────────────────────────────────────────────────────────

    /// <summary>Lleva al paso en que quedó el perfil (o a la portada si ya terminó).</summary>
    [HttpGet]
    public IActionResult Index()
    {
        if (_perfil.Perfil is null) return Redirect("/Asistente/Bienvenida");
        return _perfil.Perfil.PasoAsistente switch
        {
            PasosAsistente.Perfil => RedirectToAction(nameof(Perfil)),
            PasosAsistente.Carrera => RedirectToAction(nameof(Carrera)),
            PasosAsistente.Banner => RedirectToAction(nameof(Banner)),
            _ => RedirectToAction("Index", "Home"),
        };
    }

    // ── Paso 2: tu perfil (al retroceder desde el 3) ──────────────────────────────────────

    [HttpGet]
    public IActionResult Perfil()
    {
        if (_perfil.Perfil is not { } perfil) return Redirect("/Asistente/Bienvenida");
        Visitar(PasosAsistente.Perfil);
        return View(new AsistentePerfilViewModel { Nombre = perfil.Nombre, TienePin = perfil.TienePin, EnAsistente = EnAsistente });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult Perfil(string? nombre)
    {
        if (_perfil.Perfil is not { } perfil) return Redirect("/Asistente/Bienvenida");
        var error = string.Equals(nombre?.Trim(), perfil.Nombre, StringComparison.Ordinal) ? null : _gestor.Almacen.Renombrar(perfil.Id, nombre);
        if (error is not null)
            return View(new AsistentePerfilViewModel { Nombre = nombre ?? "", TienePin = perfil.TienePin, EnAsistente = EnAsistente, Error = error });

        if (nombre is { Length: > 0 }) perfil.Nombre = nombre.Trim();
        if (EnAsistente) Guardar(PasosAsistente.Carrera);
        return RedirectToAction(EnAsistente ? nameof(Carrera) : nameof(Perfil));
    }

    // ── Paso 3: universidad y carrera ─────────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> Carrera(string? universidad, string? q, CancellationToken ct)
    {
        if (_perfil.Perfil is null) return Redirect("/Asistente/Bienvenida");
        Visitar(PasosAsistente.Carrera);
        return View(new AsistenteCarreraViewModel
        {
            Vista = await _carrera.ObtenerAsync(universidad, q, ct),
            Universidad = universidad,
            Busqueda = q,
            EnAsistente = EnAsistente,
            Mensaje = TempData["Mensaje"] as string,
            Error = TempData["Error"] as string,
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Carrera(string? clave, CancellationToken ct)
    {
        var r = await _carrera.ActivarAsync(clave, ct);
        if (!r.Ok)
        {
            TempData["Error"] = r.Mensaje;
            return RedirectToAction(nameof(Carrera));
        }
        TempData["Mensaje"] = r.Mensaje;
        if (EnAsistente) Guardar(PasosAsistente.Banner);
        return RedirectToAction(EnAsistente ? nameof(Banner) : nameof(Carrera));
    }

    /// <summary>Pasa al siguiente paso sin elegir carrera (se puede hacer después en «Carrera y pénsum»).</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult OmitirCarrera()
    {
        if (EnAsistente) Guardar(PasosAsistente.Banner);
        return RedirectToAction(EnAsistente ? nameof(Banner) : nameof(Carrera));
    }

    // ── Paso 4: conectar Banner (opcional) ────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> Banner(CancellationToken ct)
    {
        if (_perfil.Perfil is null) return Redirect("/Asistente/Bienvenida");
        Visitar(PasosAsistente.Banner);
        return View(new AsistenteBannerViewModel
        {
            HostBanner = Uri.TryCreate(_opcionesBanner.BaseUrl, UriKind.Absolute, out var uri) ? uri.Host : null,
            NombreUniversidad = _perfil.UniversidadId is null ? null : _reglas.Activa.Nombre,
            TieneSesion = _banner.TieneSesionGuardada,
            TieneHistorico = await _db.DatosAlumno.AnyAsync(ct),
            EnAsistente = EnAsistente,
            Mensaje = TempData["Mensaje"] as string,
            Error = TempData["Error"] as string,
        });
    }

    /// <summary>
    /// Abre Chromium visible para que inicies sesión tú mismo en Banner (la aplicación nunca ve tu usuario ni tu contraseña),
    /// guarda la sesión y trae tu histórico. La petición dura mientras esperas el inicio de sesión (hasta 5 minutos).
    /// </summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Conectar(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_opcionesBanner.BaseUrl))
        {
            TempData["Error"] = "Tu universidad no tiene una dirección de Banner configurada, así que no puedo conectarme.";
            return RedirectToAction(nameof(Banner));
        }

        var r = await _sync.ActualizarAsync(permitirLogin: true, ct);
        if (!r.Exito)
        {
            TempData["Error"] = r.Mensaje;
            return RedirectToAction(nameof(Banner));
        }
        if (EnAsistente) return TerminarYVolver($"Listo: Banner quedó conectado. {r.Mensaje}");
        TempData["Mensaje"] = $"Banner quedó conectado. {r.Mensaje}";
        return RedirectToAction(nameof(Banner));
    }

    /// <summary>Termina el asistente, con Banner conectado o sin él.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult Terminar() => TerminarYVolver("Listo, tu perfil está configurado. Todo lo que omitiste lo puedes hacer después desde el menú.");
}
