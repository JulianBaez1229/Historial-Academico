using System.Text.RegularExpressions;
using HistorialAcademico.Core.Universidad;

namespace HistorialAcademico.Core.Planificacion;

/// <summary>
/// Un período académico: un año y la posición dentro de la secuencia de la universidad (<see cref="SecuenciaPeriodos"/>).
/// Sin indicar otra, la secuencia es la de UNAPEC: ENE-ABR → MAY-AGO → SEP-DIC → ENE-ABR del año siguiente.
/// Los períodos que salen de este (Siguiente, Avanzar) conservan su secuencia.
/// </summary>
public readonly record struct PeriodoAcademico(int Anio, int Termino, SecuenciaPeriodos? Secuencia = null) : IComparable<PeriodoAcademico>
{
    /// <summary>La secuencia en uso (la de UNAPEC si no se indicó otra).</summary>
    public SecuenciaPeriodos Sec => Secuencia ?? SecuenciaPeriodos.Unapec;

    public string Nombre => $"{Sec.Nombre(Termino)} {Anio}";
    public override string ToString() => Nombre;

    private int Ordinal => Anio * Sec.PorAnio + Termino;

    public PeriodoAcademico Siguiente() => Termino == Sec.PorAnio - 1 ? this with { Anio = Anio + 1, Termino = 0 } : this with { Termino = Termino + 1 };

    /// <summary>Avanza (o retrocede, si es negativo) esa cantidad de períodos.</summary>
    public PeriodoAcademico Avanzar(int periodos)
    {
        var porAnio = Sec.PorAnio;
        var ordinal = Ordinal + periodos;
        var anio = (int)Math.Floor((double)ordinal / porAnio);
        return this with { Anio = anio, Termino = ordinal - anio * porAnio };
    }

    /// <summary>Cuántos períodos hay de este a <paramref name="otro"/> (negativo si otro es anterior).</summary>
    public int Distancia(PeriodoAcademico otro) => otro.Ordinal - Ordinal;

    public int CompareTo(PeriodoAcademico otro) => Ordinal.CompareTo(otro.Ordinal);
    public static bool operator <(PeriodoAcademico a, PeriodoAcademico b) => a.CompareTo(b) < 0;
    public static bool operator >(PeriodoAcademico a, PeriodoAcademico b) => a.CompareTo(b) > 0;
    public static bool operator <=(PeriodoAcademico a, PeriodoAcademico b) => a.CompareTo(b) <= 0;
    public static bool operator >=(PeriodoAcademico a, PeriodoAcademico b) => a.CompareTo(b) >= 0;

    // Dos períodos son iguales si tienen el mismo año, posición y secuencia; indicar la de UNAPEC o no indicar ninguna es lo mismo.
    public bool Equals(PeriodoAcademico otro) => Anio == otro.Anio && Termino == otro.Termino && Sec.Equals(otro.Sec);
    public override int GetHashCode() => HashCode.Combine(Anio, Termino, Sec);

    public static bool TryParse(string? texto, out PeriodoAcademico periodo) => TryParse(texto, SecuenciaPeriodos.Unapec, out periodo);

    /// <summary>Lee «ENE-ABR 2027» con los nombres de la secuencia dada.</summary>
    public static bool TryParse(string? texto, SecuenciaPeriodos secuencia, out PeriodoAcademico periodo)
    {
        periodo = default;
        var m = Regex.Match(texto?.Trim() ?? "", @"^(?<n>[A-Za-z0-9]+(?:-[A-Za-z0-9]+)*)\s+(?<a>\d{4})$");
        if (!m.Success) return false;
        var termino = secuencia.IndiceDe(m.Groups["n"].Value);
        if (termino < 0) return false;
        periodo = new PeriodoAcademico(int.Parse(m.Groups["a"].Value), termino, ReferenciaA(secuencia));
        return true;
    }

    public static PeriodoAcademico Parse(string texto) => Parse(texto, SecuenciaPeriodos.Unapec);

    public static PeriodoAcademico Parse(string texto, SecuenciaPeriodos secuencia) =>
        TryParse(texto, secuencia, out var p) ? p : throw new FormatException($"Período no válido: \"{texto}\".");

    /// <summary>La secuencia de UNAPEC se guarda como «ninguna» para que los períodos de siempre queden idénticos.</summary>
    private static SecuenciaPeriodos? ReferenciaA(SecuenciaPeriodos s) => s.Equals(SecuenciaPeriodos.Unapec) ? null : s;

    /// <summary>
    /// Primer período planificable: el siguiente al último con cursos en progreso. Si no asumes que apruebas
    /// los cursos en progreso, ese mismo período sigue siendo planificable (por si toca repetirlos).
    /// Sin cursos en progreso, el siguiente al último cerrado.
    /// </summary>
    public static PeriodoAcademico? Primero(string? enProgreso, string? ultimoCerrado, bool asumirEnCurso, SecuenciaPeriodos? secuencia = null)
    {
        secuencia ??= SecuenciaPeriodos.Unapec;
        if (TryParse(enProgreso, secuencia, out var actual)) return asumirEnCurso ? actual.Siguiente() : actual;
        if (TryParse(ultimoCerrado, secuencia, out var cerrado)) return cerrado.Siguiente();
        return null;
    }
}
