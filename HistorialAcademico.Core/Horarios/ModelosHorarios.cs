namespace HistorialAcademico.Core.Horarios;

/// <summary>Días de la semana como banderas: un bloque de clase puede darse varios días (martes y jueves).</summary>
[Flags]
public enum DiasSemana
{
    Ninguno = 0,
    Lunes = 1,
    Martes = 2,
    Miercoles = 4,
    Jueves = 8,
    Viernes = 16,
    Sabado = 32,
    Domingo = 64,
}

/// <summary>Un bloque de reunión de una sección (los días con su hora de inicio y fin).</summary>
public class BloqueBanner
{
    public DiasSemana Dias { get; init; }
    /// <summary>Hora de inicio ya normalizada (Banner escribe 0801 para las 8:00).</summary>
    public TimeOnly? Inicio { get; init; }
    public TimeOnly? Fin { get; init; }
    public string Edificio { get; init; } = "";
    public string Aula { get; init; } = "";
    /// <summary>Tipo de reunión (CLASE…).</summary>
    public string TipoReunion { get; init; } = "";
    /// <summary>Tipo de horario del bloque (LAB, VIR, VIA…).</summary>
    public string TipoHorario { get; init; } = "";
    public DateOnly? FechaInicio { get; init; }
    public DateOnly? FechaFin { get; init; }

    /// <summary>
    /// Parte virtual asincrónica: tiene una franja nominal pero ningún día fijo. No ocupa un día de la semana
    /// ni puede provocar choques de horario.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool SinDiaFijo => Dias == DiasSemana.Ninguno;
}

/// <summary>Una sección ofertada tal como la publica Banner. Del profesor solo se conserva el nombre.</summary>
public class SeccionBanner
{
    /// <summary>Código de período de Banner (202630).</summary>
    public string Periodo { get; init; } = "";
    public string PeriodoDescripcion { get; init; } = "";
    public string Nrc { get; init; } = "";
    public string Seccion { get; init; } = "";
    public string Materia { get; init; } = "";
    public string Curso { get; init; } = "";
    /// <summary>Código completo como en el pénsum (ISO625).</summary>
    public string Codigo { get; init; } = "";
    /// <summary>Título de Banner: mayúsculas, sin acentos y recortado a 30 caracteres.</summary>
    public string Titulo { get; init; } = "";
    public decimal Creditos { get; init; }
    public string Campus { get; init; } = "";
    public string TipoHorario { get; init; } = "";
    public string Metodo { get; init; } = "";
    public List<string> Profesores { get; init; } = new();
    public int CupoMaximo { get; init; }
    public int Inscritos { get; init; }
    public int CuposDisponibles { get; init; }
    public bool Abierta { get; init; }
    public List<BloqueBanner> Bloques { get; init; } = new();

    /// <summary>Tenía cupo y ya no queda. Un curso especial con cupo 0 (nunca tuvo cupos) no está «llena».</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool Llena => CupoMaximo > 0 && CuposDisponibles <= 0;

    [System.Text.Json.Serialization.JsonIgnore]
    public bool SinCupoAsignado => CupoMaximo <= 0;
}

/// <summary>Resultado de una búsqueda de secciones (todas las páginas juntas).</summary>
public class ResultadoBusqueda
{
    public int Total { get; init; }
    public List<SeccionBanner> Secciones { get; init; } = new();
    /// <summary>Sin ninguna sección publicada: un estado normal antes de que se abra la selección.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool SinSecciones => Total == 0 && Secciones.Count == 0;
}

/// <summary>Una materia del pénsum dentro de una consulta por lote: su clave (código del pénsum) y lo que hay que preguntarle a Banner.</summary>
public record LoteConsulta(string Clave, IReadOnlyList<ConsultaBanner> Consultas);

/// <summary>Lo que devolvió Banner para un elemento del lote: las secciones, o el motivo por el que esa materia falló.</summary>
public record ResultadoLote(ResultadoBusqueda? Busqueda, string? Error);

/// <summary>Un período tal como lo lista Banner (getTerms).</summary>
public record PeriodoBanner(string Codigo, string Descripcion, bool SoloVer);
