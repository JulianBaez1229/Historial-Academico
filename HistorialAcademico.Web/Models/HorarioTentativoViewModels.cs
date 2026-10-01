using HistorialAcademico.Core.Entities;
using HistorialAcademico.Web.Services;

namespace HistorialAcademico.Web.Models;

public class TentativoViewModel
{
    public bool HayPensum { get; init; }
    public List<HorarioTentativo> Horarios { get; init; } = new();
    public VistaHorario? Vista { get; init; }
    public List<(int Id, string Nombre)> Planes { get; init; } = new();
    public List<OpcionPeriodo> Periodos { get; init; } = new();
    /// <summary>Período que se propone al crear un horario nuevo.</summary>
    public string PeriodoNuevo { get; init; } = "";
    public string? Mensaje { get; init; }
    public string? Error { get; init; }
}

public class NoDisponibleViewModel
{
    public HashSet<(HistorialAcademico.Core.Horarios.DiasSemana Dia, int Hora)> Celdas { get; init; } = new();
    public string? Mensaje { get; init; }
    public string? Error { get; init; }
}

public class AperturaViewModel
{
    public List<AperturaItem> Items { get; init; } = new();
    public List<OpcionPeriodo> Periodos { get; init; } = new();
    public string Periodo { get; init; } = "";
    public string PeriodoNombre { get; init; } = "";
    /// <summary>Materias faltantes que todavía no están en la lista (para agregarlas desde aquí).</summary>
    public List<OpcionMateria> Candidatas { get; init; } = new();
    public string Texto { get; init; } = "";
    public string? Mensaje { get; init; }
    public string? Error { get; init; }
}
