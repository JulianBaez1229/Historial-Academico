using HistorialAcademico.Core.Pensum;

namespace HistorialAcademico.Core.Universidad;

/// <summary>Un archivo universidad.json y lo que salió de leerlo.</summary>
public record EntradaUniversidad(string Ruta, ResultadoReglas Resultado)
{
    public bool EsValida => Resultado.EsValido;
}

/// <summary>Las universidades y los pénsums de la carpeta pensums/, ya validados y relacionados entre sí.</summary>
public record CatalogoCompleto(List<EntradaUniversidad> Universidades, List<EntradaCatalogo> Pensums)
{
    public IEnumerable<ReglasUniversidad> UniversidadesValidas => Universidades.Where(u => u.EsValida).Select(u => u.Resultado.Reglas!);
    public IEnumerable<PensumDefinicion> PensumsValidos => Pensums.Where(p => p.EsValida).Select(p => p.Resultado.Definicion!);

    public ReglasUniversidad? ReglasDe(string universidad) =>
        UniversidadesValidas.FirstOrDefault(u => string.Equals(u.Id, universidad, StringComparison.Ordinal));

    /// <summary>Todos los errores de la carpeta, con el archivo al que pertenecen (para mostrar qué falla en vez de esconderlo).</summary>
    public IEnumerable<string> Problemas =>
        Universidades.Where(u => !u.EsValida).SelectMany(u => u.Resultado.Errores.Select(e => $"{Path.GetFileName(Path.GetDirectoryName(u.Ruta))}/universidad.json: {e}"))
            .Concat(Pensums.Where(p => !p.EsValida).SelectMany(p => p.Resultado.Errores.Select(e => $"{Path.GetFileName(Path.GetDirectoryName(p.Ruta))}/{Path.GetFileName(p.Ruta)}: {e}")));
}

public static class CatalogoUniversidades
{
    /// <summary>
    /// Lee pensums/&lt;universidad&gt;/universidad.json y todos los pénsums. Un pénsum cuya universidad no tiene un
    /// universidad.json válido no se puede usar (no se sabría con qué reglas calcular su índice ni sus períodos) y sale con ese error.
    /// </summary>
    public static CatalogoCompleto Leer(string carpeta)
    {
        var universidades = new List<EntradaUniversidad>();
        if (Directory.Exists(carpeta))
            foreach (var archivo in Directory.EnumerateFiles(carpeta, "universidad.json", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
                universidades.Add(new EntradaUniversidad(archivo, ReglasUniversidadJson.Leer(File.ReadAllText(archivo), archivo)));

        var validas = universidades.Where(u => u.EsValida).Select(u => u.Resultado.Reglas!.Id).ToHashSet(StringComparer.Ordinal);
        var pensums = CatalogoPensums.Leer(carpeta).Select(p =>
            p.Resultado.Definicion is { } d && !validas.Contains(d.Universidad)
                ? p with { Resultado = new ResultadoPensumJson(null, new() { $"La universidad «{d.Universidad}» no tiene un universidad.json válido en pensums/{d.Universidad}/." }, p.Resultado.Advertencias) }
                : p).ToList();
        return new CatalogoCompleto(universidades, pensums);
    }
}
