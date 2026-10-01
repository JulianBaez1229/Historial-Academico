using HistorialAcademico.Core.Entities;

namespace HistorialAcademico.Core.Pensum;

/// <summary>Junta las equivalencias que declara el pénsum con las personales de la persona.</summary>
public static class EquivalenciasEfectivas
{
    /// <summary>
    /// Las declaradas por el pénsum y las personales, sin repetir (mismo código de Banner y mismo destino, sin importar mayúsculas).
    /// Si una equivalencia está en las dos, se queda la personal (es la que se puede editar).
    /// </summary>
    public static List<Equivalencia> Unir(IEnumerable<Equivalencia> declaradas, IEnumerable<Equivalencia> personales)
    {
        var resultado = personales.ToList();
        var vistas = resultado.Select(Clave).ToHashSet();
        foreach (var d in declaradas)
            if (vistas.Add(Clave(d))) resultado.Add(d);
        return resultado;
    }

    /// <summary>La misma equivalencia (origen y destino) en una lista de personales: la declarada ya la cubre.</summary>
    public static bool EstaEn(Equivalencia e, IEnumerable<Equivalencia> lista) => lista.Any(x => Clave(x) == Clave(e));

    private static (string, string?) Clave(Equivalencia e) => (e.CodigoBanner.Trim().ToUpperInvariant(), e.CodigoPensum?.Trim().ToUpperInvariant());
}
