using System.Text.RegularExpressions;

namespace HistorialAcademico.Core.Pensum;

/// <summary>Un requisito es o bien una materia (por código) o bien un porcentaje de créditos aprobados.</summary>
public record Requisito(string? Materia, int? Porcentaje)
{
    public static Requisito DeMateria(string codigo) => new(codigo, null);
    public static Requisito DePorcentaje(int porcentaje) => new(null, porcentaje);
}

/// <summary>
/// Lee el campo "prerrequisitos" del pénsum: códigos y/o reglas de porcentaje separados por ";"
/// (p. ej. "E077; 67% créditos aprobados", "SOC253; 90% créditos aprobados", "59% créditos aprobados").
/// </summary>
public static class PrerrequisitoParser
{
    private static readonly Regex Porcentaje = new(@"^(?<n>\d{1,3})\s*%", RegexOptions.Compiled);
    private static readonly Regex Codigo = new(@"^[A-Z]+\d*$", RegexOptions.Compiled);

    public static bool TryParse(string? texto, out List<Requisito> requisitos, out string? error)
    {
        requisitos = new();
        error = null;
        if (string.IsNullOrWhiteSpace(texto)) return true;

        foreach (var crudo in texto.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pct = Porcentaje.Match(crudo);
            if (pct.Success)
            {
                var n = int.Parse(pct.Groups["n"].Value);
                if (n is < 1 or > 100) { error = $"Porcentaje fuera de rango: \"{crudo}\"."; return false; }
                requisitos.Add(Requisito.DePorcentaje(n));
                continue;
            }

            var codigo = crudo.ToUpperInvariant();
            if (!Codigo.IsMatch(codigo)) { error = $"Prerrequisito no reconocido: \"{crudo}\"."; return false; }
            requisitos.Add(Requisito.DeMateria(codigo));
        }
        return true;
    }

    public static List<Requisito> Parse(string? texto) =>
        TryParse(texto, out var r, out var error) ? r : throw new FormatException(error);
}
