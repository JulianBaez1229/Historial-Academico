namespace HistorialAcademico.Banner;

/// <summary>Configuración de Banner. <c>BaseUrl</c> viene de user-secrets (Banner:BaseUrl).</summary>
public class BannerOptions
{
    public string BaseUrl { get; set; } = "";

    /// <summary>Segundos que se espera a que el usuario inicie sesión manualmente (incluye MFA).</summary>
    public int TiempoEsperaLoginSegundos { get; set; } = 300;

    /// <summary>
    /// Pantalla "Consultar programación académica" de Banner 9 (Registration): secciones con profesor, horario y cupos.
    /// No es un secreto; se puede cambiar si la universidad mueve la página.
    /// </summary>
    public string UrlProgramacionAcademica { get; set; } =
        "https://registro.unapec.edu.do/StudentRegistrationSsb/ssb/term/termSelection?mode=search";

    /// <summary>
    /// Dominios que pertenecen a Banner (incluye sus subdominios). Cualquier otro host al que te redirija Banner
    /// (login.microsoftonline.com, cierre de sesión…) se toma como pantalla de autenticación.
    /// </summary>
    public List<string> HostsBanner { get; set; } = new() { "unapec.edu.do" };

    /// <summary>Segundos que se espera a que una página de Banner empiece a mostrarse antes de rendirse.</summary>
    public int TiempoNavegacionSegundos { get; set; } = 60;

    /// <summary>Milisegundos de pausa entre una petición de secciones y la siguiente (nunca se consulta a ráfagas).</summary>
    public int PausaEntreConsultasMs { get; set; } = 1500;

    /// <summary>Secciones que se piden por página (Banner las entrega por páginas).</summary>
    public int TamanoPaginaSecciones { get; set; } = 50;

    /// <summary>Carpeta raíz de datos locales (.auth/, samples/). Si es vacía, se usa la carpeta de la solución.</summary>
    public string? CarpetaDatos { get; set; }

    public string RutaSesion => Path.Combine(RaizDatos, ".auth", "banner.json");
    public string CarpetaMuestras => Path.Combine(RaizDatos, "samples");

    public string RaizDatos => !string.IsNullOrWhiteSpace(CarpetaDatos) ? CarpetaDatos : BuscarRaizSolucion();

    private static string BuscarRaizSolucion()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (dir.GetFiles("*.sln").Length > 0) return dir.FullName;
            dir = dir.Parent;
        }
        return Directory.GetCurrentDirectory();
    }
}
