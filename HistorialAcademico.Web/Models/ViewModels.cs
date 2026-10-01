using HistorialAcademico.Core.Entities;
using HistorialAcademico.Core.Pensum;
using HistorialAcademico.Web.Services;

namespace HistorialAcademico.Web.Models;

public class PensumViewModel
{
    public required EstadoAcademico Estado { get; init; }
    public string? Mensaje { get; init; }
    public List<string> Errores { get; init; } = new();
    public List<string> Advertencias { get; init; } = new();
}

public class EquivalenciasViewModel
{
    public List<Equivalencia> Lista { get; init; } = new();
    /// <summary>Código → nombre de las materias del pénsum cargado (para sugerir y mostrar nombres).</summary>
    public Dictionary<string, string> MateriasPensum { get; init; } = new();
    /// <summary>Las equivalencias que declara el pénsum elegido del catálogo (solo lectura).</summary>
    public List<Equivalencia> Declaradas { get; init; } = new();
    /// <summary>El pénsum elegido del catálogo (null si el pénsum se cargó a mano).</summary>
    public PensumActivo? Activo { get; init; }
    /// <summary>Equivalencias tuyas que apuntan a una materia que no está en el pénsum actual (no hacen nada).</summary>
    public HashSet<int> SinEfecto { get; init; } = new();
    /// <summary>Equivalencias tuyas que el pénsum ya declara (repetidas; se pueden borrar).</summary>
    public HashSet<int> YaDeclaradas { get; init; } = new();
    public string? Mensaje { get; init; }
    public string? Error { get; init; }
}

public class DetalleTomadaViewModel
{
    public required EstadoAcademico Estado { get; init; }
    /// <summary>Todos los intentos de la materia (una materia repetida aparece más de una vez); el último es el principal.</summary>
    public required List<MateriaCursada> Intentos { get; init; }
    public MateriaCursada Principal => Intentos[^1];
    public MateriaPensum? Pensum { get; init; }
}

/// <summary>Un requisito de una materia del pénsum con su cumplimiento actual.</summary>
public record RequisitoEstado(string Texto, string Situacion, string Badge, bool Cumplido);

public class DetalleFaltanteViewModel
{
    public required EstadoAcademico Estado { get; init; }
    public required EstadoMateriaPensum Materia { get; init; }
    public required List<RequisitoEstado> Requisitos { get; init; }
    /// <summary>Materias del pénsum que tienen a esta como prerrequisito.</summary>
    public required List<EstadoMateriaPensum> Desbloquea { get; init; }
    public IReadOnlyList<OpcionElectiva>? Opciones { get; init; }
}

public class SincronizacionesViewModel
{
    public required List<Sincronizacion> Historial { get; init; }
    public string? Mensaje { get; init; }
    public string? Error { get; init; }
}
