using System.Text;
using HistorialAcademico.Banner;
using HistorialAcademico.Core.Pensum;
using HistorialAcademico.Core.Universidad;

namespace HistorialAcademico.Validador;

/// <summary>
/// Herramienta para quien agrega o revisa pénsums: <c>validar</c> comprueba la carpeta pensums/ con las mismas reglas que usa la
/// aplicación (y que ejecuta la GitHub Action en cada Pull Request) y <c>convertir</c> pasa un CSV al formato estándar.
/// Todos los mensajes van en español. Códigos de salida: 0 = todo bien, 1 = hay errores, 2 = uso incorrecto.
/// </summary>
public static class Comandos
{
    public const int Bien = 0, ConErrores = 1, UsoIncorrecto = 2;

    public const string Ayuda = """
        Uso:
          validador-pensums validar [carpeta]
              Valida los pénsums y las universidades de la carpeta (por omisión, «pensums»).

          validador-pensums convertir <archivo.csv> --universidad <id> --carrera <id> --nombre "<nombre de la carrera>" --version <versión> [--salida <archivo.json>] [--forzar]
              Convierte un CSV (codigo,nombre,creditos,cuatrimestre,prerrequisitos,es_electiva) al formato estándar.
              Por omisión lo guarda en pensums/<universidad>/<carrera>-<versión>.json y no pisa un archivo que ya exista (usa --forzar).
              Con --salida - escribe el JSON en la pantalla.

          validador-pensums anonimizar <historico.html> [--salida <archivo.html>] [--anio-inicial <año>] [--semilla <número>] [--forzar]
              Convierte un Histórico Académico real de Banner en una muestra que se puede subir al repositorio: identidad ficticia,
              calificaciones sorteadas de nuevo y totales recalculados (se conservan los códigos, títulos y créditos de las materias).
              Por omisión la guarda en tests/samples/<nombre>-anonimizado.html; nunca toca el archivo original y no pisa uno existente (usa --forzar).
              Revisa el resultado a mano antes de hacer commit.

        Códigos de salida: 0 = todo bien, 1 = hay errores, 2 = uso incorrecto.
        """;

    public static int Ejecutar(string[] argumentos, TextWriter salida, TextWriter error, string? directorioActual = null)
    {
        directorioActual ??= Directory.GetCurrentDirectory();
        if (argumentos.Length == 0 || argumentos[0] is "-h" or "--help" or "ayuda")
        {
            (argumentos.Length == 0 ? error : salida).WriteLine(Ayuda);
            return argumentos.Length == 0 ? UsoIncorrecto : Bien;
        }

        return argumentos[0].ToLowerInvariant() switch
        {
            "validar" => Validar(argumentos.Skip(1).ToArray(), salida, error, directorioActual),
            "convertir" => Convertir(argumentos.Skip(1).ToArray(), salida, error, directorioActual),
            "anonimizar" => Anonimizar(argumentos.Skip(1).ToArray(), salida, error, directorioActual),
            var otro => Uso(error, $"No conozco el comando «{otro}»."),
        };
    }

    private static int Uso(TextWriter error, string motivo)
    {
        error.WriteLine(motivo);
        error.WriteLine();
        error.WriteLine(Ayuda);
        return UsoIncorrecto;
    }

    // ── validar ───────────────────────────────────────────────────────────────────────────

    private static int Validar(string[] args, TextWriter salida, TextWriter error, string directorioActual)
    {
        if (args.Length > 1) return Uso(error, "«validar» recibe como máximo una carpeta.");
        var carpeta = Path.GetFullPath(args.Length == 1 ? args[0] : "pensums", directorioActual);
        if (!Directory.Exists(carpeta))
        {
            error.WriteLine($"No existe la carpeta «{carpeta}».");
            return ConErrores;
        }

        var catalogo = CatalogoUniversidades.Leer(carpeta);
        if (catalogo.Universidades.Count == 0 && catalogo.Pensums.Count == 0)
        {
            error.WriteLine($"No encontré ningún pénsum ni universidad en «{carpeta}».");
            return ConErrores;
        }

        var malas = 0;
        foreach (var u in catalogo.Universidades)
        {
            var nombre = Relativa(carpeta, u.Ruta);
            if (u.EsValida) salida.WriteLine($"OK      {nombre}  ({u.Resultado.Reglas!.Nombre})");
            else { malas++; Informar(salida, "ERROR", nombre, u.Resultado.Errores); }
        }
        foreach (var p in catalogo.Pensums)
        {
            var nombre = Relativa(carpeta, p.Ruta);
            if (p.EsValida)
            {
                var d = p.Resultado.Definicion!;
                salida.WriteLine($"OK      {nombre}  ({d.NombreCarrera}: {d.Materias.Count} materias, {d.TotalCreditos} créditos, {d.Cuatrimestres} cuatrimestres)");
            }
            else { malas++; Informar(salida, "ERROR", nombre, p.Resultado.Errores); }
            foreach (var a in p.Resultado.Advertencias) salida.WriteLine($"  AVISO {a}");
        }

        salida.WriteLine();
        var buenos = catalogo.Universidades.Count(u => u.EsValida) + catalogo.Pensums.Count(p => p.EsValida);
        salida.WriteLine(malas == 0
            ? $"Todo en orden: {catalogo.Pensums.Count} {(catalogo.Pensums.Count == 1 ? "pénsum" : "pénsums")} y {catalogo.Universidades.Count} {(catalogo.Universidades.Count == 1 ? "universidad" : "universidades")} válidos."
            : $"{malas} archivos con errores ({buenos} válidos). Corrige los errores marcados y vuelve a validar.");
        return malas == 0 ? Bien : ConErrores;
    }

    private static void Informar(TextWriter salida, string marca, string archivo, IEnumerable<string> errores)
    {
        salida.WriteLine($"{marca,-7} {archivo}");
        foreach (var e in errores) salida.WriteLine($"  - {e}");
    }

    private static string Relativa(string carpeta, string ruta) => Path.GetRelativePath(carpeta, ruta).Replace('\\', '/');

    // ── convertir ─────────────────────────────────────────────────────────────────────────

    private static int Convertir(string[] args, TextWriter salida, TextWriter error, string directorioActual)
    {
        var (posicionales, opciones, problema) = LeerArgumentos(args);
        if (problema is not null) return Uso(error, problema);
        if (posicionales.Count != 1) return Uso(error, "«convertir» necesita el archivo CSV.");
        foreach (var requerida in new[] { "universidad", "carrera", "nombre", "version" })
            if (!opciones.TryGetValue(requerida, out var v) || string.IsNullOrWhiteSpace(v))
                return Uso(error, $"Falta la opción --{requerida}.");
        var conocidas = new[] { "universidad", "carrera", "nombre", "version", "salida", "forzar" };
        foreach (var desconocida in opciones.Keys.Where(k => !conocidas.Contains(k))) return Uso(error, $"No conozco la opción --{desconocida}.");

        var rutaCsv = Path.GetFullPath(posicionales[0], directorioActual);
        if (!File.Exists(rutaCsv))
        {
            error.WriteLine($"No existe el archivo «{rutaCsv}».");
            return ConErrores;
        }

        var meta = new MetadatosPensum(opciones["universidad"], opciones["carrera"], opciones["nombre"], opciones["version"]);
        var r = PensumConversor.DesdeCsv(File.ReadAllText(rutaCsv), meta);
        if (!r.EsValido)
        {
            error.WriteLine("No se pudo convertir:");
            foreach (var e in r.Errores) error.WriteLine($"  - {e}");
            return ConErrores;
        }

        var json = PensumJson.Escribir(r.Definicion!);
        foreach (var a in r.Advertencias) salida.WriteLine($"AVISO {a}");

        if (opciones.TryGetValue("salida", out var destino) && destino == "-")
        {
            salida.Write(json);
            return Bien;
        }

        var ruta = Path.GetFullPath(opciones.TryGetValue("salida", out var indicada) ? indicada : Path.Combine("pensums", r.Definicion!.Universidad, r.Definicion.NombreArchivo), directorioActual);
        if (File.Exists(ruta) && !opciones.ContainsKey("forzar"))
        {
            error.WriteLine($"Ya existe «{ruta}». Usa --forzar si quieres reemplazarlo.");
            return ConErrores;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);
        File.WriteAllText(ruta, json, new UTF8Encoding(false));

        salida.WriteLine($"Guardado: {ruta}");
        salida.WriteLine($"{r.Definicion!.Materias.Count} materias, {r.Definicion.TotalCreditos} créditos, {r.Definicion.Cuatrimestres} cuatrimestres.");
        if (!File.Exists(Path.Combine(Path.GetDirectoryName(ruta)!, "universidad.json")))
            salida.WriteLine($"AVISO Falta {r.Definicion.Universidad}/universidad.json (escala de calificaciones, períodos y límites de créditos): sin él el pénsum no se puede usar.");
        salida.WriteLine("Revisa el archivo y agrega a mano lo que el CSV no trae (bloquesElectivas, certificaciones, requisitosGraduacion y equivalencias), si aplica.");
        salida.WriteLine("Después ejecuta «validar» para comprobarlo.");
        return Bien;
    }

    // ── anonimizar ────────────────────────────────────────────────────────────────────────

    private static int Anonimizar(string[] args, TextWriter salida, TextWriter error, string directorioActual)
    {
        var (posicionales, opciones, problema) = LeerArgumentos(args);
        if (problema is not null) return Uso(error, problema);
        if (posicionales.Count != 1) return Uso(error, "«anonimizar» necesita el archivo HTML del histórico.");
        foreach (var desconocida in opciones.Keys.Where(k => k is not ("salida" or "anio-inicial" or "semilla" or "forzar"))) return Uso(error, $"No conozco la opción --{desconocida}.");

        var anio = AnonimizadorHistorico.AnioInicialPorOmision;
        var semilla = AnonimizadorHistorico.SemillaPorOmision;
        if (opciones.TryGetValue("anio-inicial", out var a) && !int.TryParse(a, out anio)) return Uso(error, "--anio-inicial debe ser un año, por ejemplo 2020.");
        if (opciones.TryGetValue("semilla", out var s) && !int.TryParse(s, out semilla)) return Uso(error, "--semilla debe ser un número entero.");

        var entrada = Path.GetFullPath(posicionales[0], directorioActual);
        if (!File.Exists(entrada))
        {
            error.WriteLine($"No existe el archivo «{entrada}».");
            return ConErrores;
        }

        var ruta = Path.GetFullPath(opciones.TryGetValue("salida", out var indicada)
            ? indicada
            : Path.Combine("tests", "samples", Path.GetFileNameWithoutExtension(entrada) + "-anonimizado.html"), directorioActual);
        if (string.Equals(ruta, entrada, StringComparison.OrdinalIgnoreCase))
        {
            error.WriteLine("La salida no puede ser el mismo archivo que la entrada: el original no se toca.");
            return ConErrores;
        }
        if (File.Exists(ruta) && !opciones.ContainsKey("forzar"))
        {
            error.WriteLine($"Ya existe «{ruta}». Usa --forzar si quieres reemplazarlo.");
            return ConErrores;
        }

        string html;
        try { html = AnonimizadorHistorico.Anonimizar(File.ReadAllText(entrada), anio, semilla); }
        catch (Exception ex)
        {
            error.WriteLine($"No se pudo anonimizar «{Path.GetFileName(entrada)}»: {ex.Message}");
            return ConErrores;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);
        File.WriteAllText(ruta, html, new UTF8Encoding(false));
        salida.WriteLine($"Guardado: {ruta}");
        salida.WriteLine("Es un histórico con datos ficticios: identidad, fechas de los períodos y calificaciones nuevas; los totales se recalcularon y se comprobó que cuadran.");
        salida.WriteLine("Ábrelo y revísalo antes de hacer commit. Nunca subas el archivo original.");
        return Bien;
    }

    /// <summary>Separa los argumentos posicionales de las opciones «--nombre valor» (y las banderas sin valor, como --forzar).</summary>
    private static (List<string> Posicionales, Dictionary<string, string> Opciones, string? Problema) LeerArgumentos(string[] args)
    {
        var posicionales = new List<string>();
        var opciones = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--")) { posicionales.Add(args[i]); continue; }
            var nombre = args[i][2..].ToLowerInvariant();
            if (nombre == "forzar") { opciones[nombre] = "true"; continue; }
            if (i + 1 >= args.Length) return (posicionales, opciones, $"Falta el valor de --{nombre}.");
            opciones[nombre] = args[++i];
        }
        return (posicionales, opciones, null);
    }
}
