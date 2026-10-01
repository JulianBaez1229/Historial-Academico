using System.Text.RegularExpressions;

namespace HistorialAcademico.Tests;

/// <summary>Los documentos de la comunidad (README, licencia, guías, plantillas): que existan, que sus enlaces funcionen y que no lleven datos personales.</summary>
public class DocumentacionTests
{
    private static string Ruta(params string[] partes) => Path.Combine(new[] { PensumEjemplo.Raiz }.Concat(partes).ToArray());
    private static string Leer(params string[] partes) => File.ReadAllText(Ruta(partes));

    private static readonly string[] Documentos = { "README.md", "CONTRIBUTING.md", "CONTRIBUTING-pensums.md", "CLAUDE.md", Path.Combine("tests", "samples", "README.md"), Path.Combine("pensums", "README.md") };

    // ── README ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ElReadmeTraeLoQueUnEstudianteNecesita()
    {
        var readme = Leer("README.md");

        foreach (var titulo in new[] { "## Qué hace", "## Requisitos", "## Instalación paso a paso", "## Preguntas frecuentes", "## Privacidad", "## Contribuir", "## Licencia" })
            Assert.Contains(titulo, readme);
    }

    [Fact]
    public void ElReadmeDiceQueEsUnProyectoIndependienteYNoAvalado()
    {
        var readme = Leer("README.md");

        Assert.Contains("Proyecto independiente", readme);
        Assert.Contains("No está afiliado", readme);
        Assert.Contains("avalado por ninguna universidad", readme);
    }

    [Fact]
    public void ElReadmeExplicaLaPrivacidadConLoQueLaAplicacionHaceDeVerdad()
    {
        var readme = Leer("README.md");

        Assert.Contains("nunca ve tu contraseña", readme);
        Assert.Contains("%LOCALAPPDATA%\\HistorialAcademico", readme);
        Assert.Contains("borrar", readme);
        Assert.Contains("Aviso de versiones nuevas", readme);              // la única conexión propia se cuenta y se puede apagar
        Assert.Contains("dirección IP", readme);
        Assert.Contains("apagarlo", readme);
    }

    [Fact]
    public void LasCapturasDelReadmeExistenYSonPng()
    {
        var capturas = Regex.Matches(Leer("README.md"), @"\((docs/capturas/[^)]+)\)").Select(m => m.Groups[1].Value).Distinct().ToList();

        Assert.True(capturas.Count >= 4);
        foreach (var captura in capturas)
        {
            var ruta = Ruta(captura.Split('/'));
            Assert.True(File.Exists(ruta), $"Falta {captura}");
            Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, File.ReadAllBytes(ruta).Take(4).ToArray());
        }
    }

    // ── Paleta ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void LaNotaDeLaPaletaEsPropiaNoReclamaNingunaMarcaYCoincideConElTema()
    {
        var nota = Leer("docs", "branding", "paleta.md");
        var tema = Leer("HistorialAcademico.Web", "wwwroot", "css", "tema.css");

        Assert.False(File.Exists(Ruta("docs", "branding", "paleta-unapec.md")));
        Assert.Contains("paleta propia", nota);
        Assert.Contains("no está afiliado, patrocinado ni avalado por ninguna universidad", nota);
        Assert.DoesNotContain("del escudo", nota);
        Assert.DoesNotContain("UNIVERSIDAD APEC", nota);
        Assert.DoesNotContain("escudo de UNAPEC", tema);

        // Cada fila de la tabla (hex y token) coincide con el tema claro de tema.css.
        var filas = Regex.Matches(nota, @"`(#[0-9A-Fa-f]{6})` \| `--(color-[a-z]+)`");
        Assert.Equal(5, filas.Count);
        foreach (Match f in filas)
            Assert.Matches($@"--{f.Groups[2].Value}:\s*{Regex.Escape(f.Groups[1].Value)}\b", tema);
    }

    // ── Licencia ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void LaLicenciaEsMitAnombreDeSuAutor()
    {
        var licencia = Leer("LICENSE");

        Assert.StartsWith("MIT License", licencia);
        Assert.Contains("Copyright (c) 2026 Julian Baez Mena", licencia);
        Assert.Contains("THE SOFTWARE IS PROVIDED \"AS IS\"", licencia);
    }

    // ── Enlaces y datos personales ────────────────────────────────────────────────────────

    [Fact]
    public void LosEnlacesRelativosDeLosDocumentosApuntanAArchivosQueExisten()
    {
        foreach (var documento in Documentos)
        {
            var carpeta = Path.GetDirectoryName(Ruta(documento.Split(Path.DirectorySeparatorChar)))!;
            foreach (Match m in Regex.Matches(Leer(documento.Split(Path.DirectorySeparatorChar)), @"\]\(([^)\s]+)\)"))
            {
                var destino = m.Groups[1].Value.Split('#')[0];
                if (destino.Length == 0 || destino.StartsWith("http", StringComparison.OrdinalIgnoreCase)) continue;
                var completa = Path.GetFullPath(Path.Combine(carpeta, destino));
                Assert.True(File.Exists(completa) || Directory.Exists(completa), $"{documento} enlaza a «{destino}», que no existe");
            }
        }
    }

    [Fact]
    public void LosDocumentosNoTraenCorreosNiElUsuarioDeQuienLosEscribio()
    {
        foreach (var documento in Documentos.Concat(new[] { "LICENSE" }))
        {
            var texto = Leer(documento.Split(Path.DirectorySeparatorChar));
            Assert.DoesNotMatch(@"[\w.+-]*[A-Za-z0-9]@(?!(example|ejemplo|anthropic)\b)[A-Za-z0-9-]+(\.[A-Za-z0-9-]+)*\.[A-Za-z]{2,}", texto);
            Assert.DoesNotContain(Environment.UserName, texto, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotMatch(@"[A-Za-z]:\\Users\\", texto);
        }
    }

    // ── Guías ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ContributingCuentaRamasCommitsYPullRequests()
    {
        var guia = Leer("CONTRIBUTING.md");

        foreach (var titulo in new[] { "## Ramas", "## Commits", "## Pull Requests", "## Antes de nada: privacidad" })
            Assert.Contains(titulo, guia);
        Assert.Contains("git switch -c", guia);
        Assert.Contains("sin advertencias", guia);
        Assert.Contains("CONTRIBUTING-pensums.md", guia);
    }

    [Fact]
    public void ClaudeMdRecuerdaLasReglasDePrivacidadYLosComandos()
    {
        var reglas = Leer("CLAUDE.md");

        Assert.Contains("Nunca", reglas);
        Assert.Contains("contraseña", reglas);
        Assert.Contains("anonimizar", reglas);
        Assert.Contains("git push", reglas);
        Assert.Contains("dotnet test", reglas);
        foreach (var proyecto in new[] { "HistorialAcademico.Core", "HistorialAcademico.Banner", "HistorialAcademico.Web", "HistorialAcademico.Validador", "HistorialAcademico.Tests" })
        {
            Assert.Contains(proyecto, reglas);
            Assert.True(Directory.Exists(Ruta(proyecto)), $"CLAUDE.md describe {proyecto}, que no existe");
        }
    }

    // ── Plantillas de GitHub ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("error.yml", "Reportar un error", "error")]
    [InlineData("pensum-nuevo.yml", "Pedir o aportar un pénsum", "pensum")]
    [InlineData("mejora.yml", "Proponer una mejora", "mejora")]
    public void CadaPlantillaDeIssueEsUnFormularioEnEspanolQueAvisaDeLaPrivacidad(string archivo, string nombre, string etiqueta)
    {
        var plantilla = Leer(".github", "ISSUE_TEMPLATE", archivo).Replace("\r\n", "\n");

        Assert.Contains($"name: {nombre}", plantilla);
        Assert.Contains("\ndescription: ", plantilla);
        Assert.Contains($"labels: [\"{etiqueta}\"]", plantilla);
        Assert.Contains("\nbody:\n", plantilla);
        Assert.Contains("type: checkboxes", plantilla);
        Assert.Contains("datos personales", plantilla);
        Assert.DoesNotContain('\t', plantilla);                        // YAML no admite tabuladores
        Assert.Equal(plantilla.Split('\n').Count(l => l.Contains("id: ")), plantilla.Split('\n').Where(l => l.Contains("id: ")).Distinct().Count());
    }

    [Fact]
    public void LosIssuesEnBlancoEstanDesactivadosParaQueSeUseUnaPlantilla()
    {
        Assert.Contains("blank_issues_enabled: false", Leer(".github", "ISSUE_TEMPLATE", "config.yml"));
    }

    [Fact]
    public void LaPlantillaDePullRequestPideProbarPrivacidadYArquitectura()
    {
        var plantilla = Leer(".github", "pull_request_template.md");

        Assert.Contains("## Cómo lo probé", plantilla);
        Assert.Contains("sin advertencias", plantilla);
        Assert.Contains("No incluí datos personales", plantilla);
        Assert.Contains("CLAUDE.md", plantilla);
        Assert.Contains("CONTRIBUTING.md", plantilla);
    }
}
