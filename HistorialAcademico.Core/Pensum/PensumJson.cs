using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HistorialAcademico.Core.Pensum;

/// <summary>
/// Lee, valida y escribe pénsums en el formato estándar. La validación es la misma que describe pensums/schema.json
/// (que sirve para los editores y para la validación en cada Pull Request) más reglas que un esquema no puede expresar:
/// códigos repetidos, prerrequisitos que no existen, ciclos, suma de créditos y nombre del archivo.
/// Todos los mensajes van en español y dicen dónde está el problema.
/// </summary>
public static class PensumJson
{
    public static readonly string[] PropiedadesRaiz =
    {
        "formato", "universidad", "carrera", "nombreCarrera", "version", "totalCreditos", "cuatrimestres",
        "materias", "bloquesElectivas", "certificaciones", "requisitosGraduacion", "equivalencias",
    };
    public static readonly string[] RequeridasRaiz =
        { "formato", "universidad", "carrera", "nombreCarrera", "version", "totalCreditos", "cuatrimestres", "materias" };
    public static readonly string[] PropiedadesMateria = { "codigo", "nombre", "creditos", "cuatrimestre", "prerrequisitos", "electiva" };
    public static readonly string[] RequeridasMateria = { "codigo", "nombre", "creditos", "cuatrimestre" };
    public static readonly string[] PropiedadesBloque = { "codigo", "nombre", "opciones" };
    public static readonly string[] PropiedadesOpcion = { "codigo", "nombre" };
    public static readonly string[] PropiedadesCertificacion = { "nombre", "materias", "reemplazaElectivas", "nota" };
    public static readonly string[] PropiedadesEquivalencia = { "origen", "destino", "nota" };
    public static readonly string[] RequeridasEquivalencia = { "origen", "destino" };

    public const int MaxCuatrimestres = 24;
    public const int MaxCreditosMateria = 20;

    private static readonly Regex Identificador = new("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.Compiled);
    private static readonly Regex Codigo = new("^[A-Z0-9]{2,12}$", RegexOptions.Compiled);
    private static readonly Regex VersionRx = new("^[A-Za-z0-9]+([._-][A-Za-z0-9]+)*$", RegexOptions.Compiled);

    private static readonly JsonSerializerOptions Escritura = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,   // deja las tildes y la ñ tal cual
    };

    // ── Leer y validar ────────────────────────────────────────────────────────────────────

    /// <param name="rutaArchivo">Si se da, también se comprueba que la carpeta y el nombre del archivo correspondan al pénsum.</param>
    public static ResultadoPensumJson Leer(string json, string? rutaArchivo = null)
    {
        var errores = new List<string>();
        var advertencias = new List<string>();

        JsonDocument doc;
        try { doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow }); }
        catch (JsonException ex) { return new(null, new() { $"El archivo no es un JSON válido: {ex.Message}" }, advertencias); }

        using (doc)
        {
            var raiz = doc.RootElement;
            if (raiz.ValueKind != JsonValueKind.Object) return new(null, new() { "El archivo debe ser un objeto JSON." }, advertencias);

            Desconocidas(raiz, PropiedadesRaiz, "", errores);
            foreach (var r in RequeridasRaiz.Where(r => !raiz.TryGetProperty(r, out _))) errores.Add($"Falta la propiedad «{r}».");

            var formato = Entero(raiz, "formato", "", errores, 1, PensumDefinicion.FormatoActual);
            if (formato is not null && formato != PensumDefinicion.FormatoActual)
                errores.Add($"«formato» debe ser {PensumDefinicion.FormatoActual} (es el único formato que esta versión entiende).");
            var universidad = Texto(raiz, "universidad", "", errores, Identificador, "minúsculas, números y guiones (ej. unapec)");
            var carrera = Texto(raiz, "carrera", "", errores, Identificador, "minúsculas, números y guiones (ej. ingenieria-software)");
            var nombreCarrera = Texto(raiz, "nombreCarrera", "", errores);
            var version = Texto(raiz, "version", "", errores, VersionRx, "letras, números y . _ - (ej. 11 o 2019)");
            var total = Entero(raiz, "totalCreditos", "", errores, 0, 1000);
            var cuatrimestres = Entero(raiz, "cuatrimestres", "", errores, 1, MaxCuatrimestres);

            var materias = LeerMaterias(raiz, cuatrimestres, errores);
            var bloques = LeerBloques(raiz, materias, errores);
            var certificaciones = LeerCertificaciones(raiz, errores);
            var requisitos = LeerRequisitos(raiz, errores);
            var equivalencias = LeerEquivalencias(raiz, materias, errores, advertencias);

            ValidarRelaciones(materias, certificaciones, errores, advertencias);

            if (total is not null && materias.Count > 0 && errores.Count == 0)
            {
                var suma = materias.Sum(m => m.Creditos);
                if (suma != total) errores.Add($"«totalCreditos» dice {total}, pero los créditos de las materias suman {suma}.");
            }

            if (rutaArchivo is not null && universidad is not null && carrera is not null && version is not null)
            {
                var carpeta = Path.GetFileName(Path.GetDirectoryName(Path.GetFullPath(rutaArchivo)));
                var archivo = Path.GetFileName(rutaArchivo);
                if (!string.Equals(carpeta, universidad, StringComparison.Ordinal))
                    errores.Add($"El archivo está en la carpeta «{carpeta}», pero la universidad es «{universidad}».");
                if (!string.Equals(archivo, $"{carrera}-{version}.json", StringComparison.Ordinal))
                    errores.Add($"El archivo se debe llamar «{carrera}-{version}.json» (se llama «{archivo}»).");
            }

            if (errores.Count > 0) return new(null, errores, advertencias);
            return new(new PensumDefinicion(formato!.Value, universidad!, carrera!, nombreCarrera!, version!, total!.Value, cuatrimestres!.Value,
                materias, bloques, certificaciones, requisitos, equivalencias), errores, advertencias);
        }
    }

    private static List<MateriaDef> LeerMaterias(JsonElement raiz, int? cuatrimestres, List<string> errores)
    {
        var materias = new List<MateriaDef>();
        if (!Arreglo(raiz, "materias", "", errores, out var lista)) return materias;
        if (lista.GetArrayLength() == 0) errores.Add("«materias» no puede estar vacío.");

        var i = 0;
        foreach (var m in lista.EnumerateArray())
        {
            var ruta = $"materias[{i++}]";
            if (m.ValueKind != JsonValueKind.Object) { errores.Add($"{ruta}: debe ser un objeto."); continue; }
            Desconocidas(m, PropiedadesMateria, ruta, errores);
            foreach (var r in RequeridasMateria.Where(r => !m.TryGetProperty(r, out _))) errores.Add($"{ruta}: falta «{r}».");

            var codigo = Texto(m, "codigo", ruta, errores, Codigo, "letras mayúsculas y números, de 2 a 12 (ej. ISO100)");
            var nombre = Texto(m, "nombre", ruta, errores);
            var creditos = Entero(m, "creditos", ruta, errores, 0, MaxCreditosMateria);
            var cuat = Entero(m, "cuatrimestre", ruta, errores, 1, cuatrimestres is null ? MaxCuatrimestres : Math.Min(cuatrimestres.Value, MaxCuatrimestres));
            var prerrequisitos = ListaTextos(m, "prerrequisitos", ruta, errores);
            var electiva = Booleano(m, "electiva", ruta, errores);

            foreach (var p in prerrequisitos)
                if (!PrerrequisitoParser.TryParse(p, out var reqs, out var err) || reqs.Count != 1)
                    errores.Add($"{ruta} ({codigo}): prerrequisito «{p}» no válido: {err ?? "usa un código (ISO100) o un porcentaje («67% créditos aprobados»), uno por elemento"}");

            if (codigo is not null && nombre is not null && creditos is not null && cuat is not null)
                materias.Add(new MateriaDef(codigo, nombre, creditos.Value, cuat.Value, prerrequisitos, electiva));
        }
        return materias;
    }

    private static List<BloqueElectivasDef> LeerBloques(JsonElement raiz, List<MateriaDef> materias, List<string> errores)
    {
        var bloques = new List<BloqueElectivasDef>();
        if (!raiz.TryGetProperty("bloquesElectivas", out _) || !Arreglo(raiz, "bloquesElectivas", "", errores, out var lista)) return bloques;

        var i = 0;
        foreach (var b in lista.EnumerateArray())
        {
            var ruta = $"bloquesElectivas[{i++}]";
            if (b.ValueKind != JsonValueKind.Object) { errores.Add($"{ruta}: debe ser un objeto."); continue; }
            Desconocidas(b, PropiedadesBloque, ruta, errores);
            var codigo = Texto(b, "codigo", ruta, errores, Codigo, "letras mayúsculas y números");
            var nombre = Texto(b, "nombre", ruta, errores);
            var opciones = new List<OpcionElectivaDef>();
            if (Arreglo(b, "opciones", ruta, errores, out var ops))
            {
                if (ops.GetArrayLength() == 0) errores.Add($"{ruta}: «opciones» no puede estar vacío.");
                var j = 0;
                foreach (var o in ops.EnumerateArray())
                {
                    var rutaOp = $"{ruta}.opciones[{j++}]";
                    if (o.ValueKind != JsonValueKind.Object) { errores.Add($"{rutaOp}: debe ser un objeto."); continue; }
                    Desconocidas(o, PropiedadesOpcion, rutaOp, errores);
                    var oc = Texto(o, "codigo", rutaOp, errores, Codigo, "letras mayúsculas y números");
                    var on = Texto(o, "nombre", rutaOp, errores);
                    if (oc is not null && on is not null) opciones.Add(new OpcionElectivaDef(oc, on));
                }
            }
            if (codigo is not null && nombre is not null)
            {
                var materia = materias.FirstOrDefault(m => m.Codigo == codigo);
                if (materia is null) errores.Add($"{ruta}: la electiva {codigo} no está en «materias».");
                else if (!materia.Electiva) errores.Add($"{ruta}: {codigo} tiene bloque de electivas, pero su materia no está marcada como «electiva»: true.");
                bloques.Add(new BloqueElectivasDef(codigo, nombre, opciones));
            }
        }

        foreach (var repetido in bloques.GroupBy(b => b.Codigo).Where(g => g.Count() > 1))
            errores.Add($"bloquesElectivas: la electiva {repetido.Key} está repetida.");
        return bloques;
    }

    private static List<CertificacionDef> LeerCertificaciones(JsonElement raiz, List<string> errores)
    {
        var certificaciones = new List<CertificacionDef>();
        if (!raiz.TryGetProperty("certificaciones", out _) || !Arreglo(raiz, "certificaciones", "", errores, out var lista)) return certificaciones;

        var i = 0;
        foreach (var c in lista.EnumerateArray())
        {
            var ruta = $"certificaciones[{i++}]";
            if (c.ValueKind != JsonValueKind.Object) { errores.Add($"{ruta}: debe ser un objeto."); continue; }
            Desconocidas(c, PropiedadesCertificacion, ruta, errores);
            var nombre = Texto(c, "nombre", ruta, errores);
            var materias = ListaTextos(c, "materias", ruta, errores);
            if (materias.Count == 0 && c.TryGetProperty("materias", out _) is false) errores.Add($"{ruta}: falta «materias».");
            foreach (var m in materias.Where(m => !Codigo.IsMatch(m))) errores.Add($"{ruta}: «{m}» no es un código de materia válido.");
            var reemplaza = Booleano(c, "reemplazaElectivas", ruta, errores);
            var nota = c.TryGetProperty("nota", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
            if (nombre is not null) certificaciones.Add(new CertificacionDef(nombre, materias, reemplaza, nota));
        }
        return certificaciones;
    }

    private static List<EquivalenciaDef> LeerEquivalencias(JsonElement raiz, List<MateriaDef> materias, List<string> errores, List<string> advertencias)
    {
        var equivalencias = new List<EquivalenciaDef>();
        if (!raiz.TryGetProperty("equivalencias", out _) || !Arreglo(raiz, "equivalencias", "", errores, out var lista)) return equivalencias;

        var codigos = materias.Select(m => m.Codigo).ToHashSet();
        var i = 0;
        foreach (var e in lista.EnumerateArray())
        {
            var ruta = $"equivalencias[{i++}]";
            if (e.ValueKind != JsonValueKind.Object) { errores.Add($"{ruta}: debe ser un objeto."); continue; }
            Desconocidas(e, PropiedadesEquivalencia, ruta, errores);
            foreach (var r in RequeridasEquivalencia.Where(r => !e.TryGetProperty(r, out _))) errores.Add($"{ruta}: falta «{r}».");

            var origen = Texto(e, "origen", ruta, errores, Codigo, "el código de la materia del plan anterior, tal como aparece en Banner (ej. ING701)");
            var destino = ListaTextos(e, "destino", ruta, errores);
            var nota = e.TryGetProperty("nota", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;

            foreach (var d in destino)
            {
                if (!Codigo.IsMatch(d)) errores.Add($"{ruta}: «{d}» no es un código de materia válido.");
                else if (!codigos.Contains(d)) errores.Add($"{ruta} ({origen}): el destino {d} no existe en el pénsum.");
            }
            foreach (var repetido in destino.GroupBy(d => d).Where(g => g.Count() > 1)) errores.Add($"{ruta}: el destino {repetido.Key} está repetido.");
            if (origen is not null && codigos.Contains(origen))
                advertencias.Add($"{ruta}: {origen} ya es una materia de este pénsum; una equivalencia con ese código como origen no hace falta.");
            if (origen is not null) equivalencias.Add(new EquivalenciaDef(origen, destino, string.IsNullOrWhiteSpace(nota) ? null : nota.Trim()));
        }

        foreach (var repetido in equivalencias.GroupBy(e => e.Origen).Where(g => g.Count() > 1))
            errores.Add($"equivalencias: el origen {repetido.Key} está repetido (junta sus destinos en una sola entrada).");
        return equivalencias;
    }

    private static List<string> LeerRequisitos(JsonElement raiz, List<string> errores) =>
        raiz.TryGetProperty("requisitosGraduacion", out _) ? ListaTextos(raiz, "requisitosGraduacion", "", errores) : new();

    private static void ValidarRelaciones(List<MateriaDef> materias, List<CertificacionDef> certificaciones, List<string> errores, List<string> advertencias)
    {
        foreach (var repetido in materias.GroupBy(m => m.Codigo).Where(g => g.Count() > 1))
            errores.Add($"materias: el código {repetido.Key} está repetido.");

        var codigos = materias.Select(m => m.Codigo).ToHashSet();
        foreach (var m in materias)
            foreach (var p in m.Prerrequisitos)
            {
                var requerida = MateriaRequerida(p);
                if (requerida is null) continue;   // una regla de porcentaje (o un texto mal escrito, ya informado antes)
                if (requerida == m.Codigo) errores.Add($"{m.Codigo}: no puede ser prerrequisito de sí misma.");
                else if (!codigos.Contains(requerida)) errores.Add($"{m.Codigo}: su prerrequisito {requerida} no existe en el pénsum.");
                else if (materias.First(x => x.Codigo == requerida).Cuatrimestre > m.Cuatrimestre)
                    advertencias.Add($"{m.Codigo} (cuatrimestre {m.Cuatrimestre}) tiene como prerrequisito a {requerida}, que está en un cuatrimestre posterior.");
            }

        var ciclo = BuscarCiclo(materias);
        if (ciclo is not null) errores.Add($"Los prerrequisitos forman un ciclo: {string.Join(" → ", ciclo)}.");

        foreach (var c in certificaciones)
            foreach (var m in c.Materias.Where(m => Codigo.IsMatch(m) && codigos.Contains(m)))
                advertencias.Add($"Certificación «{c.Nombre}»: {m} también está en el pénsum como materia normal (¿está bien?).");
    }

    /// <summary>El código de materia que exige un prerrequisito, o null si es una regla de porcentaje o no se entiende.</summary>
    private static string? MateriaRequerida(string prerrequisito) =>
        PrerrequisitoParser.TryParse(prerrequisito, out var reqs, out _) && reqs.Count == 1 ? reqs[0].Materia : null;

    /// <summary>Un ciclo de prerrequisitos (A → B → A), si lo hay: dejaría esas materias bloqueadas para siempre.</summary>
    private static List<string>? BuscarCiclo(List<MateriaDef> materias)
    {
        var por = materias.GroupBy(m => m.Codigo).ToDictionary(g => g.Key, g => g.First());
        var estado = new Dictionary<string, int>();   // 1 = en la pila, 2 = terminada
        var pila = new List<string>();

        List<string>? Visitar(string codigo)
        {
            estado[codigo] = 1;
            pila.Add(codigo);
            foreach (var p in por[codigo].Prerrequisitos)
            {
                var req = MateriaRequerida(p);
                if (req is null || !por.ContainsKey(req) || req == codigo) continue;
                if (estado.GetValueOrDefault(req) == 1) return pila.SkipWhile(x => x != req).Append(req).ToList();
                if (estado.GetValueOrDefault(req) == 0 && Visitar(req) is { } ciclo) return ciclo;
            }
            pila.RemoveAt(pila.Count - 1);
            estado[codigo] = 2;
            return null;
        }

        foreach (var m in por.Keys)
            if (estado.GetValueOrDefault(m) == 0 && Visitar(m) is { } ciclo) return ciclo;
        return null;
    }

    // ── Escribir ──────────────────────────────────────────────────────────────────────────

    /// <summary>El pénsum en el formato estándar, con sangría y las propiedades en el orden documentado.</summary>
    public static string Escribir(PensumDefinicion p)
    {
        var raiz = new Dictionary<string, object?>
        {
            ["formato"] = p.Formato,
            ["universidad"] = p.Universidad,
            ["carrera"] = p.Carrera,
            ["nombreCarrera"] = p.NombreCarrera,
            ["version"] = p.Version,
            ["totalCreditos"] = p.TotalCreditos,
            ["cuatrimestres"] = p.Cuatrimestres,
            ["materias"] = p.Materias.Select(m => Ordenado(
                ("codigo", m.Codigo), ("nombre", m.Nombre), ("creditos", m.Creditos), ("cuatrimestre", m.Cuatrimestre),
                ("prerrequisitos", m.Prerrequisitos), ("electiva", m.Electiva))).ToList(),
        };
        if (p.BloquesElectivas.Count > 0)
            raiz["bloquesElectivas"] = p.BloquesElectivas.Select(b => Ordenado(
                ("codigo", b.Codigo), ("nombre", b.Nombre),
                ("opciones", b.Opciones.Select(o => Ordenado(("codigo", o.Codigo), ("nombre", o.Nombre))).ToList()))).ToList();
        if (p.Certificaciones.Count > 0)
            raiz["certificaciones"] = p.Certificaciones.Select(c =>
            {
                var d = Ordenado(("nombre", c.Nombre), ("materias", c.Materias), ("reemplazaElectivas", c.ReemplazaElectivas));
                if (c.Nota is not null) d["nota"] = c.Nota;
                return d;
            }).ToList();
        if (p.RequisitosGraduacion.Count > 0) raiz["requisitosGraduacion"] = p.RequisitosGraduacion;
        if (p.Equivalencias.Count > 0)
            raiz["equivalencias"] = p.Equivalencias.Select(e =>
            {
                var d = Ordenado(("origen", e.Origen), ("destino", e.Destino));
                if (e.Nota is not null) d["nota"] = e.Nota;
                return d;
            }).ToList();
        return JsonSerializer.Serialize(raiz, Escritura).Replace("\r\n", "\n") + "\n";
    }

    private static Dictionary<string, object?> Ordenado(params (string Clave, object? Valor)[] pares)
    {
        var d = new Dictionary<string, object?>();
        foreach (var (clave, valor) in pares) d[clave] = valor;
        return d;
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
        if (!o.TryGetProperty(propiedad, out var v)) return null;   // «falta» ya se informó donde corresponde
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
        if (v.ValueKind is JsonValueKind.True) return true;
        if (v.ValueKind is JsonValueKind.False) return false;
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

    private static List<string> ListaTextos(JsonElement o, string propiedad, string ruta, List<string> errores)
    {
        var resultado = new List<string>();
        if (!Arreglo(o, propiedad, ruta, errores, out var lista)) return resultado;
        var i = 0;
        foreach (var e in lista.EnumerateArray())
        {
            if (e.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(e.GetString()))
                errores.Add($"{Donde(ruta, propiedad)}[{i}] debe ser un texto no vacío.");
            else resultado.Add(e.GetString()!.Trim());
            i++;
        }
        return resultado;
    }
}
