using HistorialAcademico.Core.Horarios;

namespace HistorialAcademico.Core.Entities;

/// <summary>Un horario semanal armado a mano con secciones de Banner de un período; se puede asociar a un escenario del planificador.</summary>
public class HorarioTentativo
{
    public int Id { get; set; }
    public string Nombre { get; set; } = "";
    /// <summary>Código de período de Banner (202710).</summary>
    public string Periodo { get; set; } = "";
    /// <summary>Escenario del planificador al que se asocia (null = ninguno). Si el escenario se borra, queda sin asociar.</summary>
    public int? PlanEstudioId { get; set; }
    public DateTime Creado { get; set; }
    public DateTime Actualizado { get; set; }
    public List<SeccionElegida> Secciones { get; set; } = new();
}

/// <summary>Una sección elegida para un horario tentativo. Se guarda el NRC (con el período del horario) y la materia.</summary>
public class SeccionElegida
{
    public int Id { get; set; }
    public int HorarioTentativoId { get; set; }
    public string Nrc { get; set; } = "";
    /// <summary>Materia de la sección (ISO625); solo puede haber una sección por materia en un horario.</summary>
    public string Codigo { get; set; } = "";
    /// <summary>Etiqueta de respaldo por si la sección deja de aparecer al volver a consultar Banner.</summary>
    public string Etiqueta { get; set; } = "";
}

/// <summary>Una franja de la semana en la que no puedo tomar clases (por trabajo, por ejemplo). Es una preferencia global.</summary>
public class BloqueNoDisponible
{
    public int Id { get; set; }
    /// <summary>Un solo día (una bandera de <see cref="DiasSemana"/>).</summary>
    public DiasSemana Dia { get; set; }
    /// <summary>Minutos desde la medianoche (8:00 = 480).</summary>
    public int DesdeMin { get; set; }
    public int HastaMin { get; set; }
}

/// <summary>Una materia que necesito que abran (no tiene secciones publicadas) y quiero recordar solicitar en la escuela.</summary>
public class AperturaSolicitada
{
    public int Id { get; set; }
    public string Codigo { get; set; } = "";
    public string Nota { get; set; } = "";
    public DateTime Marcada { get; set; }
}
