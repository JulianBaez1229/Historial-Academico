using HistorialAcademico.Banner;
using HistorialAcademico.Core.Perfiles;
using HistorialAcademico.Web.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace HistorialAcademico.Web.Perfiles;

/// <summary>
/// Lo que la aplicación sabe de los perfiles de este equipo: dónde están sus datos (una carpeta del usuario, fuera del repositorio),
/// quién entró (una cookie protegida que solo dice «este perfil», nunca el PIN) y cuándo hay que crear o migrar su base.
/// </summary>
public sealed class GestorPerfiles
{
    public const string NombreCookie = "ha_perfil";

    private readonly IConfiguration _configuracion;
    private readonly IDataProtector _protector;
    private readonly object _candado = new();
    private readonly HashSet<string> _migradas = new(StringComparer.OrdinalIgnoreCase);

    public GestorPerfiles(IConfiguration configuracion, IWebHostEnvironment entorno, IDataProtectionProvider proteccion)
    {
        _configuracion = configuracion;
        _protector = proteccion.CreateProtector("HistorialAcademico.Perfil.v1");

        // Las iteraciones del hash del PIN solo se bajan en las pruebas (por velocidad); nunca por debajo de 1000.
        var iteraciones = Math.Max(1000, configuracion.GetValue("Perfiles:IteracionesPin", PinHasher.IteracionesPredeterminadas));
        Almacen = new AlmacenPerfiles(CarpetaDatosUsuario(configuracion), iteracionesPin: iteraciones);

        if (configuracion["Perfiles:BaseFija"] is { Length: > 0 } fija)
        {
            EsFijo = true;
            BaseFija = Path.GetFullPath(fija, entorno.ContentRootPath);
            CarpetaFija = configuracion["Banner:CarpetaDatos"] is { Length: > 0 } c ? Path.GetFullPath(c, entorno.ContentRootPath) : Path.GetDirectoryName(BaseFija);
        }

        // Los datos de la versión anterior (una sola base junto al proyecto): se ofrecen una vez, nunca se mueven ni se borran.
        BaseAnterior = configuracion["Perfiles:BaseAnterior"] is { Length: > 0 } b
            ? Path.GetFullPath(b, entorno.ContentRootPath)
            : Path.GetFullPath("historial.db", entorno.ContentRootPath);
        CarpetaAnterior = configuracion["Perfiles:CarpetaAnterior"] is { Length: > 0 } ca
            ? Path.GetFullPath(ca, entorno.ContentRootPath)
            : new BannerOptions().RaizDatos;
    }

    /// <summary>La carpeta de datos del usuario: «Perfiles:Carpeta», o %LOCALAPPDATA%\HistorialAcademico.</summary>
    public static string CarpetaDatosUsuario(IConfiguration configuracion) =>
        configuracion["Perfiles:Carpeta"] is { Length: > 0 } c
            ? Path.GetFullPath(c)
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HistorialAcademico");

    public AlmacenPerfiles Almacen { get; }

    /// <summary>Una sola base fija, sin perfiles ni PIN (pruebas y desarrollo con «Perfiles:BaseFija»).</summary>
    public bool EsFijo { get; }
    public string? BaseFija { get; }
    public string? CarpetaFija { get; }
    public string? UniversidadFija { get; private set; }

    public string BaseAnterior { get; }
    public string CarpetaAnterior { get; }

    // ── Cookie de perfil ──────────────────────────────────────────────────────────────────

    /// <summary>El id de perfil de la cookie, o null si no hay o no es válida (otra clave, alterada).</summary>
    public string? LeerCookie(HttpRequest peticion)
    {
        if (!peticion.Cookies.TryGetValue(NombreCookie, out var valor) || string.IsNullOrEmpty(valor)) return null;
        try { return _protector.Unprotect(valor); }
        catch (System.Security.Cryptography.CryptographicException) { return null; }
    }

    /// <summary>De sesión: al cerrar el navegador hay que volver a entrar (con el PIN si el perfil lo tiene).</summary>
    public void EmitirCookie(HttpContext contexto, string id) =>
        contexto.Response.Cookies.Append(NombreCookie, _protector.Protect(id), new CookieOptions
        {
            HttpOnly = true, SameSite = SameSiteMode.Lax, Secure = contexto.Request.IsHttps, IsEssential = true,
        });

    public void BorrarCookie(HttpContext contexto) => contexto.Response.Cookies.Delete(NombreCookie);

    // ── Base de datos del perfil ──────────────────────────────────────────────────────────

    /// <summary>
    /// Crea la base del perfil o le aplica las migraciones pendientes, la primera vez que se abre en esta ejecución.
    /// Si el perfil aún no tenía universidad y la base ya tiene un pénsum elegido, se toma la de ese pénsum.
    /// </summary>
    public void AsegurarBase(PerfilActual actual, HistorialContext db)
    {
        if (actual.RutaBase is null) return;
        lock (_candado)
        {
            if (_migradas.Contains(actual.RutaBase)) return;
            db.Database.Migrate();
            if (actual.UniversidadId is null && db.PensumActivo.AsNoTracking().FirstOrDefault() is { } activo)
                actual.GuardarUniversidad(activo.Universidad);
            _migradas.Add(actual.RutaBase);
        }
    }

    /// <summary>Olvida que la base ya se migró (al borrar un perfil su carpeta desaparece).</summary>
    public void OlvidarBase(string ruta)
    {
        lock (_candado) _migradas.Remove(ruta);
    }

    public void GuardarUniversidad(PerfilActual actual, string universidad)
    {
        if (EsFijo) UniversidadFija = universidad;
        else if (actual.Perfil is { } perfil) Almacen.GuardarUniversidad(perfil.Id, universidad);
    }

    /// <summary>
    /// Las opciones de Banner de este perfil: la misma configuración para todos, pero cada uno con su carpeta de sesión.
    /// La dirección de Banner es la de la universidad elegida (su «urlBanner»); si tú configuraste «Banner:BaseUrl» (user-secrets), esa manda.
    /// </summary>
    public BannerOptions OpcionesBanner(PerfilActual actual, string? urlDeLaUniversidad = null)
    {
        var opciones = _configuracion.GetSection("Banner").Get<BannerOptions>() ?? new BannerOptions();
        if (actual.CarpetaDatos is not null) opciones.CarpetaDatos = actual.CarpetaDatos;

        if (string.IsNullOrWhiteSpace(opciones.BaseUrl) && Uri.TryCreate(urlDeLaUniversidad, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
        {
            opciones.BaseUrl = uri.ToString();
            // Banner vive en el dominio de la universidad (alumnos.unapec.edu.do → unapec.edu.do); otro host cuenta como pantalla de inicio de sesión.
            var dominio = DominioBase(uri.Host);
            if (!opciones.HostsBanner.Contains(dominio, StringComparer.OrdinalIgnoreCase)) opciones.HostsBanner.Add(dominio);
        }
        return opciones;
    }

    /// <summary>Quita el primer nombre del host si quedan al menos dos: «alumnos.unapec.edu.do» → «unapec.edu.do».</summary>
    public static string DominioBase(string host)
    {
        var partes = host.Split('.');
        return partes.Length >= 3 ? string.Join('.', partes.Skip(1)) : host;
    }
}
