namespace HistorialAcademico.Web.Models;

public record PerfilItem(string Id, string Nombre, bool TienePin, TimeSpan? Espera, DateTime Creado);

/// <summary>La pantalla de elegir perfil.</summary>
public class PerfilesIndexViewModel
{
    public List<PerfilItem> Perfiles { get; set; } = new();
    /// <summary>El perfil donde falló el PIN (para mostrar el error debajo de su campo).</summary>
    public string? IdConError { get; set; }
    public string? Error { get; set; }
    public string? Advertencia { get; set; }
}

/// <summary>La pantalla de crear un perfil.</summary>
public class CrearPerfilViewModel
{
    public string? Nombre { get; set; }
    /// <summary>Entrar sin PIN (queda abierto para quien use este equipo).</summary>
    public bool SinPin { get; set; }
    public string? Pin { get; set; }
    public string? ConfirmarPin { get; set; }
    public bool TraerAnteriores { get; set; } = true;
    /// <summary>Hay datos de la versión anterior (una sola base junto al proyecto) que se pueden traer a este perfil.</summary>
    public bool HayDatosAnteriores { get; set; }
    public bool EsElPrimero { get; set; }
    public string? ErrorNombre { get; set; }
    public string? ErrorPin { get; set; }
    public string? Error { get; set; }
}
