namespace HistorialAcademico.Core.Pensum;

/// <summary>Datos que el CSV no trae y hay que indicar al convertirlo.</summary>
public record MetadatosPensum(string Universidad, string Carrera, string NombreCarrera, string Version);

/// <summary>Convierte el CSV de siempre (codigo, nombre, creditos, cuatrimestre, prerrequisitos, es_electiva) al formato estándar.</summary>
public static class PensumConversor
{
    /// <summary>
    /// Devuelve el pénsum ya validado: el resultado sale de escribir el JSON y volver a leerlo, así lo que se entrega es
    /// exactamente lo que se guardará en el archivo. Los errores del CSV o de la validación vienen en el resultado.
    /// </summary>
    public static ResultadoPensumJson DesdeCsv(
        string csv, MetadatosPensum meta,
        IEnumerable<BloqueElectivasDef>? bloques = null, IEnumerable<CertificacionDef>? certificaciones = null, IEnumerable<string>? requisitos = null,
        IEnumerable<EquivalenciaDef>? equivalencias = null)
    {
        var lectura = PensumCsvParser.Parse(csv);
        if (!lectura.EsValido) return new(null, lectura.Errores.Count > 0 ? lectura.Errores : new() { "El CSV no trae materias." }, lectura.Advertencias);

        var materias = lectura.Materias.Select(m => new MateriaDef(
            m.Codigo, m.Nombre, m.Creditos, m.Cuatrimestre,
            (m.Prerrequisitos ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
            m.EsElectiva)).ToList();

        var definicion = new PensumDefinicion(
            PensumDefinicion.FormatoActual, meta.Universidad, meta.Carrera, meta.NombreCarrera, meta.Version,
            materias.Sum(m => m.Creditos), materias.Max(m => m.Cuatrimestre), materias,
            (bloques ?? Enumerable.Empty<BloqueElectivasDef>()).ToList(),
            (certificaciones ?? Enumerable.Empty<CertificacionDef>()).ToList(),
            (requisitos ?? Enumerable.Empty<string>()).ToList(),
            (equivalencias ?? Enumerable.Empty<EquivalenciaDef>()).ToList());

        var comprobado = PensumJson.Leer(PensumJson.Escribir(definicion));
        return comprobado with { Advertencias = lectura.Advertencias.Concat(comprobado.Advertencias).Distinct().ToList() };
    }
}

/// <summary>Un archivo de la carpeta de pénsums y lo que salió de leerlo.</summary>
public record EntradaCatalogo(string Ruta, ResultadoPensumJson Resultado)
{
    public bool EsValida => Resultado.EsValido;
}

/// <summary>Los pénsums que hay en una carpeta (pensums/&lt;universidad&gt;/&lt;carrera&gt;-&lt;versión&gt;.json).</summary>
public static class CatalogoPensums
{
    /// <summary>Archivos que viven en la carpeta pero no son pénsums.</summary>
    private static readonly string[] NoSonPensums = { "schema.json", "universidad.json", "universidad.schema.json" };

    /// <summary>
    /// Lee y valida todos los pénsums. Uno con errores no se descarta en silencio: sale en la lista con sus errores
    /// (así se puede mostrar qué falla en vez de que «desaparezca»). No hace falta buscar claves repetidas: como la carpeta y
    /// el nombre del archivo deben coincidir con universidad, carrera y versión, dos archivos válidos nunca comparten clave.
    /// </summary>
    public static List<EntradaCatalogo> Leer(string carpeta)
    {
        if (!Directory.Exists(carpeta)) return new();
        var entradas = new List<EntradaCatalogo>();
        foreach (var archivo in Directory.EnumerateFiles(carpeta, "*.json", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
        {
            if (NoSonPensums.Contains(Path.GetFileName(archivo), StringComparer.OrdinalIgnoreCase)) continue;
            entradas.Add(new EntradaCatalogo(archivo, PensumJson.Leer(File.ReadAllText(archivo), archivo)));
        }

        return entradas;
    }
}
