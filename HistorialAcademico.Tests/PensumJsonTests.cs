using System.Text.Json;
using HistorialAcademico.Core.Pensum;

namespace HistorialAcademico.Tests;

/// <summary>Rutas y datos del pénsum de ejemplo (UNAPEC, Ingeniería de Software, plan 11).</summary>
internal static class PensumEjemplo
{
    public static string Raiz
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && dir.GetFiles("*.sln").Length == 0) dir = dir.Parent;
            return dir!.FullName;
        }
    }

    public static string CarpetaPensums => Path.Combine(Raiz, "pensums");
    public static string Archivo => Path.Combine(CarpetaPensums, "unapec", "ingenieria-software-11.json");
    public static string Csv => File.ReadAllText(Path.Combine(Raiz, "docs", "pensum_iso_unapec.csv"));

    public static readonly MetadatosPensum Meta = new("unapec", "ingenieria-software", "Ingeniería de Software", "11");

    /// <summary>Todo lo que el CSV no trae: las electivas con sus opciones, las certificaciones y lo que hay que cumplir además del pénsum.</summary>
    public static ResultadoPensumJson Convertir()
    {
        var bloques = new[]
        {
            ("E077", "Electiva I ISO-11"), ("E078", "Electiva II ISO-11"), ("E079", "Electiva III ISO-11"),
        }.Select(b => new BloqueElectivasDef(b.Item1, b.Item2,
            ElectivasReferencia.Opciones[b.Item1].Select(o => new OpcionElectivaDef(o.Codigo, o.Nombre)).ToList())).ToList();

        var certificaciones = new List<CertificacionDef>
        {
            new("Dirección de Proyectos", new() { "ISO122" }, true, null),
            new("Desarrollo en Emprendimiento", new() { "ADM202", "ADM537", "ADM538" }, true, "Se toman en este orden: ADM202, ADM537 y ADM538."),
        };

        var requisitos = new List<string>
        {
            "Aprobar un deporte.",
            "Aprobar los 8 niveles de inglés.",
            "Actitud Profesional (60 horas).",
            "Pasantía (150 a 300 horas).",
        };
        // Materias del plan anterior tal como aparecen en el histórico de Banner. Las dos últimas se sabe que no tienen equivalente.
        var equivalencias = new List<EquivalenciaDef>
        {
            new("ING701", new() { "ING716", "ING717" }, "Física I y Laboratorio del plan anterior."),
            new("ESP102", new(), "Redacción de Textos Discursivos I del plan anterior: sin equivalente."),
            new("MAT126", new(), "Matemática Básica para Ingenieros del plan anterior: sin equivalente."),
        };
        return PensumConversor.DesdeCsv(Csv, Meta, bloques, certificaciones, requisitos, equivalencias);
    }
}

/// <summary>Formato estándar de pénsum: lectura, validación, escritura, conversión del CSV y catálogo de archivos.</summary>
public class PensumJsonTests
{
    private const string Minimo = """
        {
          "formato": 1, "universidad": "uni-prueba", "carrera": "carrera-x", "nombreCarrera": "Carrera X", "version": "2019",
          "totalCreditos": 8, "cuatrimestres": 2,
          "materias": [
            { "codigo": "AAA100", "nombre": "Primera", "creditos": 3, "cuatrimestre": 1 },
            { "codigo": "BBB200", "nombre": "Segunda", "creditos": 5, "cuatrimestre": 2, "prerrequisitos": ["AAA100"] }
          ]
        }
        """;

    /// <summary>El mínimo con un cambio: se reemplaza un texto (para probar cada error por separado).</summary>
    private static ResultadoPensumJson Con(string buscar, string reemplazo) => PensumJson.Leer(Minimo.Replace(buscar, reemplazo));

    private static void HayError(ResultadoPensumJson r, string fragmento)
    {
        Assert.False(r.EsValido);
        Assert.Contains(r.Errores, e => e.Contains(fragmento, StringComparison.OrdinalIgnoreCase));
    }

    // ── Lo válido ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ElPensumMinimoEsValidoYSeLee()
    {
        var r = PensumJson.Leer(Minimo);

        Assert.True(r.EsValido, string.Join(" | ", r.Errores));
        var p = r.Definicion!;
        Assert.Equal(("uni-prueba", "carrera-x", "2019", 8, 2), (p.Universidad, p.Carrera, p.Version, p.TotalCreditos, p.Cuatrimestres));
        Assert.Equal("uni-prueba/carrera-x-2019.json", p.Clave);
        Assert.Equal(new[] { "AAA100", "BBB200" }, p.Materias.Select(m => m.Codigo));
        Assert.Equal(new[] { "AAA100" }, p.Materias[1].Prerrequisitos);
        Assert.Empty(p.Materias[0].Prerrequisitos);
        Assert.Empty(p.BloquesElectivas);
    }

    [Fact]
    public void LasMateriasSePasanAlFormatoDeLaBaseDeDatosConSusPrerrequisitos()
    {
        var p = PensumJson.Leer(Minimo).Definicion!;

        var m = p.AMateriasPensum();

        Assert.Equal(("BBB200", 5, 2, "AAA100", false), (m[1].Codigo, m[1].Creditos, m[1].Cuatrimestre, m[1].Prerrequisitos, m[1].EsElectiva));
        Assert.Null(m[0].Prerrequisitos);
    }

    [Fact]
    public void EscribirYVolverALeerDevuelveLoMismo()
    {
        var p = PensumEjemplo.Convertir().Definicion!;

        var otra = PensumJson.Leer(PensumJson.Escribir(p)).Definicion!;

        Assert.Equal(p.Materias.Select(m => (m.Codigo, m.Nombre, m.Creditos, m.Cuatrimestre, string.Join("|", m.Prerrequisitos), m.Electiva)),
                     otra.Materias.Select(m => (m.Codigo, m.Nombre, m.Creditos, m.Cuatrimestre, string.Join("|", m.Prerrequisitos), m.Electiva)));
        Assert.Equal(p.BloquesElectivas.Select(b => (b.Codigo, b.Opciones.Count)), otra.BloquesElectivas.Select(b => (b.Codigo, b.Opciones.Count)));
        Assert.Equal(p.Certificaciones.Select(c => c.Nombre), otra.Certificaciones.Select(c => c.Nombre));
        Assert.Equal(p.RequisitosGraduacion, otra.RequisitosGraduacion);
    }

    [Fact]
    public void ElArchivoEscritoLlevaLasPropiedadesEnElOrdenDocumentadoYConTildesLegibles()
    {
        var json = PensumJson.Escribir(PensumEjemplo.Convertir().Definicion!);

        Assert.StartsWith("{\n  \"formato\": 1,\n  \"universidad\": \"unapec\"", json);
        Assert.Contains("\"nombreCarrera\": \"Ingeniería de Software\"", json);   // sin í
        Assert.DoesNotContain("\\u", json);
        Assert.EndsWith("}\n", json);
        Assert.True(json.IndexOf("\"totalCreditos\"", StringComparison.Ordinal) < json.IndexOf("\"materias\"", StringComparison.Ordinal));
        Assert.True(json.IndexOf("\"materias\"", StringComparison.Ordinal) < json.IndexOf("\"bloquesElectivas\"", StringComparison.Ordinal));
    }

    // ── Errores del JSON ──────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("no es json")]
    [InlineData("")]
    [InlineData("{ \"formato\": 1, }")]
    public void UnArchivoQueNoEsJsonDaUnErrorClaro(string texto) => HayError(PensumJson.Leer(texto), "no es un JSON válido");

    [Fact]
    public void LaRaizDebeSerUnObjeto() => HayError(PensumJson.Leer("[1,2]"), "debe ser un objeto");

    [Fact]
    public void FaltanLasPropiedadesObligatorias()
    {
        var r = PensumJson.Leer("{}");

        Assert.False(r.EsValido);
        foreach (var p in PensumJson.RequeridasRaiz) Assert.Contains(r.Errores, e => e.Contains($"«{p}»"));
    }

    [Fact]
    public void UnaPropiedadDesconocidaSeRechazaParaQueLosErroresDeTipeoNoPasenDesapercibidos()
    {
        HayError(Con("\"version\": \"2019\",", "\"version\": \"2019\", \"cuatrimestre\": 3,"), "Propiedad desconocida «cuatrimestre»");
        HayError(Con("\"creditos\": 3,", "\"creditos\": 3, \"creditso\": 1,"), "propiedad desconocida «creditso»");
    }

    [Theory]
    [InlineData("\"formato\": 1", "\"formato\": 2", "«formato»")]
    [InlineData("\"universidad\": \"uni-prueba\"", "\"universidad\": \"Uni Prueba\"", "universidad")]
    [InlineData("\"carrera\": \"carrera-x\"", "\"carrera\": \"\"", "no puede estar vacío")]
    [InlineData("\"version\": \"2019\"", "\"version\": 2019", "debe ser un texto")]
    [InlineData("\"totalCreditos\": 8", "\"totalCreditos\": \"ocho\"", "número entero")]
    [InlineData("\"cuatrimestres\": 2", "\"cuatrimestres\": 0", "entre 1 y 24")]
    [InlineData("\"cuatrimestres\": 2", "\"cuatrimestres\": 30", "entre 1 y 24")]
    [InlineData("\"codigo\": \"AAA100\"", "\"codigo\": \"aaa 100\"", "letras mayúsculas y números")]
    [InlineData("\"creditos\": 3", "\"creditos\": 3.5", "número entero")]
    [InlineData("\"creditos\": 3", "\"creditos\": -1", "entre 0 y 20")]
    [InlineData("\"creditos\": 3", "\"creditos\": 99", "entre 0 y 20")]
    [InlineData("\"cuatrimestre\": 2", "\"cuatrimestre\": 3", "materias[1].cuatrimestre debe estar entre 1 y 2")]
    public void CadaPropiedadConUnValorInvalidoDaSuPropioError(string buscar, string reemplazo, string fragmento) => HayError(Con(buscar, reemplazo), fragmento);

    [Fact]
    public void ElErrorDiceDondeEstaElProblema()
    {
        var r = Con("\"nombre\": \"Segunda\"", "\"nombre\": 5");

        Assert.Contains("materias[1].nombre debe ser un texto.", r.Errores);
    }

    [Fact]
    public void LasMateriasNoPuedenEstarVaciasNiRepetirCodigo()
    {
        HayError(PensumJson.Leer("""
            { "formato": 1, "universidad": "u", "carrera": "c", "nombreCarrera": "C", "version": "1", "totalCreditos": 0, "cuatrimestres": 1, "materias": [] }
            """), "no puede estar vacío");

        HayError(Con("\"codigo\": \"BBB200\"", "\"codigo\": \"AAA100\""), "el código AAA100 está repetido");
    }

    // ── Errores de relaciones ─────────────────────────────────────────────────────────────

    [Fact]
    public void UnPrerrequisitoQueNoExisteSeRechaza() => HayError(Con("[\"AAA100\"]", "[\"ZZZ999\"]"), "su prerrequisito ZZZ999 no existe");

    [Fact]
    public void UnaMateriaNoPuedeSerPrerrequisitoDeSiMisma() => HayError(Con("[\"AAA100\"]", "[\"BBB200\"]"), "no puede ser prerrequisito de sí misma");

    [Fact]
    public void LosCiclosDePrerrequisitosSeRechazan()
    {
        var r = Con("\"nombre\": \"Primera\", \"creditos\": 3, \"cuatrimestre\": 1", "\"nombre\": \"Primera\", \"creditos\": 3, \"cuatrimestre\": 1, \"prerrequisitos\": [\"BBB200\"]");

        HayError(r, "forman un ciclo: AAA100 → BBB200 → AAA100");
    }

    [Theory]
    [InlineData("[\"AAA100; BBB200\"]")]     // dos en un mismo elemento
    [InlineData("[\"algo raro\"]")]
    [InlineData("[\"\"]")]
    [InlineData("[5]")]
    public void UnPrerrequisitoMalEscritoSeRechaza(string valor) => Assert.False(Con("[\"AAA100\"]", valor).EsValido);

    [Fact]
    public void LasReglasDePorcentajeSeAceptanComoPrerrequisito()
    {
        var r = Con("[\"AAA100\"]", "[\"AAA100\", \"67% créditos aprobados\"]");

        Assert.True(r.EsValido, string.Join(" | ", r.Errores));
        Assert.Equal(new[] { "AAA100", "67% créditos aprobados" }, r.Definicion!.Materias[1].Prerrequisitos);
        Assert.Equal("AAA100; 67% créditos aprobados", r.Definicion.AMateriasPensum()[1].Prerrequisitos);
    }

    [Fact]
    public void UnPrerrequisitoDeUnCuatrimestrePosteriorEsSoloUnaAdvertencia()
    {
        var r = Con("\"nombre\": \"Primera\", \"creditos\": 3, \"cuatrimestre\": 1", "\"nombre\": \"Primera\", \"creditos\": 3, \"cuatrimestre\": 1, \"prerrequisitos\": [\"CCC300\"]")
            .Errores;   // CCC300 no existe: error; lo que se prueba abajo es el caso válido

        var valido = PensumJson.Leer("""
            { "formato": 1, "universidad": "u", "carrera": "c", "nombreCarrera": "C", "version": "1", "totalCreditos": 2, "cuatrimestres": 2,
              "materias": [ { "codigo": "AA1", "nombre": "A", "creditos": 1, "cuatrimestre": 1, "prerrequisitos": ["BB2"] },
                            { "codigo": "BB2", "nombre": "B", "creditos": 1, "cuatrimestre": 2 } ] }
            """);

        Assert.NotEmpty(r);
        Assert.True(valido.EsValido, string.Join(" | ", valido.Errores));
        Assert.Contains(valido.Advertencias, a => a.Contains("cuatrimestre posterior"));
    }

    [Fact]
    public void ElTotalDeCreditosDebeSerLaSumaDeLasMaterias() => HayError(Con("\"totalCreditos\": 8", "\"totalCreditos\": 9"), "suman 8");

    // ── Electivas y certificaciones ───────────────────────────────────────────────────────

    private const string ConElectiva = """
        {
          "formato": 1, "universidad": "u", "carrera": "c", "nombreCarrera": "C", "version": "1", "totalCreditos": 6, "cuatrimestres": 1,
          "materias": [
            { "codigo": "AAA100", "nombre": "Normal", "creditos": 3, "cuatrimestre": 1 },
            { "codigo": "E001", "nombre": "Electiva I", "creditos": 3, "cuatrimestre": 1, "electiva": true }
          ],
          "bloquesElectivas": [ { "codigo": "E001", "nombre": "Electiva I", "opciones": [ { "codigo": "OPC101", "nombre": "Opción uno" } ] } ],
          "certificaciones": [ { "nombre": "Cert", "materias": ["CER100"], "reemplazaElectivas": true, "nota": "n" } ],
          "requisitosGraduacion": ["Aprobar un deporte."]
        }
        """;

    [Fact]
    public void LasElectivasLasCertificacionesYLosRequisitosSeLeen()
    {
        var r = PensumJson.Leer(ConElectiva);

        Assert.True(r.EsValido, string.Join(" | ", r.Errores));
        var p = r.Definicion!;
        Assert.True(p.Materias[1].Electiva);
        Assert.Equal(("E001", "OPC101"), (p.BloquesElectivas[0].Codigo, p.BloquesElectivas[0].Opciones[0].Codigo));
        Assert.Equal(("Cert", true, "n"), (p.Certificaciones[0].Nombre, p.Certificaciones[0].ReemplazaElectivas, p.Certificaciones[0].Nota));
        Assert.Equal(new[] { "Aprobar un deporte." }, p.RequisitosGraduacion);
    }

    [Fact]
    public void UnBloqueDeElectivasDebeReferirseAUnaMateriaElectiva()
    {
        HayError(PensumJson.Leer(ConElectiva.Replace("\"codigo\": \"E001\", \"nombre\": \"Electiva I\", \"opciones\"", "\"codigo\": \"E999\", \"nombre\": \"Otra\", \"opciones\"")), "E999 no está en «materias»");
        HayError(PensumJson.Leer(ConElectiva.Replace(", \"electiva\": true", "")), "no está marcada como «electiva»");
    }

    [Fact]
    public void UnBloqueSinOpcionesOUnaCertificacionSinMateriasSeRechazan()
    {
        HayError(PensumJson.Leer(ConElectiva.Replace("[ { \"codigo\": \"OPC101\", \"nombre\": \"Opción uno\" } ]", "[]")), "«opciones» no puede estar vacío");
        HayError(PensumJson.Leer(ConElectiva.Replace("\"materias\": [\"CER100\"], ", "")), "falta «materias»");
        HayError(PensumJson.Leer(ConElectiva.Replace("\"CER100\"", "\"cer 100\"")), "no es un código de materia válido");
    }

    // ── Archivo y carpeta ─────────────────────────────────────────────────────────────────

    [Fact]
    public void ElArchivoDebeEstarEnLaCarpetaDeSuUniversidadConElNombreDeCarreraYVersion()
    {
        var bueno = Path.Combine("pensums", "uni-prueba", "carrera-x-2019.json");

        Assert.True(PensumJson.Leer(Minimo, bueno).EsValido);
        HayError(PensumJson.Leer(Minimo, Path.Combine("pensums", "otra", "carrera-x-2019.json")), "está en la carpeta «otra»");
        HayError(PensumJson.Leer(Minimo, Path.Combine("pensums", "uni-prueba", "carrera-x.json")), "se debe llamar «carrera-x-2019.json»");
    }

    // ── Conversión del CSV ────────────────────────────────────────────────────────────────

    [Fact]
    public void ElCsvDeUnapecSeConvierteAlFormatoEstandarYSaleValido()
    {
        var r = PensumEjemplo.Convertir();

        Assert.True(r.EsValido, string.Join(" | ", r.Errores));
        var p = r.Definicion!;
        Assert.Equal(218, p.TotalCreditos);                       // la suma del CSV, la cifra que confirmaste
        Assert.Equal(12, p.Cuatrimestres);
        Assert.Equal(PensumCsvParser.Parse(PensumEjemplo.Csv).Materias.Count, p.Materias.Count);
        Assert.Equal(("unapec/ingenieria-software-11.json"), p.Clave);
    }

    [Fact]
    public void LosPrerrequisitosDelCsvSePartenEnUnElementoCadaUno()
    {
        var p = PensumEjemplo.Convertir().Definicion!;

        Assert.Equal(new[] { "E077", "67% créditos aprobados" }, p.Materias.Single(m => m.Codigo == "E078").Prerrequisitos);
        Assert.Equal(new[] { "SOC253", "90% créditos aprobados" }, p.Materias.Single(m => m.Codigo == "SOC281").Prerrequisitos);
        Assert.Equal(new[] { "ESP101" }, p.Materias.Single(m => m.Codigo == "ESP106").Prerrequisitos);
        Assert.Empty(p.Materias.Single(m => m.Codigo == "ISO100").Prerrequisitos);
    }

    [Fact]
    public void LasMateriasConvertidasSonExactamenteLasDelCsvAlVolverAlFormatoAntiguo()
    {
        var original = PensumCsvParser.Parse(PensumEjemplo.Csv).Materias;
        var convertidas = PensumEjemplo.Convertir().Definicion!.AMateriasPensum();

        Assert.Equal(original.Select(m => (m.Codigo, m.Nombre, m.Creditos, m.Cuatrimestre, m.Prerrequisitos?.Replace(" ", ""), m.EsElectiva)),
                     convertidas.Select(m => (m.Codigo, m.Nombre, m.Creditos, m.Cuatrimestre, m.Prerrequisitos?.Replace(" ", ""), m.EsElectiva)));
    }

    [Fact]
    public void UnCsvConErroresNoSeConvierte()
    {
        var r = PensumConversor.DesdeCsv("codigo,nombre\nX,Y", PensumEjemplo.Meta);

        Assert.False(r.EsValido);
        Assert.Contains(r.Errores, e => e.Contains("Encabezado inesperado"));
        Assert.Null(r.Definicion);
    }

    [Fact]
    public void LosMetadatosInvalidosHacenFallarLaConversion()
    {
        var r = PensumConversor.DesdeCsv(PensumEjemplo.Csv, new MetadatosPensum("UNAPEC", "Ingeniería", "X", "11"));

        Assert.False(r.EsValido);
    }

    // ── El pénsum de ejemplo del repositorio ──────────────────────────────────────────────

    [Fact]
    public void GenerarElArchivoDeEjemploSoloConLaVariableDeEntorno()
    {
        // Para regenerar pensums/unapec/ingenieria-software-11.json después de cambiar el CSV o los datos de arriba:
        //   $env:GENERAR_PENSUMS = "1"; dotnet test --filter GenerarElArchivoDeEjemplo
        if (Environment.GetEnvironmentVariable("GENERAR_PENSUMS") != "1") return;
        Directory.CreateDirectory(Path.GetDirectoryName(PensumEjemplo.Archivo)!);
        File.WriteAllText(PensumEjemplo.Archivo, PensumJson.Escribir(PensumEjemplo.Convertir().Definicion!), new System.Text.UTF8Encoding(false));
    }

    [Fact]
    public void ElArchivoDeEjemploDelRepositorioEsValidoYCoincideConElCsvActual()
    {
        Assert.True(File.Exists(PensumEjemplo.Archivo), "Falta pensums/unapec/ingenieria-software-11.json (ver GenerarElArchivoDeEjemplo).");

        var leido = PensumJson.Leer(File.ReadAllText(PensumEjemplo.Archivo), PensumEjemplo.Archivo);

        Assert.True(leido.EsValido, string.Join(" | ", leido.Errores));
        Assert.Empty(leido.Advertencias);
        Assert.Equal(PensumJson.Escribir(PensumEjemplo.Convertir().Definicion!), File.ReadAllText(PensumEjemplo.Archivo).Replace("\r\n", "\n"));   // si cambia el CSV, hay que regenerar
        var p = leido.Definicion!;
        Assert.Equal(new[] { "E077", "E078", "E079" }, p.BloquesElectivas.Select(b => b.Codigo));
        Assert.Equal(new[] { "ADM103", "ADM536", "ADM540" }, p.BloquesElectivas[0].Opciones.Select(o => o.Codigo));
        Assert.Equal(2, p.Certificaciones.Count);
        Assert.Equal(4, p.RequisitosGraduacion.Count);
    }

    // ── Esquema publicado ─────────────────────────────────────────────────────────────────

    [Fact]
    public void ElEsquemaPublicadoDescribeLasMismasPropiedadesQueLaValidacion()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(PensumEjemplo.CarpetaPensums, "schema.json")));
        var raiz = doc.RootElement;
        var defs = raiz.GetProperty("$defs");

        Assert.Equal("https://json-schema.org/draft/2020-12/schema", raiz.GetProperty("$schema").GetString());
        Assert.False(raiz.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(PensumJson.PropiedadesRaiz.OrderBy(x => x), raiz.GetProperty("properties").EnumerateObject().Select(p => p.Name).OrderBy(x => x));
        Assert.Equal(PensumJson.RequeridasRaiz.OrderBy(x => x), raiz.GetProperty("required").EnumerateArray().Select(e => e.GetString()!).OrderBy(x => x));
        Assert.Equal(PensumJson.PropiedadesMateria.OrderBy(x => x), defs.GetProperty("materia").GetProperty("properties").EnumerateObject().Select(p => p.Name).OrderBy(x => x));
        Assert.Equal(PensumJson.RequeridasMateria.OrderBy(x => x), defs.GetProperty("materia").GetProperty("required").EnumerateArray().Select(e => e.GetString()!).OrderBy(x => x));
        Assert.Equal(PensumJson.PropiedadesBloque.OrderBy(x => x), defs.GetProperty("bloqueElectivas").GetProperty("properties").EnumerateObject().Select(p => p.Name).OrderBy(x => x));
        Assert.Equal(PensumJson.PropiedadesCertificacion.OrderBy(x => x), defs.GetProperty("certificacion").GetProperty("properties").EnumerateObject().Select(p => p.Name).OrderBy(x => x));
        Assert.Equal(PensumJson.MaxCuatrimestres, raiz.GetProperty("properties").GetProperty("cuatrimestres").GetProperty("maximum").GetInt32());
        Assert.Equal(PensumJson.MaxCreditosMateria, defs.GetProperty("materia").GetProperty("properties").GetProperty("creditos").GetProperty("maximum").GetInt32());
    }

    // ── Catálogo de archivos ──────────────────────────────────────────────────────────────

    private static string CarpetaTemporal()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pensums-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void Escribir(string carpeta, string universidad, string archivo, string contenido)
    {
        Directory.CreateDirectory(Path.Combine(carpeta, universidad));
        File.WriteAllText(Path.Combine(carpeta, universidad, archivo), contenido);
    }

    [Fact]
    public void ElCatalogoListaLosPensumsValidosEInvalidosSinTragarseLosErrores()
    {
        var dir = CarpetaTemporal();
        try
        {
            Escribir(dir, "uni-prueba", "carrera-x-2019.json", Minimo);
            Escribir(dir, "uni-prueba", "rota-1.json", "{ no es json");
            Escribir(dir, "uni-prueba", "universidad.json", "{ \"esto\": \"no es un pénsum\" }");
            File.WriteAllText(Path.Combine(dir, "schema.json"), "{}");

            var catalogo = CatalogoPensums.Leer(dir);

            Assert.Equal(2, catalogo.Count);                               // schema.json y universidad.json no cuentan
            Assert.True(catalogo.Single(e => e.Ruta.EndsWith("carrera-x-2019.json")).EsValida);
            var rota = catalogo.Single(e => e.Ruta.EndsWith("rota-1.json"));
            Assert.False(rota.EsValida);
            Assert.Contains(rota.Resultado.Errores, e => e.Contains("no es un JSON válido"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void UnaCopiaDelMismoPensumConOtroNombreDeArchivoOCarpetaSeRechaza()
    {
        var dir = CarpetaTemporal();
        try
        {
            Escribir(dir, "uni-prueba", "carrera-x-2019.json", Minimo);
            Escribir(dir, "uni-prueba", "copia.json", Minimo);
            Escribir(dir, "otra", "carrera-x-2019.json", Minimo);

            var catalogo = CatalogoPensums.Leer(dir);

            Assert.Single(catalogo, e => e.EsValida);                    // solo el que está donde debe
            Assert.Equal(2, catalogo.Count(e => !e.EsValida));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void UnaCarpetaQueNoExisteDaUnCatalogoVacio() => Assert.Empty(CatalogoPensums.Leer(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));

    [Fact]
    public void ElCatalogoDelRepositorioSoloTienePensumsValidos()
    {
        var catalogo = CatalogoPensums.Leer(PensumEjemplo.CarpetaPensums);

        Assert.NotEmpty(catalogo);
        Assert.All(catalogo, e => Assert.True(e.EsValida, $"{e.Ruta}: {string.Join(" | ", e.Resultado.Errores)}"));
    }
}
