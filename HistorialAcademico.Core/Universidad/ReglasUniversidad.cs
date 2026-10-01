using HistorialAcademico.Core.Planificacion;

namespace HistorialAcademico.Core.Universidad;

/// <summary>Una letra de la escala de calificaciones de la universidad.</summary>
/// <param name="Puntos">Puntos de calidad; solo tiene valor si la letra cuenta para el índice.</param>
/// <param name="Aprueba">La materia queda aprobada con esta letra.</param>
/// <param name="CuentaParaIndice">Entra en el cálculo del índice (una F sí: baja el promedio; una E de exenta no).</param>
public record LetraCalificacion(string Letra, int? Puntos, bool Aprueba, bool CuentaParaIndice, string? Nota = null);

/// <summary>
/// La escala de calificaciones de una universidad. Sustituye a las reglas que antes estaban fijas en el código:
/// UNAPEC usa A=4, B=3, C=2, D=1, F=0 (sin + ni −) y la E (exenta) cuenta como aprobada pero no entra en el índice.
/// </summary>
public sealed record EscalaCalificaciones(IReadOnlyList<LetraCalificacion> Letras)
{
    public static readonly EscalaCalificaciones Unapec = new(new[]
    {
        new LetraCalificacion("A", 4, true, true),
        new LetraCalificacion("B", 3, true, true),
        new LetraCalificacion("C", 2, true, true),
        new LetraCalificacion("D", 1, true, true),
        new LetraCalificacion("F", 0, false, true),
        new LetraCalificacion("E", null, true, false, "Exenta: cuenta como aprobada, pero no entra en el índice."),
    });

    public LetraCalificacion? Buscar(string? letra)
    {
        var l = letra?.Trim();
        return string.IsNullOrEmpty(l) ? null : Letras.FirstOrDefault(x => string.Equals(x.Letra, l, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Puntos de la letra, o null si no cuenta para el índice.</summary>
    public int? PuntosPorLetra(string? letra) => Buscar(letra) is { CuentaParaIndice: true } l ? l.Puntos : null;

    public bool CuentaParaIndice(string? letra) => Buscar(letra)?.CuentaParaIndice == true;

    /// <summary>Aprobada sin ser exenta: aprueba y además entra en el índice.</summary>
    public bool EsAprobada(string? letra) => Buscar(letra) is { Aprueba: true, CuentaParaIndice: true };

    /// <summary>Exenta: aprueba pero no entra en el índice (la E de UNAPEC).</summary>
    public bool EsExenta(string? letra) => Buscar(letra) is { Aprueba: true, CuentaParaIndice: false };

    /// <summary>Cuenta como créditos aprobados: aprueba, sea normal o exenta.</summary>
    public bool CuentaComoAprobada(string? letra) => Buscar(letra)?.Aprueba == true;

    /// <summary>Los puntos más altos de la escala (el índice no puede pasar de ahí).</summary>
    public int PuntosMaximos => Letras.Where(l => l.CuentaParaIndice).Select(l => l.Puntos ?? 0).DefaultIfEmpty(0).Max();

    public decimal Redondear(decimal indice) => Math.Round(indice, 2, MidpointRounding.AwayFromZero);

    // Un record con una lista compara la lista por referencia; se compara por contenido.
    public bool Equals(EscalaCalificaciones? otra) => otra is not null && Letras.SequenceEqual(otra.Letras);
    public override int GetHashCode() => Letras.Aggregate(17, (h, l) => HashCode.Combine(h, l));
}

/// <summary>Un período del año académico: su nombre (ENE-ABR) y, si la universidad usa Banner, su código (10).</summary>
public record PeriodoDef(string Nombre, string? CodigoBanner = null);

/// <summary>
/// El orden de los períodos de un año académico. UNAPEC: ENE-ABR, MAY-AGO, SEP-DIC (tres al año);
/// otra universidad podría tener dos semestres o cuatro trimestres.
/// </summary>
public sealed class SecuenciaPeriodos : IEquatable<SecuenciaPeriodos>
{
    public static readonly SecuenciaPeriodos Unapec = new(new PeriodoDef[] { new("ENE-ABR", "10"), new("MAY-AGO", "20"), new("SEP-DIC", "30") });

    public IReadOnlyList<PeriodoDef> Periodos { get; }

    public SecuenciaPeriodos(IReadOnlyList<PeriodoDef> periodos)
    {
        if (periodos.Count == 0) throw new ArgumentException("La secuencia necesita al menos un período.", nameof(periodos));
        Periodos = periodos;
    }

    /// <summary>Cuántos períodos tiene un año.</summary>
    public int PorAnio => Periodos.Count;

    public string Nombre(int indice) => Periodos[indice].Nombre;

    public int IndiceDe(string nombre) => Periodos.ToList().FindIndex(p => string.Equals(p.Nombre, nombre.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>¿Todos los períodos tienen código de Banner?</summary>
    public bool TieneCodigosBanner => Periodos.All(p => p.CodigoBanner is not null);

    public bool Equals(SecuenciaPeriodos? otra) => otra is not null && Periodos.SequenceEqual(otra.Periodos);
    public override bool Equals(object? obj) => Equals(obj as SecuenciaPeriodos);
    public override int GetHashCode() => Periodos.Aggregate(17, (h, p) => HashCode.Combine(h, p));
    public override string ToString() => string.Join(", ", Periodos.Select(p => p.Nombre));
}

/// <summary>Límites de créditos por período: uno normal y uno mayor para quien supera un índice.</summary>
public record LimitesCreditos(int Base, int Alto, decimal UmbralIndice);

/// <summary>
/// Las reglas propias de una universidad, leídas de pensums/&lt;universidad&gt;/universidad.json: nombre, página de Banner
/// (si aplica), escala de calificaciones, secuencia de períodos y límites de créditos.
/// El índice, el estado de las materias y el planificador las usan en lugar de valores fijos en el código.
/// </summary>
public sealed record ReglasUniversidad(
    string Id, string Nombre, string? UrlBanner, EscalaCalificaciones Escala, SecuenciaPeriodos Periodos, LimitesCreditos Limites)
{
    /// <summary>Lo que se usa si no se puede leer el archivo de la universidad. Debe coincidir con pensums/unapec/universidad.json (una prueba lo vigila).</summary>
    public static readonly ReglasUniversidad Unapec = new(
        "unapec", "UNAPEC – Universidad APEC", "https://alumnos.unapec.edu.do/StudentSelfService/ssb/studentCommonDashboard",
        EscalaCalificaciones.Unapec, SecuenciaPeriodos.Unapec, new LimitesCreditos(25, 27, 3.40m));

    /// <summary>La configuración del planificador con la que se empieza si la persona no ha guardado la suya.</summary>
    public ConfigPlanificador ConfigPorDefecto => new(Limites.Base, Limites.Alto, Limites.UmbralIndice);
}
