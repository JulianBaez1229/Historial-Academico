namespace HistorialAcademico.Core.Models;

/// <summary>Totales tal como los publica Banner (Horas Intentadas … PGA).</summary>
public record TotalesBanner(
    decimal HorasIntentadas,
    decimal HorasAprobadas,
    decimal HorasGanadas,
    decimal HorasPga,
    decimal PuntosCalidad,
    decimal Pga);

public class DatosAlumnoBanner
{
    /// <summary>Nombre completo tal como lo muestra Banner (en mayúsculas). Sale de la cabecera de la página, no de la tabla.</summary>
    public string? Nombre { get; set; }
    public DateOnly? FechaNacimiento { get; set; }
    public string? TipoAlumno { get; set; }
    public string? Programa { get; set; }
    public string? Escuela { get; set; }
    public string? Campus { get; set; }
    public string? Carrera { get; set; }
    public string? GradoAObtener { get; set; }
    public string? EstadoAcademico { get; set; }
}

public class MateriaHistorico
{
    /// <summary>Prefijo de la materia, ej. "ISO".</summary>
    public string Materia { get; set; } = "";
    /// <summary>Número del curso, ej. "700".</summary>
    public string Curso { get; set; } = "";
    /// <summary>Código completo (Materia + Curso), ej. "ISO700".</summary>
    public string Codigo => Materia + Curso;
    public string Campus { get; set; } = "";
    public string Nivel { get; set; } = "";
    /// <summary>Título tal como lo entrega Banner: mayúsculas, sin acentos y a veces recortado.</summary>
    public string Titulo { get; set; } = "";
    public string Calificacion { get; set; } = "";
    public decimal HorasCredito { get; set; }
    public decimal PuntosCalidad { get; set; }
}

public class PeriodoHistorico
{
    /// <summary>Ej. "MAY-AGO 2024".</summary>
    public string Nombre { get; set; } = "";
    public string Nivel { get; set; } = "";
    public string? Escuela { get; set; }
    public string? Carrera { get; set; }
    public string? TipoAlumno { get; set; }
    public string? EstadoAcademico { get; set; }
    public List<MateriaHistorico> Materias { get; } = new();
    /// <summary>Fila "Periodo Actual" de los totales del período.</summary>
    public TotalesBanner? TotalesPeriodo { get; set; }
    /// <summary>Fila "Acumulativo" al cierre de este período.</summary>
    public TotalesBanner? TotalesAcumulados { get; set; }
}

public class CursoEnProgresoHistorico
{
    public string Materia { get; set; } = "";
    public string Curso { get; set; } = "";
    public string Codigo => Materia + Curso;
    public string Campus { get; set; } = "";
    public string Nivel { get; set; } = "";
    public string Titulo { get; set; } = "";
    public decimal HorasCredito { get; set; }
}

public class PeriodoEnProgreso
{
    public string Nombre { get; set; } = "";
    public string Nivel { get; set; } = "";
    public List<CursoEnProgresoHistorico> Cursos { get; } = new();
}

/// <summary>Resultado completo del parser del Histórico Académico de Banner.</summary>
public class HistoricoBanner
{
    public DatosAlumnoBanner Alumno { get; } = new();
    /// <summary>Períodos cerrados, en el orden en que aparecen en Banner.</summary>
    public List<PeriodoHistorico> Periodos { get; } = new();
    public List<PeriodoEnProgreso> EnProgreso { get; } = new();

    /// <summary>Fila "Institución" del resumen superior (Banner la publica sin contar las materias exentas).</summary>
    public TotalesBanner? ResumenSuperior { get; set; }
    public TotalesBanner? TotalInstitucion { get; set; }
    public TotalesBanner? TotalTransferido { get; set; }
    /// <summary>Fila "Global" del bloque final. Son los totales de referencia (143 horas aprobadas, etc.).</summary>
    public TotalesBanner? TotalGlobal { get; set; }
}
