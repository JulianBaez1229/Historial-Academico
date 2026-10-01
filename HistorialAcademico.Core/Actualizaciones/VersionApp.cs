using System.Text.RegularExpressions;

namespace HistorialAcademico.Core.Actualizaciones;

/// <summary>
/// Una versión del programa en formato semántico (1.2.3 o 1.2.3-beta.1), tal como se escribe en la etiqueta de un Release (v1.2.3).
/// Una versión de prueba (con guion) es anterior a la estable del mismo número.
/// </summary>
public sealed partial record VersionApp(int Mayor, int Menor, int Parche, string? Prueba = null) : IComparable<VersionApp>
{
    [GeneratedRegex(@"^[vV]?(?<a>\d{1,6})\.(?<b>\d{1,6})\.(?<c>\d{1,6})(?:-(?<p>[0-9A-Za-z]+(?:\.[0-9A-Za-z]+)*))?(?:\+[0-9A-Za-z.-]+)?$")]
    private static partial Regex Formato();

    public static bool TryParse(string? texto, out VersionApp version)
    {
        version = new VersionApp(0, 0, 0);
        if (texto is null || Formato().Match(texto.Trim()) is not { Success: true } m) return false;
        version = new VersionApp(int.Parse(m.Groups["a"].Value), int.Parse(m.Groups["b"].Value), int.Parse(m.Groups["c"].Value),
            m.Groups["p"].Success ? m.Groups["p"].Value : null);
        return true;
    }

    /// <summary>Quien compila desde el código tiene la versión 0.0.0-desarrollo: no se le avisa de nada, porque no es una versión publicada.</summary>
    public bool EsDeDesarrollo => Mayor == 0 && Menor == 0 && Parche == 0;

    public int CompareTo(VersionApp? otra)
    {
        if (otra is null) return 1;
        var c = Mayor.CompareTo(otra.Mayor);
        if (c != 0) return c;
        c = Menor.CompareTo(otra.Menor);
        if (c != 0) return c;
        c = Parche.CompareTo(otra.Parche);
        if (c != 0) return c;
        return (Prueba, otra.Prueba) switch
        {
            (null, null) => 0,
            (null, _) => 1,        // la estable va después de sus versiones de prueba
            (_, null) => -1,
            var (a, b) => ComparaPrueba(a!, b!),
        };
    }

    // Identificadores separados por punto: los números se comparan como números y ganan los que tienen más identificadores (regla de semver).
    private static int ComparaPrueba(string a, string b)
    {
        var x = a.Split('.');
        var y = b.Split('.');
        for (var i = 0; i < Math.Min(x.Length, y.Length); i++)
        {
            var (nx, ny) = (long.TryParse(x[i], out var vx), long.TryParse(y[i], out var vy));
            int c = nx && ny ? vx.CompareTo(vy) : nx ? -1 : ny ? 1 : string.CompareOrdinal(x[i], y[i]);
            if (c != 0) return c;
        }
        return x.Length.CompareTo(y.Length);
    }

    public static bool operator >(VersionApp a, VersionApp b) => a.CompareTo(b) > 0;
    public static bool operator <(VersionApp a, VersionApp b) => a.CompareTo(b) < 0;
    public static bool operator >=(VersionApp a, VersionApp b) => a.CompareTo(b) >= 0;
    public static bool operator <=(VersionApp a, VersionApp b) => a.CompareTo(b) <= 0;

    public override string ToString() => $"{Mayor}.{Menor}.{Parche}{(Prueba is null ? "" : "-" + Prueba)}";

    /// <summary>¿La versión publicada es más nueva que la que se está usando? Falso si alguna no se entiende o si esta es de desarrollo.</summary>
    public static bool HayNueva(string? actual, string? publicada) =>
        TryParse(actual, out var a) && TryParse(publicada, out var p) && !a.EsDeDesarrollo && p > a;
}
