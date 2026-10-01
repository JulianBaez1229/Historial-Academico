namespace HistorialAcademico.Core.Entities;

/// <summary>
/// El pénsum que la persona eligió del catálogo (pensums/&lt;universidad&gt;/&lt;carrera&gt;-&lt;versión&gt;.json). Una sola fila.
/// Si no hay fila, el pénsum se cargó a mano desde un CSV (o todavía no hay ninguno).
/// </summary>
public class PensumActivo
{
    public int Id { get; set; }
    public string Universidad { get; set; } = "";
    public string Carrera { get; set; } = "";
    public string Version { get; set; } = "";
    /// <summary>Nombre para mostrar, guardado por si el archivo del catálogo cambia o desaparece.</summary>
    public string NombreCarrera { get; set; } = "";
    /// <summary>Cuándo se aplicó (UTC).</summary>
    public DateTime Aplicado { get; set; }

    /// <summary>unapec/ingenieria-software-11.json</summary>
    public string Clave => $"{Universidad}/{Carrera}-{Version}.json";
}
