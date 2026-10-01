using HistorialAcademico.Core.Pensum;
using HistorialAcademico.Core.Universidad;
using HistorialAcademico.Web.Services;

namespace HistorialAcademico.Web.Models;

public class CarreraViewModel
{
    public required VistaCarrera Vista { get; init; }
    public string? Universidad { get; init; }
    public string? Busqueda { get; init; }
    /// <summary>Las reglas de la universidad activa (escala, períodos y límites), que se muestran junto al pénsum activo.</summary>
    public required ReglasUniversidad Reglas { get; init; }
    public string? Mensaje { get; init; }
    public string? Error { get; init; }

    public bool HayFiltro => !string.IsNullOrWhiteSpace(Universidad) || !string.IsNullOrWhiteSpace(Busqueda);
}

/// <summary>El formulario para pegar el texto del plan de estudios.</summary>
public class ImportarViewModel
{
    public List<ReglasUniversidad> Universidades { get; init; } = new();
    public string? Texto { get; init; }
    public string? Universidad { get; init; }
    public string? NombreCarrera { get; init; }
    public string Version { get; init; } = "";
    public string? Error { get; init; }
}

/// <summary>La vista previa editable del pénsum importado, con lo que la validación encontró.</summary>
public class RevisarViewModel
{
    public List<FilaEditable> Filas { get; init; } = new();
    public List<ReglasUniversidad> Universidades { get; init; } = new();
    public string? Universidad { get; init; }
    public string? NombreCarrera { get; init; }
    public string Version { get; init; } = "";
    /// <summary>Lo que se avisó al leer el texto (líneas saltadas, cuatrimestres estimados…).</summary>
    public List<string> Avisos { get; init; } = new();
    /// <summary>El resultado de validar las filas (null si todavía faltan el nombre, la versión o la universidad).</summary>
    public ResultadoConstruccion? Construccion { get; init; }
    /// <summary>Por qué no se pudieron armar los datos de la carrera (nombre, versión o universidad).</summary>
    public string? ErrorDatos { get; init; }
    /// <summary>Ya existe un pénsum personal con ese nombre y versión.</summary>
    public bool Existe { get; init; }
    public bool Reemplazar { get; init; }
    public bool Usar { get; init; } = true;
    public string? Error { get; init; }

    public bool PuedeGuardar => Construccion is { EsValido: true } && ErrorDatos is null;
}

public class GuiaViewModel
{
    /// <summary>La guía ya convertida a HTML (el texto se codifica antes: ver MarkdownSencillo).</summary>
    public string? Html { get; init; }
    public string? Archivo { get; init; }
}
