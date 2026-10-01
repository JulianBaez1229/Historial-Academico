using System.Text.RegularExpressions;
using HistorialAcademico.Core.Pensum;
using HistorialAcademico.Core.Universidad;
using HistorialAcademico.Validador;
using HistorialAcademico.Web.Helpers;

namespace HistorialAcademico.Tests;

/// <summary>La guía CONTRIBUTING-pensums.md y la GitHub Action: que existan, que no se desactualicen y que lo que muestran funcione.</summary>
public class GuiaYFlujoDeTrabajoTests
{
    private static string Ruta(params string[] partes) => Path.Combine(new[] { PensumEjemplo.Raiz }.Concat(partes).ToArray());
    private static string Guia => File.ReadAllText(Ruta("CONTRIBUTING-pensums.md"));
    private static string Flujo => File.ReadAllText(Ruta(".github", "workflows", "validar-pensums.yml"));

    /// <summary>Los bloques de código de la guía con el lenguaje indicado (```json … ```).</summary>
    private static List<string> Bloques(string lenguaje) =>
        Regex.Matches(Guia.Replace("\r\n", "\n"), "```" + lenguaje + "\n(.*?)\n\\s*```", RegexOptions.Singleline).Select(m => m.Groups[1].Value).ToList();

    // ── La guía ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void LaGuiaExisteYCuentaElCaminoCompleto()
    {
        var guia = Guia;

        Assert.StartsWith("# Cómo agregar tu carrera", guia);
        foreach (var titulo in new[] { "## Antes de empezar", "## Dónde va cada archivo", "## Paso 1: la universidad", "## Paso 2: el pénsum",
                                       "## Paso 3: comprueba tu archivo", "## Paso 4: abre el Pull Request", "## Preguntas frecuentes" })
            Assert.Contains(titulo, guia);
        Assert.Contains("git clone", guia);
        Assert.Contains("git switch -c", guia);
        Assert.Contains("git push", guia);
        Assert.Contains("GitHub Action", guia);
        Assert.Contains("datos personales", guia);
    }

    [Fact]
    public void LaGuiaExplicaCadaPropiedadDelFormato()
    {
        var guia = Guia;
        var todas = PensumJson.PropiedadesRaiz.Concat(PensumJson.PropiedadesMateria).Concat(PensumJson.PropiedadesBloque)
            .Concat(PensumJson.PropiedadesOpcion).Concat(PensumJson.PropiedadesCertificacion).Concat(PensumJson.PropiedadesEquivalencia)
            .Concat(ReglasUniversidadJson.PropiedadesRaiz).Concat(ReglasUniversidadJson.PropiedadesLetra)
            .Concat(ReglasUniversidadJson.PropiedadesPeriodo).Concat(ReglasUniversidadJson.PropiedadesLimites).Distinct();

        foreach (var propiedad in todas) Assert.Contains($"`{propiedad}`", guia);
    }

    [Fact]
    public void LosEjemplosJsonDeLaGuiaSonValidos()
    {
        var ejemplos = Bloques("json");

        Assert.Equal(2, ejemplos.Count);                                          // la universidad y el pénsum mínimo
        var universidad = ReglasUniversidadJson.Leer(ejemplos[0]);
        Assert.True(universidad.EsValido, string.Join(" | ", universidad.Errores));
        Assert.Equal("uni-de-ejemplo", universidad.Reglas!.Id);
        var pensum = PensumJson.Leer(ejemplos[1]);
        Assert.True(pensum.EsValido, string.Join(" | ", pensum.Errores));
        Assert.Equal(("uni-de-ejemplo", "derecho", "2022"), (pensum.Definicion!.Universidad, pensum.Definicion.Carrera, pensum.Definicion.Version));
    }

    [Fact]
    public void ElEjemploDeCsvDeLaGuiaSeLee()
    {
        var csv = Bloques("text").Single(b => b.StartsWith("codigo,nombre"));

        var r = PensumCsvParser.Parse(csv);

        Assert.True(r.EsValido, string.Join(" | ", r.Errores));
        Assert.Equal(new[] { "ESP101", "ESP106", "E077" }, r.Materias.Select(m => m.Codigo));
    }

    [Fact]
    public void ElComandoConvertirDeLaGuiaUsaLasOpcionesReales()
    {
        var comando = Bloques("text").Single(b => b.StartsWith("dotnet run --project HistorialAcademico.Validador -- convertir"));

        foreach (var opcion in new[] { "--universidad", "--carrera", "--nombre", "--version" }) Assert.Contains(opcion, comando);
        Assert.Contains("mi-plan.csv", comando);
        // Y ese mismo comando funciona con un CSV como el del ejemplo.
        var dir = Path.Combine(Path.GetTempPath(), "guia-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "mi-plan.csv"), Bloques("text").Single(b => b.StartsWith("codigo,nombre")));
            var args = new[] { "convertir", "mi-plan.csv", "--universidad", "uni-de-ejemplo", "--carrera", "derecho", "--nombre", "Derecho", "--version", "2022" };

            Assert.Equal(Comandos.Bien, Comandos.Ejecutar(args, new StringWriter(), new StringWriter(), dir));
            Assert.True(File.Exists(Path.Combine(dir, "pensums", "uni-de-ejemplo", "derecho-2022.json")));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void LaSalidaDeEjemploDeLaGuiaCoincideConLaRealParaUnapec()
    {
        var real = new StringWriter();
        Comandos.Ejecutar(new[] { "validar", PensumEjemplo.CarpetaPensums }, real, new StringWriter());
        var guia = Guia;

        foreach (var linea in real.ToString().Split('\n').Where(l => l.StartsWith("OK ")).Select(l => l.TrimEnd()))
            Assert.Contains(linea, guia);
    }

    [Fact]
    public void LaGuiaSoloUsaLosComandosDelValidadorQueExisten()
    {
        var comandos = Regex.Matches(Guia, @"HistorialAcademico\.Validador -- (\w+)").Select(m => m.Groups[1].Value).Distinct().ToList();

        Assert.Equal(new[] { "convertir", "validar" }, comandos.OrderBy(c => c));
        Assert.True(File.Exists(Ruta("HistorialAcademico.Validador", "HistorialAcademico.Validador.csproj")));
    }

    [Fact]
    public void LosEnlacesRelativosQueApuntanALaGuiaExisten()
    {
        Assert.Contains("../CONTRIBUTING-pensums.md", File.ReadAllText(Ruta("pensums", "README.md")));
        Assert.Contains("../CONTRIBUTING-pensums.md", File.ReadAllText(Ruta(".github", "pull_request_template.md")));
        Assert.True(File.Exists(Path.GetFullPath(Path.Combine(Ruta("pensums"), "..", "CONTRIBUTING-pensums.md"))));
        Assert.True(File.Exists(Path.GetFullPath(Path.Combine(Ruta(".github"), "..", "CONTRIBUTING-pensums.md"))));
    }

    [Fact]
    public void LaGuiaNoTrae_DatosPersonales()
    {
        var guia = Guia;

        Assert.DoesNotContain(Environment.UserName, guia, StringComparison.OrdinalIgnoreCase);   // el usuario de quien la escribió
        Assert.DoesNotMatch(@"[\w.+-]+@[\w-]+\.\w+", guia);   // ningún correo
        Assert.DoesNotMatch(@"\b\d{8,}\b", guia);          // ninguna matrícula ni documento
    }

    // ── La plantilla del Pull Request ─────────────────────────────────────────────────────

    [Fact]
    public void LaPlantillaDelPullRequestPideLoImportante()
    {
        var plantilla = File.ReadAllText(Ruta(".github", "pull_request_template.md"));

        Assert.Contains("De dónde sale el plan", plantilla);
        Assert.Contains("No incluí datos personales", plantilla);
        Assert.Contains("dotnet run --project HistorialAcademico.Validador -- validar", plantilla);
        Assert.Contains("pensums/<universidad>/<carrera>-<versión>.json", plantilla);
    }

    // ── La GitHub Action ──────────────────────────────────────────────────────────────────

    [Fact]
    public void LaActionSeEjecutaEnLosPullRequestsQueTocanLosPensums()
    {
        var flujo = Flujo;

        Assert.Contains("on:\n  pull_request:", flujo.Replace("\r\n", "\n"));
        Assert.Contains("- 'pensums/**'", flujo);
        Assert.Contains("workflow_dispatch:", flujo);
        Assert.Contains("permissions:\n  contents: read", flujo.Replace("\r\n", "\n"));   // solo lectura: no puede escribir en el repositorio
    }

    [Fact]
    public void LaActionValidaContraLosDosEsquemasYConLaHerramientaDelProyecto()
    {
        var flujo = Flujo;

        Assert.Contains("-s pensums/schema.json", flujo);
        Assert.Contains("-s pensums/universidad.schema.json", flujo);
        Assert.Contains("ajv-cli@5.0.0", flujo);                    // versión fija: no cambia sola
        Assert.Contains("--spec=draft2020", flujo);                 // los esquemas son de la versión 2020-12
        Assert.Contains("dotnet run --project HistorialAcademico.Validador --configuration Release -- validar pensums", flujo);
        Assert.Contains("actions/checkout@v4", flujo);
        Assert.Contains("actions/setup-dotnet@v4", flujo);
    }

    [Fact]
    public void TodoLoQueLaActionNombraExiste()
    {
        var flujo = Flujo;

        foreach (var ruta in new[] { "pensums/schema.json", "pensums/universidad.schema.json", "HistorialAcademico.Validador/HistorialAcademico.Validador.csproj" })
            Assert.True(File.Exists(Ruta(ruta.Split('/'))), $"La Action usa {ruta}, que no existe");

        // Las carpetas cuyo cambio dispara la Action.
        foreach (var carpeta in Regex.Matches(flujo, @"- '([^*']+)/\*\*'").Select(m => m.Groups[1].Value))
            Assert.True(Directory.Exists(Ruta(carpeta.Split('/'))), $"La Action vigila {carpeta}, que no existe");
    }

    [Fact]
    public void ElComandoDeLaActionSalePorCeroConLaCarpetaDelRepositorio()
    {
        // Es exactamente lo que ejecuta la Action tras instalar .NET.
        Assert.Equal(Comandos.Bien, Comandos.Ejecutar(new[] { "validar", "pensums" }, new StringWriter(), new StringWriter(), PensumEjemplo.Raiz));
    }

    [Fact]
    public void ElArchivoDeLaActionNoTieneTabuladoresNiCredenciales()
    {
        var flujo = Flujo;

        Assert.DoesNotContain('\t', flujo);                               // YAML no admite tabuladores
        Assert.DoesNotContain("secrets.", flujo);
        Assert.DoesNotContain("GITHUB_TOKEN", flujo);
        Assert.DoesNotContain("pull_request_target", flujo);              // ese evento correría código de forks con permisos
    }
}

/// <summary>El convertidor de Markdown sencillo que muestra la guía dentro de la aplicación.</summary>
public class MarkdownSencilloTests
{
    private static string Html(string? md) => MarkdownSencillo.AHtml(md);

    [Fact]
    public void LosTitulosBajanUnNivelPorqueLaPaginaYaTieneSuH1()
    {
        var html = Html("# Uno\n## Dos\n### Tres\n#### Cuatro");

        Assert.Contains("<h2>Uno</h2>", html);
        Assert.Contains("<h3>Dos</h3>", html);
        Assert.Contains("<h4>Tres</h4>", html);
        Assert.Contains("<h5>Cuatro</h5>", html);
    }

    [Fact]
    public void LasLineasSeguidasSonUnParrafoYUnaLineaEnBlancoLosSepara()
    {
        var html = Html("primera línea\nsegunda línea\n\notro párrafo");

        Assert.Contains("<p>primera línea segunda línea</p>", html);
        Assert.Contains("<p>otro párrafo</p>", html);
    }

    [Fact]
    public void NegritaCodigoYEnlaces()
    {
        var html = Html("Esto es **importante**, usa `--forzar` y mira [la guía](https://ejemplo.org/x?a=1&b=2).");

        Assert.Contains("<strong>importante</strong>", html);
        Assert.Contains("<code>--forzar</code>", html);
        Assert.Contains("<a href=\"https://ejemplo.org/x?a=1&amp;b=2\" target=\"_blank\" rel=\"noopener noreferrer\">la guía</a>", html);
    }

    [Fact]
    public void LoQueVaEntreComillasInvertidasNoSeProcesa()
    {
        var html = Html("mira `**no negrita** [no](enlace)` y **sí negrita**");

        Assert.Contains("<code>**no negrita** [no](enlace)</code>", html);
        Assert.Contains("<strong>sí negrita</strong>", html);
    }

    [Theory]
    [InlineData("<script>alert(1)</script>", "&lt;script&gt;alert(1)&lt;/script&gt;")]
    [InlineData("<img src=x onerror=alert(1)>", "&lt;img src=x onerror=alert(1)&gt;")]
    [InlineData("**<b>negrita</b>**", "<strong>&lt;b&gt;negrita&lt;/b&gt;</strong>")]
    [InlineData("`<i>código</i>`", "<code>&lt;i&gt;código&lt;/i&gt;</code>")]
    public void ElHtmlDelArchivoNuncaPasaSinCodificar(string md, string esperado)
    {
        var html = Html(md);

        Assert.Contains(esperado, html);
        Assert.DoesNotContain("<script", html);
        Assert.DoesNotContain("<img", html);
    }

    [Theory]
    [InlineData("[clic](javascript:alert(1))")]
    [InlineData("[clic](JavaScript:alert(1))")]
    [InlineData("[clic](data:text/html;base64,AAAA)")]
    [InlineData("[clic](//sitio-malo.example/x)")]
    [InlineData("[clic](vbscript:x)")]
    public void UnEnlaceConEsquemaPeligrosoSeQuedaSoloConElTexto(string md)
    {
        var html = Html(md);

        Assert.DoesNotContain("<a ", html);
        Assert.DoesNotContain("href", html);
        Assert.Contains("clic", html);
    }

    [Theory]
    [InlineData("[interno](#paso-1)", "href=\"#paso-1\"")]
    [InlineData("[relativo](../CONTRIBUTING-pensums.md)", "href=\"../CONTRIBUTING-pensums.md\"")]
    [InlineData("[web](http://ejemplo.org)", "href=\"http://ejemplo.org\"")]
    public void LosEnlacesInternosRelativosYWebSePermiten(string md, string esperado) => Assert.Contains(esperado, Html(md));

    [Fact]
    public void ListasConVinetasYNumeradas()
    {
        var html = Html("- uno\n- dos con `codigo`\n\n1. primero\n2. segundo");

        Assert.Contains("<ul>\n<li>uno</li>\n<li>dos con <code>codigo</code></li>\n</ul>", html.Replace("\r\n", "\n"));
        Assert.Contains("<ol>\n<li>primero</li>\n<li>segundo</li>\n</ol>", html.Replace("\r\n", "\n"));
    }

    [Fact]
    public void UnaLineaEnBlancoEntreElementosNoCortaLaLista()
    {
        var html = Html("1. uno\n\n2. dos\n\n3. tres");

        Assert.Single(Regex.Matches(html, "<ol>"));
        Assert.Equal(3, Regex.Matches(html, "<li>").Count);
    }

    [Fact]
    public void UnElementoPuedeTenerViñetasAnidadasYBloquesDeCodigo()
    {
        var md = "1. Abre el PR y escribe:\n   - la universidad\n   - el plan\n\n2. Ejecuta:\n\n   ```text\n   git push\n   ```\n\n3. Listo";

        var html = Html(md);

        Assert.Single(Regex.Matches(html, "<ol>"));
        Assert.Contains("<li>Abre el PR y escribe:<ul><li>la universidad</li><li>el plan</li></ul></li>", html);
        Assert.Contains("<li>Ejecuta:<pre class=\"guia-codigo\"><code>git push</code></pre></li>", html);
        Assert.Contains("<li>Listo</li>", html);
    }

    [Fact]
    public void LosBloquesDeCodigoConservanElTextoCodificado()
    {
        var html = Html("```json\n{ \"a\": \"<b>\" }\n  sangría\n```");

        Assert.Contains("<pre class=\"guia-codigo\"><code>{ &quot;a&quot;: &quot;&lt;b&gt;&quot; }\n  sangría</code></pre>", html);
    }

    [Fact]
    public void UnBloqueSinCerrarLlegaHastaElFinalSinRomperNada()
    {
        var html = Html("```text\nsin cierre\notra línea");

        Assert.Contains("<pre class=\"guia-codigo\"><code>sin cierre\notra línea</code></pre>", html);
    }

    [Fact]
    public void LasTablasTienenEncabezadoYFilas()
    {
        var html = Html("| Propiedad | Qué poner |\n| --- | --- |\n| `id` | El nombre |\n| `x` | con **negrita** |");

        Assert.Contains("<table class=\"table table-sm guia-tabla\">", html);
        Assert.Contains("<thead><tr><th>Propiedad</th><th>Qué poner</th></tr></thead>", html);
        Assert.Contains("<tr><td><code>id</code></td><td>El nombre</td></tr>", html);
        Assert.Contains("<td>con <strong>negrita</strong></td>", html);
    }

    [Fact]
    public void UnaBarraDentroDeCodigoNoPartelaCelda()
    {
        var html = Html("| A | B |\n| --- | --- |\n| `a | b` | c |");

        Assert.Contains("<td><code>a | b</code></td><td>c</td>", html);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n\n  ")]
    public void UnTextoVacioDaHtmlVacio(string? md) => Assert.Equal("", Html(md).Trim());

    [Fact]
    public void LaGuiaRealSeConvierteSinDejarMarcasSinProcesar()
    {
        var html = Html(File.ReadAllText(Path.Combine(PensumEjemplo.Raiz, "CONTRIBUTING-pensums.md")));

        Assert.Contains("<h2>Cómo agregar tu carrera (un pénsum nuevo)</h2>", html);
        Assert.Contains("<h3>Paso 4: abre el Pull Request</h3>", html);
        Assert.Equal(3, Regex.Matches(html, "<table").Count);                 // universidad, propiedades del pénsum y de las materias
        Assert.True(Regex.Matches(html, "<pre class=\"guia-codigo\">").Count >= 8);
        Assert.DoesNotContain("```", html);                       // ningún bloque de código quedó sin convertir
        Assert.DoesNotContain("**", html);                        // ni negritas
        Assert.DoesNotContain("| ---", html);                     // ni tablas
        Assert.DoesNotContain("<script", html);
    }
}
