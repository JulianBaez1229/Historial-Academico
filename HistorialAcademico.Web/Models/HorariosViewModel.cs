using HistorialAcademico.Web.Services;

namespace HistorialAcademico.Web.Models;

public record OpcionMateria(string Codigo, string Nombre, int Cuatrimestre, bool Consultable);

public record OpcionPeriodo(string Codigo, string Nombre);

public class HorariosViewModel
{
    public bool HayPensum { get; init; }
    public bool HayHistorico { get; init; }
    /// <summary>Materias del pénsum que faltan y se pueden consultar por horario.</summary>
    public List<OpcionMateria> Materias { get; init; } = new();
    /// <summary>Faltantes que no se consultan por horario (Trabajo Final de Grado, pasantía).</summary>
    public List<OpcionMateria> SinHorario { get; init; } = new();
    public List<OpcionPeriodo> Periodos { get; init; } = new();
    public string? Materia { get; init; }
    public string Periodo { get; init; } = "";
    public VistaSecciones? Vista { get; init; }
    public List<OfertaPrevia> Previa { get; init; } = new();
    /// <summary>Profesores que han dado la materia elegida, según lo consultado en Banner (solo nombres y períodos).</summary>
    public List<ProfesorDeMateria> Profesores { get; init; } = new();
    /// <summary>Materias marcadas para solicitar su apertura.</summary>
    public HashSet<string> AperturaMarcadas { get; init; } = new();
    /// <summary>Materias disponibles ahora (cumples sus requisitos) con lo que se sabe de ellas en el período elegido.</summary>
    public List<FilaPanorama> Panorama { get; init; } = new();
    /// <summary>Consulta de varias materias en curso, o el resumen de la última de esta ejecución.</summary>
    public EstadoConsultaMasiva? Masiva { get; init; }
    public string? Mensaje { get; init; }
    public string? Error { get; init; }
}
