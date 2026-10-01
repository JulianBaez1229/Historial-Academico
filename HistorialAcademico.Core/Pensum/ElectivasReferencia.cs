namespace HistorialAcademico.Core.Pensum;

public record OpcionElectiva(string Codigo, string Nombre);

/// <summary>Opciones de las electivas del pénsum ISO-11 (datos públicos del plan de estudios).</summary>
public static class ElectivasReferencia
{
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<OpcionElectiva>> Opciones =
        new Dictionary<string, IReadOnlyList<OpcionElectiva>>(StringComparer.OrdinalIgnoreCase)
        {
            ["E077"] = new[]
            {
                new OpcionElectiva("ADM103", "Fundamentos de Administración"),
                new OpcionElectiva("ADM536", "Creación e Innovación de Negocios"),
                new OpcionElectiva("ADM540", "Liderazgo y Habilidades Directivas"),
            },
            ["E078"] = new[]
            {
                new OpcionElectiva("ISO110", "TIC Inclusivas"),
                new OpcionElectiva("ISO112", "Ingeniería Telemática"),
                new OpcionElectiva("ISO114", "Gestión de Procesos"),
            },
            ["E079"] = new[]
            {
                new OpcionElectiva("ISO116", "Reconocimiento de Patrones"),
                new OpcionElectiva("ISO118", "Nanotecnología"),
                new OpcionElectiva("ISO120", "Bioinformática"),
            },
        };

    public const string Certificacion =
        "Alternativa por certificación: Dirección de Proyectos (ISO122) o Desarrollo en Emprendimiento (ADM202 → ADM537 → ADM538). " +
        "Si se elige una certificación, no se toman las Electivas Generales.";

    public const string RequisitosDeGraduacion =
        "Además del pénsum, para graduarte debes aprobar un deporte, los 8 niveles de inglés, Actitud Profesional (60 horas) y la Pasantía (150-300 horas).";
}
