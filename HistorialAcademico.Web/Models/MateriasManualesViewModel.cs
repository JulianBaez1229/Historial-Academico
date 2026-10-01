using HistorialAcademico.Web.Services;

namespace HistorialAcademico.Web.Models;

public class MateriasManualesViewModel
{
    public List<FilaManualVista> Filas { get; init; } = new();
    public bool HayPensum { get; init; }
    public string NombreUniversidad { get; init; } = "";
    /// <summary>Las materias del pénsum (código y nombre) para sugerir mientras se escribe.</summary>
    public List<(string Codigo, string Nombre)> Pensum { get; init; } = new();
    /// <summary>Períodos que se sugieren al escribir («ENE-ABR 2026»…).</summary>
    public List<string> Periodos { get; init; } = new();
    public string EjemploPeriodo { get; init; } = "";
    /// <summary>Las letras de la escala de la universidad.</summary>
    public List<string> Letras { get; init; } = new();

    // Lo que se escribió en el formulario (para no perderlo si hay un error).
    public string? Codigo { get; init; }
    public string? Periodo { get; init; }
    public string? Calificacion { get; init; }

    public string? Mensaje { get; init; }
    public string? Error { get; init; }
    /// <summary>Los problemas de un CSV, uno por línea, cuando no se pudo importar.</summary>
    public IReadOnlyList<string> Detalles { get; init; } = Array.Empty<string>();
}
