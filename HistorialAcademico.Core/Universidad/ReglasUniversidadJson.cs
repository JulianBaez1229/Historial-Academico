using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HistorialAcademico.Core.Universidad;

public record ResultadoReglas(ReglasUniversidad? Reglas, List<string> Errores, List<string> Advertencias)
{
    public bool EsValido => Reglas is not null && Errores.Count == 0;
}

/// <summary>
/// Lee, valida y escribe pensums/&lt;universidad&gt;/universidad.json (esquema en pensums/universidad.schema.json).
/// Igual que con los pénsums, los mensajes van en español y dicen dónde está el problema.
/// </summary>
public static class ReglasUniversidadJson
{
    public static readonly string[] PropiedadesRaiz = { "formato", "id", "nombre", "urlBanner", "escalaCalificaciones", "periodos", "limitesCreditos" };
    public static readonly string[] RequeridasRaiz = { "formato", "id", "nombre", "escalaCalificaciones", "periodos", "limitesCreditos" };
    public static readonly string[] PropiedadesLetra = { "letra", "puntos", "aprueba", "cuentaParaIndice", "nota" };
    public static readonly string[] RequeridasLetra = { "letra", "aprueba", "cuentaParaIndice" };
    public static readonly string[] PropiedadesPeriodo = { "nombre", "codigoBanner" };
    public static readonly string[] PropiedadesLimites = { "base", "alto", "umbralIndice" };

    public const int FormatoActual = 1;
    public const int MaxPeriodosPorAnio = 12;
    public const int MaxCreditosPorPeriodo = 60;
    public const int MaxPuntos = 10;

    private static readonly Regex Identificador = new("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.Compiled);
    private static readonly Regex Letra = new("^[A-Z]{1,2}[+-]?$", RegexOptions.Compiled);
    private static readonly Regex NombrePeriodo = new("^[A-Z0-9]+(-[A-Z0-9]+)*$", RegexOptions.Compiled);
    private static readonly Regex CodigoBanner = new("^[0-9]{2}$", RegexOptions.Compiled);

    private static readonly JsonSerializerOptions Escritura = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <param name="rutaArchivo">Si se da, comprueba que el archivo esté en la carpeta de la universidad y se llame universidad.json.</param>
    public static ResultadoReglas Leer(string json, string? rutaArchivo = null)
    {
        var errores = new List<string>();
        var advertencias = new List<string>();

        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException ex) { return new(null, new() { $"El archivo no es un JSON válido: {ex.Message}" }, advertencias); }

        using (doc)
        {
            var raiz = doc.RootElement;
            if (raiz.ValueKind != JsonValueKind.Object) return new(null, new() { "El archivo debe ser un objeto JSON." }, advertencias);

            Desconocidas(raiz, PropiedadesRaiz, "", errores);
            foreach (var r in RequeridasRaiz.Where(r => !raiz.TryGetProperty(r, out _))) errores.Add($"Falta la propiedad «{r}».");

            var formato = Entero(raiz, "formato", "", errores, 1, FormatoActual);
            var id = Texto(raiz, "id", "", errores, Identificador, "minúsculas, números y guiones (ej. unapec)");
            var nombre = Texto(raiz, "nombre", "", errores);
            var url = raiz.TryGetProperty("urlBanner", out _) ? Texto(raiz, "urlBanner", "", errores, new Regex("^https://[^\\s]+$"), "debe empezar con https://") : null;

            var letras = LeerLetras(raiz, errores);
            var periodos = LeerPeriodos(raiz, errores);
            var limites = LeerLimites(raiz, letras, errores);

            if (rutaArchivo is not null && id is not null)
            {
                var carpeta = Path.GetFileName(Path.GetDirectoryName(Path.GetFullPath(rutaArchivo)));
                if (!string.Equals(carpeta, id, StringComparison.Ordinal)) errores.Add($"El archivo está en la carpeta «{carpeta}», pero el «id» es «{id}».");
                if (!string.Equals(Path.GetFileName(rutaArchivo), "universidad.json", StringComparison.Ordinal))
                    errores.Add($"El archivo se debe llamar «universidad.json» (se llama «{Path.GetFileName(rutaArchivo)}»).");
            }

            if (errores.Count > 0 || formato is null || id is null || nombre is null || limites is null) return new(null, errores, advertencias);
            return new(new ReglasUniversidad(id, nombre, url, new EscalaCalificaciones(letras), new SecuenciaPeriodos(periodos), limites), errores, advertencias);
        }
    }

    private static List<LetraCalificacion> LeerLetras(JsonElement raiz, List<string> errores)
    {
        var letras = new List<LetraCalificacion>();
        if (!Arreglo(raiz, "escalaCalificaciones", "", errores, out var lista)) return letras;
        if (lista.GetArrayLength() == 0) errores.Add("«escalaCalificaciones» no puede estar vacío.");

        var i = 0;
        foreach (var e in lista.EnumerateArray())
        {
            var ruta = $"escalaCalificaciones[{i++}]";
            if (e.ValueKind != JsonValueKind.Object) { errores.Add($"{ruta}: debe ser un objeto."); continue; }
            Desconocidas(e, PropiedadesLetra, ruta, errores);
            foreach (var r in RequeridasLetra.Where(r => !e.TryGetProperty(r, out _))) errores.Add($"{ruta}: falta «{r}».");

            var letra = Texto(e, "letra", ruta, errores, Letra, "una o dos letras mayúsculas, con + o - opcional (ej. A, B+)");
            var aprueba = Booleano(e, "aprueba", ruta, errores);
            var cuenta = Booleano(e, "cuentaParaIndice", ruta, errores);
            var tienePuntos = e.TryGetProperty("puntos", out var pv) && pv.ValueKind != JsonValueKind.Null;
            var puntos = tienePuntos ? Entero(e, "puntos", ruta, errores, 0, MaxPuntos) : null;
            var nota = e.TryGetProperty("nota", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;

            if (cuenta && !tienePuntos) errores.Add($"{ruta}: la letra {letra} cuenta para el índice, así que necesita «puntos».");
            if (!cuenta && tienePuntos) errores.Add($"{ruta}: la letra {letra} no cuenta para el índice, así que no debe tener «puntos».");
            if (letra is not null) letras.Add(new LetraCalificacion(letra, puntos, aprueba, cuenta, nota));
        }

        foreach (var repetida in letras.GroupBy(l => l.Letra, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            errores.Add($"escalaCalificaciones: la letra {repetida.Key} está repetida.");
        if (letras.Count > 0 && !letras.Any(l => l.Aprueba)) errores.Add("escalaCalificaciones: al menos una letra debe aprobar.");
        if (letras.Count > 0 && !letras.Any(l => l.CuentaParaIndice)) errores.Add("escalaCalificaciones: al menos una letra debe contar para el índice.");
        return letras;
    }

    private static List<PeriodoDef> LeerPeriodos(JsonElement raiz, List<string> errores)
    {
        var periodos = new List<PeriodoDef>();
        if (!Arreglo(raiz, "periodos", "", errores, out var lista)) return periodos;
        if (lista.GetArrayLength() is 0 or > MaxPeriodosPorAnio) errores.Add($"«periodos» debe tener entre 1 y {MaxPeriodosPorAnio} períodos por año.");

        var i = 0;
        foreach (var e in lista.EnumerateArray())
        {
            var ruta = $"periodos[{i++}]";
            if (e.ValueKind != JsonValueKind.Object) { errores.Add($"{ruta}: debe ser un objeto."); continue; }
            Desconocidas(e, PropiedadesPeriodo, ruta, errores);
            if (!e.TryGetProperty("nombre", out _)) errores.Add($"{ruta}: falta «nombre».");
            var nombre = Texto(e, "nombre", ruta, errores, NombrePeriodo, "mayúsculas, números y guiones, sin espacios (ej. ENE-ABR)");
            var codigo = e.TryGetProperty("codigoBanner", out _) ? Texto(e, "codigoBanner", ruta, errores, CodigoBanner, "dos dígitos (ej. 10)") : null;
            if (nombre is not null) periodos.Add(new PeriodoDef(nombre, codigo));
        }

        foreach (var repetido in periodos.GroupBy(p => p.Nombre, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            errores.Add($"periodos: «{repetido.Key}» está repetido.");
        var codigos = periodos.Where(p => p.CodigoBanner is not null).Select(p => p.CodigoBanner!).ToList();
        if (codigos.Count > 0 && codigos.Count != periodos.Count) errores.Add("periodos: si uno tiene «codigoBanner», todos deben tenerlo.");
        foreach (var repetido in codigos.GroupBy(c => c).Where(g => g.Count() > 1)) errores.Add($"periodos: el código de Banner {repetido.Key} está repetido.");
        return periodos;
    }

    private static LimitesCreditos? LeerLimites(JsonElement raiz, List<LetraCalificacion> letras, List<string> errores)
    {
        if (!raiz.TryGetProperty("limitesCreditos", out var l)) return null;
        if (l.ValueKind != JsonValueKind.Object) { errores.Add("«limitesCreditos» debe ser un objeto."); return null; }
        Desconocidas(l, PropiedadesLimites, "limitesCreditos", errores);
        foreach (var r in PropiedadesLimites.Where(r => !l.TryGetProperty(r, out _))) errores.Add($"limitesCreditos: falta «{r}».");

        var b = Entero(l, "base", "limitesCreditos", errores, 1, MaxCreditosPorPeriodo);
        var a = Entero(l, "alto", "limitesCreditos", errores, 1, MaxCreditosPorPeriodo);
        decimal? umbral = null;
        if (l.TryGetProperty("umbralIndice", out var u))
        {
            var maximo = letras.Where(x => x.CuentaParaIndice).Select(x => x.Puntos ?? 0).DefaultIfEmpty(MaxPuntos).Max();
            if (u.ValueKind != JsonValueKind.Number || !u.TryGetDecimal(out var d)) errores.Add("limitesCreditos.umbralIndice debe ser un número.");
            else if (d < 0 || d > maximo) errores.Add($"limitesCreditos.umbralIndice debe estar entre 0 y {maximo} (el máximo de la escala); es {d}.");
            else umbral = d;
        }
        if (b is not null && a is not null && a < b) errores.Add($"limitesCreditos: «alto» ({a}) no puede ser menor que «base» ({b}).");
        return b is not null && a is not null && umbral is not null ? new LimitesCreditos(b.Value, a.Value, umbral.Value) : null;
    }

    // ── Escribir ──────────────────────────────────────────────────────────────────────────

    public static string Escribir(ReglasUniversidad r)
    {
        var raiz = new Dictionary<string, object?> { ["formato"] = FormatoActual, ["id"] = r.Id, ["nombre"] = r.Nombre };
        if (r.UrlBanner is not null) raiz["urlBanner"] = r.UrlBanner;
        raiz["escalaCalificaciones"] = r.Escala.Letras.Select(l =>
        {
            var d = new Dictionary<string, object?> { ["letra"] = l.Letra };
            if (l.Puntos is not null) d["puntos"] = l.Puntos;
            d["aprueba"] = l.Aprueba;
            d["cuentaParaIndice"] = l.CuentaParaIndice;
            if (l.Nota is not null) d["nota"] = l.Nota;
            return d;
        }).ToList();
        raiz["periodos"] = r.Periodos.Periodos.Select(p =>
        {
            var d = new Dictionary<string, object?> { ["nombre"] = p.Nombre };
            if (p.CodigoBanner is not null) d["codigoBanner"] = p.CodigoBanner;
            return d;
        }).ToList();
        raiz["limitesCreditos"] = new Dictionary<string, object?> { ["base"] = r.Limites.Base, ["alto"] = r.Limites.Alto, ["umbralIndice"] = r.Limites.UmbralIndice };
        return JsonSerializer.Serialize(raiz, Escritura).Replace("\r\n", "\n") + "\n";
    }

    // ── Lectura de propiedades ────────────────────────────────────────────────────────────

    private static string Donde(string ruta, string propiedad) => ruta.Length == 0 ? $"«{propiedad}»" : $"{ruta}.{propiedad}";

    private static void Desconocidas(JsonElement objeto, string[] conocidas, string ruta, List<string> errores)
    {
        foreach (var p in objeto.EnumerateObject().Where(p => !conocidas.Contains(p.Name)))
            errores.Add(ruta.Length == 0 ? $"Propiedad desconocida «{p.Name}»." : $"{ruta}: propiedad desconocida «{p.Name}».");
    }

    private static string? Texto(JsonElement o, string propiedad, string ruta, List<string> errores, Regex? formato = null, string? descripcion = null)
    {
        if (!o.TryGetProperty(propiedad, out var v)) return null;
        if (v.ValueKind != JsonValueKind.String) { errores.Add($"{Donde(ruta, propiedad)} debe ser un texto."); return null; }
        var texto = v.GetString()!.Trim();
        if (texto.Length == 0) { errores.Add($"{Donde(ruta, propiedad)} no puede estar vacío."); return null; }
        if (formato is not null && !formato.IsMatch(texto)) { errores.Add($"{Donde(ruta, propiedad)} «{texto}» no es válido: {descripcion}."); return null; }
        return texto;
    }

    private static int? Entero(JsonElement o, string propiedad, string ruta, List<string> errores, int minimo, int maximo)
    {
        if (!o.TryGetProperty(propiedad, out var v)) return null;
        if (v.ValueKind != JsonValueKind.Number || !v.TryGetInt32(out var n)) { errores.Add($"{Donde(ruta, propiedad)} debe ser un número entero."); return null; }
        if (n < minimo || n > maximo) { errores.Add($"{Donde(ruta, propiedad)} debe estar entre {minimo} y {maximo} (es {n})."); return null; }
        return n;
    }

    private static bool Booleano(JsonElement o, string propiedad, string ruta, List<string> errores)
    {
        if (!o.TryGetProperty(propiedad, out var v)) return false;
        if (v.ValueKind == JsonValueKind.True) return true;
        if (v.ValueKind == JsonValueKind.False) return false;
        errores.Add($"{Donde(ruta, propiedad)} debe ser true o false.");
        return false;
    }

    private static bool Arreglo(JsonElement o, string propiedad, string ruta, List<string> errores, out JsonElement arreglo)
    {
        arreglo = default;
        if (!o.TryGetProperty(propiedad, out var v)) return false;
        if (v.ValueKind != JsonValueKind.Array) { errores.Add($"{Donde(ruta, propiedad)} debe ser una lista."); return false; }
        arreglo = v;
        return true;
    }
}
