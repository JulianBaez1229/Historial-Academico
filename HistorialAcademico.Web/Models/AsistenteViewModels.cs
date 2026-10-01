using HistorialAcademico.Web.Services;

namespace HistorialAcademico.Web.Models;

/// <summary>El indicador «Paso X de 4» que se repite en cada pantalla del asistente.</summary>
public record PasosViewModel(int Actual)
{
    public static readonly string[] Nombres = { "Bienvenida", "Tu perfil", "Tu carrera", "Banner" };
}

public class AsistentePerfilViewModel
{
    public required string Nombre { get; init; }
    public bool TienePin { get; init; }
    public bool EnAsistente { get; init; }
    public string? Error { get; init; }
}

public class AsistenteCarreraViewModel
{
    public required VistaCarrera Vista { get; init; }
    public string? Universidad { get; init; }
    public string? Busqueda { get; init; }
    public bool EnAsistente { get; init; }
    public string? Mensaje { get; init; }
    public string? Error { get; init; }
    public bool HayFiltro => !string.IsNullOrWhiteSpace(Universidad) || !string.IsNullOrWhiteSpace(Busqueda);
}

public class AsistenteBannerViewModel
{
    /// <summary>La página de Banner de la universidad elegida (solo el host, para mostrarlo). Null si no hay ninguna configurada.</summary>
    public string? HostBanner { get; init; }
    public string? NombreUniversidad { get; init; }
    public bool TieneSesion { get; init; }
    public bool TieneHistorico { get; init; }
    public bool EnAsistente { get; init; }
    public string? Mensaje { get; init; }
    public string? Error { get; init; }
    public bool HayBanner => HostBanner is not null;
}
