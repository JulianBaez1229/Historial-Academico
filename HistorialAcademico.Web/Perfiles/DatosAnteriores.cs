using Microsoft.Data.Sqlite;

namespace HistorialAcademico.Web.Perfiles;

/// <summary>
/// Antes de los perfiles la aplicación guardaba una sola base (historial.db junto al proyecto) y una sola sesión de Banner (.auth/).
/// Al crear un perfil se pueden traer esos datos: se COPIAN (los originales no se tocan ni se borran) y se ofrece una sola vez.
/// </summary>
public sealed record DatosAnteriores(string? BaseDeDatos, string? Sesion)
{
    public bool Hay => BaseDeDatos is not null;

    public static DatosAnteriores Buscar(string rutaBase, string carpeta)
    {
        var sesion = Path.Combine(carpeta, ".auth", "banner.json");
        return new DatosAnteriores(File.Exists(rutaBase) ? rutaBase : null, File.Exists(sesion) ? sesion : null);
    }

    /// <summary>Copia la base (con la API de copias de SQLite, correcta aunque otro proceso la tenga abierta) y la sesión al perfil.</summary>
    public void CopiarA(string rutaBaseDestino, string carpetaDestino)
    {
        if (BaseDeDatos is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(rutaBaseDestino)!);
            using (var origen = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = BaseDeDatos, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
            using (var destino = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = rutaBaseDestino, Pooling = false }.ToString()))
            {
                origen.Open();
                destino.Open();
                origen.BackupDatabase(destino);
            }
        }

        if (Sesion is not null)
        {
            var carpetaSesion = Path.Combine(carpetaDestino, ".auth");
            Directory.CreateDirectory(carpetaSesion);
            File.Copy(Sesion, Path.Combine(carpetaSesion, "banner.json"), overwrite: true);
        }
    }
}
