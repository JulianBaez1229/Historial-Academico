using System.Text.RegularExpressions;
using HistorialAcademico.Core.Pensum;
using HistorialAcademico.Core.Planificacion;
using HistorialAcademico.Core.Universidad;

namespace HistorialAcademico.Core.Horarios;

/// <summary>Una consulta concreta a Banner: materia (prefijo) y, si aplica, número de curso.</summary>
public record ConsultaBanner(string Materia, string? Curso)
{
    public string Codigo => Materia + (Curso ?? "");
}

/// <summary>Cómo se traducen los períodos y los códigos del pénsum a los de Banner.</summary>
public static class MapeoBanner
{
    private static readonly Regex CodigoRx = new(@"^(?<materia>[A-Z]+)(?<curso>\d{3})$", RegexOptions.Compiled);

    // ── Períodos ──────────────────────────────────────────────────────────────────────────

    /// <summary>ENE-ABR 2027 → 202710, MAY-AGO → 20, SEP-DIC → 30 (Grado). Posgrado usa 15/25/35 y Educación Continuada 70.</summary>
    public static string CodigoDePeriodo(PeriodoAcademico p) =>
        $"{p.Anio}{p.Sec.Periodos[p.Termino].CodigoBanner ?? throw new InvalidOperationException($"El período {p.Sec.Nombre(p.Termino)} no tiene código de Banner en las reglas de la universidad.")}";

    /// <summary>202630 → SEP-DIC 2026. Devuelve false para Posgrado, Educación Continuada o códigos que no entiende.</summary>
    public static bool TryPeriodoDeCodigo(string? codigo, out PeriodoAcademico periodo) => TryPeriodoDeCodigo(codigo, SecuenciaPeriodos.Unapec, out periodo);

    /// <summary>Igual que la anterior, con los códigos de Banner de la secuencia de períodos de la universidad.</summary>
    public static bool TryPeriodoDeCodigo(string? codigo, SecuenciaPeriodos secuencia, out PeriodoAcademico periodo)
    {
        periodo = default;
        if (codigo is null || !Regex.IsMatch(codigo, @"^\d{6}$")) return false;
        var indice = secuencia.Periodos.ToList().FindIndex(p => p.CodigoBanner == codigo[4..]);
        if (indice < 0) return false;
        periodo = new PeriodoAcademico(int.Parse(codigo[..4]), indice, secuencia.Equals(SecuenciaPeriodos.Unapec) ? null : secuencia);
        return true;
    }

    /// <summary>Períodos de Grado anteriores a <paramref name="desde"/>, del más reciente al más antiguo.</summary>
    public static IEnumerable<PeriodoAcademico> Anteriores(PeriodoAcademico desde, int cuantos)
    {
        for (var i = 1; i <= cuantos; i++) yield return desde.Avanzar(-i);
    }

    // ── Materias ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Qué hay que preguntarle a Banner para una materia del pénsum. Casi siempre es una sola consulta (ISO625 → ISO / 625).
    /// Las electivas se consultan por sus opciones (E077 → ADM103, ADM536, ADM540), el deporte por toda la materia DEP,
    /// y el TFG y la pasantía no se consultan por horario.
    /// </summary>
    public static (List<ConsultaBanner> Consultas, string? Motivo) ConsultasDe(string? codigoPensum)
    {
        var codigo = codigoPensum?.Trim().ToUpperInvariant() ?? "";
        if (codigo.Length == 0) return (new(), "No indicaste ninguna materia.");

        if (ElectivasReferencia.Opciones.TryGetValue(codigo, out var opciones))
            return (opciones.Select(o => DeCodigo(o.Codigo)).Where(c => c is not null).Select(c => c!).ToList(), null);

        if (codigo == "ODEP") return (new() { new ConsultaBanner("DEP", null) }, null);   // en Banner el deporte es la materia DEP
        if (codigo == "TFG") return (new(), "El Trabajo Final de Grado no se consulta por horario.");
        if (codigo.StartsWith("PAS")) return (new(), "La pasantía no tiene secciones con horario.");

        var consulta = DeCodigo(codigo);
        return consulta is null ? (new(), $"No reconozco el código «{codigoPensum}».") : (new() { consulta }, null);
    }

    private static ConsultaBanner? DeCodigo(string codigo)
    {
        var m = CodigoRx.Match(codigo);
        return m.Success ? new ConsultaBanner(m.Groups["materia"].Value, m.Groups["curso"].Value) : null;
    }
}
