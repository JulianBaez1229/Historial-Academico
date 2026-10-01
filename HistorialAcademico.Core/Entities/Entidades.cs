namespace HistorialAcademico.Core.Entities;

/// <summary>Datos del alumno y totales globales publicados por Banner (una sola fila). Se reemplaza en cada sincronización.</summary>
public class DatosAlumno
{
    public int Id { get; set; }
    public string? Nombre { get; set; }
    public DateOnly? FechaNacimiento { get; set; }
    public string? TipoAlumno { get; set; }
    public string? Programa { get; set; }
    public string? Escuela { get; set; }
    public string? Campus { get; set; }
    public string? Carrera { get; set; }
    public string? GradoAObtener { get; set; }
    public string? EstadoAcademico { get; set; }

    // Fila "Global" de Banner: sirve para comparar contra el índice que calcula la app.
    public decimal TotalHorasIntentadas { get; set; }
    public decimal TotalHorasAprobadas { get; set; }
    public decimal TotalHorasGanadas { get; set; }
    public decimal TotalHorasPga { get; set; }
    public decimal TotalPuntosCalidad { get; set; }
    public decimal TotalPga { get; set; }
}

public class Periodo
{
    public int Id { get; set; }
    /// <summary>Posición cronológica (1 = el más antiguo), según el orden del histórico de Banner.</summary>
    public int Orden { get; set; }
    /// <summary>Ej. "MAY-AGO 2024".</summary>
    public string Nombre { get; set; } = "";
    public string Nivel { get; set; } = "";
    public string? Escuela { get; set; }
    public string? Carrera { get; set; }
    public string? TipoAlumno { get; set; }
    public string? EstadoAcademico { get; set; }

    // Totales del período tal como los publica Banner.
    public decimal HorasIntentadas { get; set; }
    public decimal HorasAprobadas { get; set; }
    public decimal HorasGanadas { get; set; }
    public decimal HorasPga { get; set; }
    public decimal PuntosCalidad { get; set; }
    public decimal Pga { get; set; }

    // Acumulado al cierre del período.
    public decimal AcumHorasIntentadas { get; set; }
    public decimal AcumHorasAprobadas { get; set; }
    public decimal AcumHorasGanadas { get; set; }
    public decimal AcumHorasPga { get; set; }
    public decimal AcumPuntosCalidad { get; set; }
    public decimal AcumPga { get; set; }

    public List<MateriaCursada> Materias { get; set; } = new();
}

public class MateriaCursada
{
    public int Id { get; set; }
    public int PeriodoId { get; set; }
    public Periodo? Periodo { get; set; }
    /// <summary>Código completo (Materia + Curso), ej. "ISO700".</summary>
    public string Codigo { get; set; } = "";
    public string Materia { get; set; } = "";
    public string Curso { get; set; } = "";
    /// <summary>Título de Banner: mayúsculas, sin acentos, a veces recortado.</summary>
    public string Titulo { get; set; } = "";
    public string Calificacion { get; set; } = "";
    public decimal HorasCredito { get; set; }
    public decimal PuntosCalidad { get; set; }
    public string Campus { get; set; } = "";
    public string Nivel { get; set; } = "";
}

public class CursoEnProgreso
{
    public int Id { get; set; }
    /// <summary>Nombre del período en curso, ej. "SEP-DIC 2026".</summary>
    public string Periodo { get; set; } = "";
    public string Codigo { get; set; } = "";
    public string Materia { get; set; } = "";
    public string Curso { get; set; } = "";
    public string Titulo { get; set; } = "";
    public decimal HorasCredito { get; set; }
    public string Campus { get; set; } = "";
    public string Nivel { get; set; } = "";
}

/// <summary>
/// Una materia que la persona registró a mano (sin Banner): el código del pénsum, el período en que la cursó o la cursa y la calificación
/// (vacía = la está cursando). No la toca la sincronización con Banner; se mezcla con el histórico al leerlo
/// (ver <c>HistoricoManual</c>).
/// </summary>
public class MateriaManual
{
    public int Id { get; set; }
    public string Codigo { get; set; } = "";
    /// <summary>Nombre del período como lo escribe la universidad: «ENE-ABR 2026».</summary>
    public string Periodo { get; set; } = "";
    /// <summary>La letra de la escala de la universidad; vacía si todavía la está cursando.</summary>
    public string Calificacion { get; set; } = "";
    public DateTime Creada { get; set; }
}

/// <summary>Materia del pénsum. Se carga desde el CSV (Fase 4); no la toca la sincronización con Banner.</summary>
public class MateriaPensum
{
    public int Id { get; set; }
    public string Codigo { get; set; } = "";
    public string Nombre { get; set; } = "";
    public int Creditos { get; set; }
    public int Cuatrimestre { get; set; }
    /// <summary>Texto original del CSV: códigos separados por ";" y/o reglas de porcentaje.</summary>
    public string? Prerrequisitos { get; set; }
    public bool EsElectiva { get; set; }
}

/// <summary>
/// Una materia de Banner que equivale a una materia del pénsum (ING701 → ING716 y ING701 → ING717 son dos filas).
/// <c>CodigoPensum</c> nulo significa "sin equivalente". Editable desde la app (Fase 4).
/// </summary>
public class Equivalencia
{
    public int Id { get; set; }
    public string CodigoBanner { get; set; } = "";
    public string? CodigoPensum { get; set; }
    public string? Nota { get; set; }
}

/// <summary>Configuración del planificador (una sola fila). Ver <see cref="Planificacion.ConfigPlanificador"/>.</summary>
public class ConfiguracionPlanificador
{
    public int Id { get; set; }
    public int LimiteBase { get; set; } = 25;
    public int LimiteAlto { get; set; } = 27;
    public decimal UmbralIndice { get; set; } = 3.40m;
    public int Minimo { get; set; }
    public bool AsumirEnCurso { get; set; } = true;

    public Planificacion.ConfigPlanificador ToConfig() => new(LimiteBase, LimiteAlto, UmbralIndice, Minimo, AsumirEnCurso);

    public void Aplicar(Planificacion.ConfigPlanificador c)
    {
        LimiteBase = c.LimiteBase; LimiteAlto = c.LimiteAlto; UmbralIndice = c.UmbralIndice; Minimo = c.Minimo; AsumirEnCurso = c.AsumirEnCurso;
    }
}

/// <summary>Un escenario de planificación con nombre (p. ej. "Carga normal", "Carga pesada").</summary>
public class PlanEstudio
{
    public int Id { get; set; }
    public string Nombre { get; set; } = "";
    public DateTime Creado { get; set; }
    public DateTime Actualizado { get; set; }
    /// <summary>El escenario que la persona decidió seguir («Plan activo»). Solo uno a la vez; ninguno mientras no elija.</summary>
    public bool Activo { get; set; }
    /// <summary>
    /// Cómo estaba la situación académica la última vez que se miró este plan (JSON de <c>InstantaneaPlan</c>): sirve para contar qué cambió
    /// después de sincronizar. Vacío mientras no se haya mirado.
    /// </summary>
    public string? Instantanea { get; set; }
    public List<PeriodoPlanificado> Periodos { get; set; } = new();
}

/// <summary>Algo que cambió en un plan por sí solo (tras sincronizar) y que la persona debe saber. Se muestra hasta que la persona lo descarta.</summary>
public class AvisoPlan
{
    public int Id { get; set; }
    public DateTime Fecha { get; set; }
    public string PlanNombre { get; set; } = "";
    public string Texto { get; set; } = "";
    public bool Leido { get; set; }
}

/// <summary>
/// La calificación que la persona espera sacar en una materia, para el simulador de índice. Es una preferencia por materia (sirve en
/// cualquier escenario) y no cuenta como calificación real: nada más la lee el simulador.
/// </summary>
public class NotaEsperada
{
    public int Id { get; set; }
    public string Codigo { get; set; } = "";
    public string Letra { get; set; } = "";
}

public class PeriodoPlanificado
{
    public int Id { get; set; }
    public int PlanEstudioId { get; set; }
    /// <summary>Ej. "ENE-ABR 2027".</summary>
    public string Nombre { get; set; } = "";
    public List<MateriaPlanificada> Materias { get; set; } = new();
}

public class MateriaPlanificada
{
    public int Id { get; set; }
    public int PeriodoPlanificadoId { get; set; }
    public string Codigo { get; set; } = "";
    /// <summary>Línea corta de por qué va en ese período (solo la llena el plan sugerido; se borra si la mueves a mano).</summary>
    public string? Razon { get; set; }
}

/// <summary>
/// Una sección publicada por Banner en un período, guardada tal como se consultó. Del profesor solo se guarda el nombre
/// (que Banner publica): nunca su correo ni su matrícula.
/// </summary>
public class SeccionOfertada
{
    public int Id { get; set; }
    /// <summary>Código de período de Banner (202630).</summary>
    public string Periodo { get; set; } = "";
    public string Nrc { get; set; } = "";
    /// <summary>Código como en el pénsum (ISO625).</summary>
    public string Codigo { get; set; } = "";
    public string Titulo { get; set; } = "";
    public string Seccion { get; set; } = "";
    public decimal Creditos { get; set; }
    public string Campus { get; set; } = "";
    public string Metodo { get; set; } = "";
    /// <summary>Nombres separados por «; » (casi siempre uno). Vacío si Banner aún no asigna profesor.</summary>
    public string Profesor { get; set; } = "";
    public int CupoMaximo { get; set; }
    public int Inscritos { get; set; }
    public int CuposDisponibles { get; set; }
    public bool Abierta { get; set; }
    /// <summary>Bloques de horario en JSON (días, horas, aula, fechas) para poder rearmar la sección sin volver a Banner.</summary>
    public string BloquesJson { get; set; } = "[]";
    /// <summary>Cuándo se consultó (UTC).</summary>
    public DateTime Consultada { get; set; }
}

/// <summary>
/// Registro de que se consultó una materia en un período, aunque no hubiera secciones. Permite distinguir «no he consultado»
/// de «consulté y no hay secciones publicadas por ahora».
/// </summary>
public class ConsultaSecciones
{
    public int Id { get; set; }
    public string Periodo { get; set; } = "";
    /// <summary>Código consultado (ISO625, o DEP para todo el deporte).</summary>
    public string Codigo { get; set; } = "";
    public int Secciones { get; set; }
    public DateTime Fecha { get; set; }
}

public enum ResultadoSincronizacion { Exito = 1, Error = 2 }

public class Sincronizacion
{
    public int Id { get; set; }
    /// <summary>Fecha y hora en UTC.</summary>
    public DateTime Fecha { get; set; }
    public ResultadoSincronizacion Resultado { get; set; }
    public string Mensaje { get; set; } = "";
}
