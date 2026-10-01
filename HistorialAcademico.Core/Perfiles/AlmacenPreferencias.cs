using System.Text;
using System.Text.Json;

namespace HistorialAcademico.Core.Perfiles;

/// <summary>Preferencias de este equipo (valen para todos los perfiles): lo que se guarda en preferencias.json.</summary>
public class PreferenciasEquipo
{
    /// <summary>Consultar en GitHub si hay una versión nueva al abrir la aplicación. Encendido por omisión; apagarlo deja la aplicación sin ninguna conexión propia.</summary>
    public bool AvisarActualizaciones { get; set; } = true;

    /// <summary>La versión que la persona dijo que no le interesa (el aviso no vuelve hasta que salga una más nueva).</summary>
    public string? VersionIgnorada { get; set; }
}

/// <summary>
/// Guarda las preferencias del equipo en la carpeta de datos del usuario. Un archivo dañado o ausente equivale a las preferencias por omisión:
/// nunca impide arrancar. La escritura reemplaza el archivo completo, así que no queda a medias.
/// </summary>
public sealed class AlmacenPreferencias
{
    private static readonly JsonSerializerOptions Formato = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private readonly object _candado = new();

    public AlmacenPreferencias(string raiz) => Raiz = Path.GetFullPath(raiz);

    public string Raiz { get; }
    public string RutaArchivo => Path.Combine(Raiz, "preferencias.json");

    public PreferenciasEquipo Cargar()
    {
        lock (_candado) return CargarSinCandado();
    }

    private PreferenciasEquipo CargarSinCandado()
    {
        try
        {
            if (!File.Exists(RutaArchivo)) return new PreferenciasEquipo();
            return JsonSerializer.Deserialize<PreferenciasEquipo>(File.ReadAllText(RutaArchivo, Encoding.UTF8), Formato) ?? new PreferenciasEquipo();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new PreferenciasEquipo();
        }
    }

    /// <summary>Cambia una preferencia y la guarda. Si no se puede escribir (carpeta de solo lectura) devuelve false: el cambio no se conserva.</summary>
    public bool Cambiar(Action<PreferenciasEquipo> cambio)
    {
        lock (_candado)
        {
            var preferencias = CargarSinCandado();
            cambio(preferencias);
            try
            {
                Directory.CreateDirectory(Raiz);
                var temporal = RutaArchivo + ".tmp";
                File.WriteAllText(temporal, JsonSerializer.Serialize(preferencias, Formato), new UTF8Encoding(false));
                File.Move(temporal, RutaArchivo, overwrite: true);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }
    }
}
