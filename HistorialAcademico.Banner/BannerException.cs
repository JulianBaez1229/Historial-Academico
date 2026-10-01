namespace HistorialAcademico.Banner;

public class BannerException : Exception
{
    public BannerException(string mensaje) : base(mensaje) { }
    public BannerException(string mensaje, Exception inner) : base(mensaje, inner) { }
}

/// <summary>La sesión guardada ya no es válida: Banner redirigió al login.</summary>
public class BannerSesionExpiradaException : BannerException
{
    public BannerSesionExpiradaException()
        : base("La sesión de Banner expiró. Inicia sesión de nuevo.") { }
}
