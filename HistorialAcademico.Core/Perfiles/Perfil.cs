namespace HistorialAcademico.Core.Perfiles;

/// <summary>
/// Una persona que usa la aplicación en este equipo. Cada perfil tiene su propia carpeta con su base de datos y su sesión de Banner,
/// así que los datos de uno nunca se mezclan con los de otro. Del PIN solo se guarda el hash.
/// </summary>
public class Perfil
{
    public string Id { get; set; } = "";
    public string Nombre { get; set; } = "";
    public DateTime Creado { get; set; }
    /// <summary>El PIN con hash (ver <see cref="PinHasher"/>); null si el perfil no tiene PIN.</summary>
    public string? PinHash { get; set; }
    /// <summary>Intentos fallidos seguidos de PIN.</summary>
    public int Fallos { get; set; }
    /// <summary>Hasta cuándo (UTC) no se aceptan intentos, tras varios fallos.</summary>
    public DateTime? BloqueadoHasta { get; set; }
    /// <summary>La universidad del pénsum elegido (para usar sus reglas al abrir el perfil).</summary>
    public string? UniversidadId { get; set; }
    /// <summary>El paso del asistente de primer uso en que va el perfil (ver <see cref="PasosAsistente"/>); 0 si ya terminó o nunca lo usó.</summary>
    public int PasoAsistente { get; set; }

    public bool TienePin => !string.IsNullOrEmpty(PinHash);
}

/// <summary>Los pasos del asistente de primer uso: 1) bienvenida (no necesita perfil), 2) perfil, 3) carrera, 4) Banner.</summary>
public static class PasosAsistente
{
    public const int Terminado = 0, Perfil = 2, Carrera = 3, Banner = 4;
    public static bool EsPasoValido(int paso) => paso is Perfil or Carrera or Banner;
}

/// <summary>El resultado de intentar entrar a un perfil.</summary>
public enum ResultadoEntrada
{
    Correcto,
    PinIncorrecto,
    /// <summary>Demasiados intentos fallidos: hay que esperar.</summary>
    Bloqueado,
    NoExiste,
}

public record IntentoDeEntrada(ResultadoEntrada Resultado, TimeSpan Espera = default, int FallosSeguidos = 0);

/// <summary>Cuánto hay que esperar tras varios PIN incorrectos seguidos.</summary>
public static class PoliticaBloqueo
{
    /// <summary>Fallos que se permiten antes de empezar a hacer esperar.</summary>
    public const int FallosPermitidos = 4;
    public static readonly TimeSpan Maximo = TimeSpan.FromMinutes(15);

    /// <summary>Del quinto fallo seguido en adelante: 30 s, 1 min, 2 min, 4 min… hasta 15 minutos.</summary>
    public static TimeSpan Espera(int fallosSeguidos)
    {
        if (fallosSeguidos <= FallosPermitidos) return TimeSpan.Zero;
        var exponente = Math.Min(fallosSeguidos - FallosPermitidos - 1, 10);
        var espera = TimeSpan.FromSeconds(30 * Math.Pow(2, exponente));
        return espera > Maximo ? Maximo : espera;
    }
}
