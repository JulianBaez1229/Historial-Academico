using System.Net;
using System.Text.RegularExpressions;
using HistorialAcademico.Core.Entities;
using HistorialAcademico.Core.Pensum;
using HistorialAcademico.Web.Helpers;
using Xunit;

namespace HistorialAcademico.Tests;

public class GraficosTests
{
    private static string Dona(params (string clase, decimal valor, string etiqueta)[] s) =>
        WebUtility.HtmlDecode(Graficos.Dona(s.Select(x => new SegmentoDona(x.clase, x.valor, x.etiqueta)).ToList(), 218, "Progreso de prueba").ToString()!);

    private static string Lineas(IReadOnlyList<string> x, params SerieLinea[] series) =>
        WebUtility.HtmlDecode(Graficos.Lineas(x, series, 4, "PGA de prueba").ToString()!);

    [Fact]
    public void LaDonaEsUnaImagenAccesibleConSuDescripcion()
    {
        var svg = Dona(("dona-aprobada", 128, "Aprobados"));
        Assert.Contains("role=\"img\"", svg);
        Assert.Contains("aria-label=\"Progreso de prueba\"", svg);
        Assert.Contains("<title>Progreso de prueba</title>", svg);
    }

    [Fact]
    public void CadaSegmentoOcupaSuFraccionDeLaCircunferencia()
    {
        var svg = Dona(("dona-aprobada", 128, "A"), ("dona-encurso", 20, "E"), ("dona-faltante", 70, "F"));
        Assert.Equal(4, Regex.Matches(svg, "<circle").Count);   // pista + 3 segmentos

        var circunferencia = 2 * Math.PI * 48;
        var segmentos = Regex.Matches(svg, "stroke-dasharray=\"([\\d.]+) ([\\d.]+)\" stroke-dashoffset=\"(-?[\\d.]+)\"")
            .Select(m => (largo: double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
                          resto: double.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture),
                          desfase: double.Parse(m.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture))).ToList();
        Assert.Equal(3, segmentos.Count);

        double acumulado = 0;
        foreach (var (segmento, valor) in segmentos.Zip(new[] { 128.0, 20.0, 70.0 }))
        {
            var esperado = valor / 218 * circunferencia;
            Assert.Equal(esperado, segmento.largo, 1);                         // cada uno ocupa su fracción
            Assert.Equal(circunferencia, segmento.largo + segmento.resto, 1);  // trazo + hueco = circunferencia completa
            Assert.Equal(-acumulado, segmento.desfase, 1);                     // empieza donde termina el anterior
            acumulado += esperado;
        }
        Assert.Equal(58.7, segmentos[0].largo / circunferencia * 100, 1);      // 128 de 218 créditos = 58.7 %
    }

    [Fact]
    public void UnSegmentoVacioNoSeDibujaYUnTotalCeroNoDivide()
    {
        Assert.Single(Regex.Matches(Dona(("dona-encurso", 0, "nada")), "<circle"));   // solo la pista
        var vacia = WebUtility.HtmlDecode(Graficos.Dona(new[] { new SegmentoDona("x", 5, "y") }, 0, "sin total").ToString()!);
        Assert.Single(Regex.Matches(vacia, "<circle"));
    }

    [Fact]
    public void UnSegmentoNuncaPasaDeLaCircunferenciaCompleta()
    {
        var svg = WebUtility.HtmlDecode(Graficos.Dona(new[] { new SegmentoDona("dona-aprobada", 500, "de más") }, 218, "x").ToString()!);
        Assert.Contains("stroke-dasharray=\"301.59 0\"", svg);
    }

    [Fact]
    public void ElTextoCentralSeInsertaTalCual()
    {
        var svg = WebUtility.HtmlDecode(Graficos.Dona(Array.Empty<SegmentoDona>(), 100, "x", "<tspan>58.7%</tspan>").ToString()!);
        Assert.Contains("<text class=\"dona-centro\"", svg);
        Assert.Contains("<tspan>58.7%</tspan>", svg);
    }

    [Fact]
    public void LasLineasDibujanUnPuntoPorPeriodoYUnaLineaPorSerie()
    {
        var x = new[] { "MAY-AGO 2024", "SEP-DIC 2024", "ENE-ABR 2025" };
        var svg = Lineas(x,
            new SerieLinea("linea-periodo", "PGA del período", new decimal?[] { 3.82m, 3.48m, 3.11m }, MostrarValores: true),
            new SerieLinea("linea-acum", "PGA acumulado", new decimal?[] { 3.82m, 3.65m, 3.49m }, Cuadrados: true));

        Assert.Equal(2, Regex.Matches(svg, "<polyline").Count);
        Assert.Equal(3, Regex.Matches(svg, "<circle class=\"marcador linea-periodo\"").Count);   // círculos para una serie…
        Assert.Equal(3, Regex.Matches(svg, "<rect class=\"marcador linea-acum\"").Count);        // …y cuadrados para la otra
        Assert.Equal(3, Regex.Matches(svg, "class=\"lineas-valor\"").Count);                     // solo la serie con valores visibles
        Assert.Contains("MAY-AGO 2024: PGA del período 3.82", svg);                            // cada punto tiene su descripción
        Assert.Contains("<tspan x=", svg);
    }

    [Fact]
    public void ElEjeYVaDeCeroAlMaximoYLosPuntosCaenDentroDelGrafico()
    {
        var svg = Lineas(new[] { "A 1", "B 2" }, new SerieLinea("linea-periodo", "S", new decimal?[] { 0m, 4m }));

        Assert.Equal(5, Regex.Matches(svg, "class=\"lineas-rejilla\"").Count);   // 0, 1, 2, 3 y 4
        foreach (Match m in Regex.Matches(svg, "<circle class=\"marcador[^>]*cy=\"([\\d.]+)\""))
        {
            var y = double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            Assert.InRange(y, 0, 250);    // dentro de la altura del gráfico (250)
        }
        // 4.0 queda arriba (y menor) y 0.0 abajo (y mayor).
        var ys = Regex.Matches(svg, "<circle class=\"marcador[^>]*cy=\"([\\d.]+)\"").Select(m => double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)).ToList();
        Assert.True(ys[0] > ys[1]);
    }

    [Fact]
    public void UnPuntoSinValorSeOmiteYUnaSolaSerieDeUnPuntoNoTraeLinea()
    {
        var svg = Lineas(new[] { "A 1", "B 2", "C 3" }, new SerieLinea("linea-periodo", "S", new decimal?[] { 3m, null, 2m }));
        Assert.Equal(2, Regex.Matches(svg, "<circle class=\"marcador").Count);
        Assert.Single(Regex.Matches(svg, "<polyline"));

        var uno = Lineas(new[] { "A 1" }, new SerieLinea("linea-periodo", "S", new decimal?[] { 3m }));
        Assert.DoesNotContain("<polyline", uno);
        Assert.Contains("<circle class=\"marcador", uno);
    }

    [Fact]
    public void LosTextosSeEscapanParaNoInyectarHtml()
    {
        var svg = Graficos.Lineas(new[] { "<b>x</b> 1" }, new[] { new SerieLinea("linea-periodo", "<script>", new decimal?[] { 3m }) }, 4, "\"><script>alert(1)</script>").ToString()!;
        Assert.DoesNotContain("<script>", svg);
        Assert.DoesNotContain("<b>", svg);
    }

    [Fact]
    public void ElContadorLlevaElValorFinalEscritoYSoloElFinalSeAnunciaALosLectores()
    {
        var html = WebUtility.HtmlDecode(Ui.Contador(3.3373m, 2).ToString()!);
        Assert.Contains("data-contar=\"3.34\"", html);          // separador de datos siempre con punto
        Assert.Contains("data-decimales=\"2\"", html);
        Assert.Contains("aria-hidden=\"true\">3.34</span>", html);   // el que cuenta va oculto a los lectores
        Assert.Contains("<span class=\"visually-hidden\">3.34</span>", html);   // y el final se lee de un texto aparte

        var pct = WebUtility.HtmlDecode(Ui.Contador(58.7m, 1, "%").ToString()!);
        Assert.Contains("data-sufijo=\"%\"", pct);
        Assert.Contains("58.7%", pct);
    }
}

public class PensumGrafoTests
{
    private static readonly ResultadoPensum R = MotorEstadoPensum.Calcular(DatosLab.Pensum(), DatosLab.Cargar().Cursadas, DatosLab.Cargar().EnProgreso, Array.Empty<Equivalencia>());

    [Fact]
    public void LasMateriasQueDesbloqueaSonLosDependientesDirectosPorCuatrimestre()
    {
        Assert.Equal(new[] { "ISO720" }, R.Desbloquea("ISO615"));
        Assert.Equal(new[] { "ISO912" }, R.Desbloquea("ISO725"));
        Assert.Equal(new[] { "E078" }, R.Desbloquea("E077"));
        Assert.Equal(new[] { "ENG002" }, R.Desbloquea("eng001"));    // el código se acepta en minúsculas
        Assert.Empty(R.Desbloquea("TFG"));
        Assert.Empty(R.Desbloquea("NOEXISTE"));
    }

    [Fact]
    public void LosRequisitosSonLosPrerrequisitosDeMateriaSinLosPorcentajes()
    {
        Assert.Equal(new[] { "INF165" }, R.Requisitos("ISO615"));
        Assert.Equal(new[] { "E077" }, R.Requisitos("E078"));         // "E077; 67% créditos aprobados": el porcentaje no es una materia
        Assert.Equal(new[] { "SOC253" }, R.Requisitos("SOC281"));
        Assert.Empty(R.Requisitos("ISO100"));
        Assert.Empty(R.Requisitos("NOEXISTE"));
    }

    [Fact]
    public void ElGrafoEsConsistente_SiAEsRequisitoDeBEntoncesBEstaEnLoQueDesbloqueaA()
    {
        foreach (var m in R.Materias)
            foreach (var previa in R.Requisitos(m.Materia.Codigo))
                Assert.Contains(m.Materia.Codigo, R.Desbloquea(previa));
    }
}

/// <summary>Dashboard y mapa con la aplicación completa (datos sintéticos: 2 períodos, 2 cursos en progreso, pénsum real).</summary>
public class DashboardYMapaPantallasTests : IClassFixture<AppConDatosFactory>
{
    private readonly AppConDatosFactory _app;

    public DashboardYMapaPantallasTests(AppConDatosFactory app) => _app = app;

    private static string Contadores(string html) =>
        string.Join(" | ", Regex.Matches(html, "data-contar=\"([^\"]+)\"[^>]*data-sufijo=\"([^\"]*)\"").Select(m => m.Groups[1].Value + m.Groups[2].Value));

    [Fact]
    public async Task ElDashboardMuestraLasCuatroTarjetasConSusNumeros()
    {
        var (estado, html) = await _app.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, estado);

        foreach (var titulo in new[] { "Índice acumulado", "Créditos aprobados", "Cuatrimestres restantes", "Materias en curso" })
            Assert.Contains(titulo, html);

        // Índice 2.31, aprobados (ENG001 E + ESP101 C + ISO200 B... según el pénsum), total del pénsum 218, 2 cursos en curso.
        Assert.Contains("data-contar=\"2.31\" data-decimales=\"2\"", html);
        Assert.Contains("/ 218", html);                                       // total real del CSV, no 221
        Assert.DoesNotContain("221", html);
        Assert.Matches(@"En curso|Materias en curso", html);
        Assert.Contains("Graduación estimada:", html);
        Assert.Matches(@"data-contar=""2""[^>]*>2</span>\s*<span class=""visually-hidden"">2</span>", html);   // 2 materias en curso
    }

    [Fact]
    public async Task ElDashboardIncluyeLaDonaYElGraficoDeLineasComoImagenesAccesibles()
    {
        var (_, html) = await _app.GetAsync("/");

        Assert.Equal(2, Regex.Matches(html, "role=\"img\"").Count);
        Assert.Contains("aria-label=\"Progreso de la carrera:", html);
        Assert.Contains("aria-label=\"PGA por período:", html);
        Assert.Contains("class=\"dona-segmento dona-aprobada\"", html);
        Assert.Equal(2, Regex.Matches(html, "<polyline").Count);                    // PGA del período y acumulado
        Assert.Equal(2, Regex.Matches(html, "<circle class=\"marcador").Count);     // un punto por cada uno de los 2 períodos
        Assert.Equal(2, Regex.Matches(html, "<rect class=\"marcador").Count);

        // Alternativa en tabla y leyendas con texto: los datos no dependen solo del color.
        Assert.Contains("Ver los datos en una tabla", html);
        Assert.Contains("ENE-ABR 2025", html);
        Assert.Contains("Aprobados", html);
        Assert.Contains("Faltantes", html);
    }

    [Fact]
    public async Task ElDashboardNoRompeSinDatosNiSinPenum()
    {
        using var vacia = new AppFactory();
        await vacia.InitializeAsync();
        var (estado, html) = await vacia.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, estado);
        Assert.Contains("Todavía no hay datos", html);
        Assert.DoesNotContain("role=\"img\"", html);        // sin datos no hay gráficos
        Assert.DoesNotContain("data-contar", html);
        Assert.Contains("Ir a…", html);                     // los atajos siguen
    }

    [Fact]
    public async Task CadaTarjetaDelMapaTieneIconoDeEstadoYNombreAccesible()
    {
        var (_, html) = await _app.GetAsync("/Pensum/Mapa");
        var tarjetas = Regex.Matches(html, "<button type=\"button\" class=\"mapa-card [^>]*>(.*?)</button>", RegexOptions.Singleline).ToList();

        Assert.Equal(75, tarjetas.Count);
        Assert.All(tarjetas, t =>
        {
            Assert.Contains("<svg class=\"icono icono-estado\"", t.Value);                  // ícono propio de cada estado
            Assert.Matches(@"aria-label=""[A-Z0-9]+, .+, \d+ créditos, (Aprobada|Exenta|En curso|Disponible|Bloqueada)""", t.Value);
        });

        // El ícono cambia con el estado (no solo el color).
        string IconoDe(string codigo) => Regex.Match(html, $"data-codigo=\"{codigo}\".*?<path d=\"([^\"]+)\"", RegexOptions.Singleline).Groups[1].Value;
        var iconos = new[] { IconoDe("ENG001"), IconoDe("ESP101"), IconoDe("ISO400"), IconoDe("ISO100"), IconoDe("ISO800") };   // exenta, aprobada, en curso, disponible, bloqueada
        Assert.Equal(5, iconos.Distinct().Count());
    }

    [Fact]
    public async Task LaLeyendaUsaLosMismosIconosQueLasTarjetas()
    {
        var (_, html) = await _app.GetAsync("/Pensum/Mapa");
        var leyenda = html[html.IndexOf("class=\"leyenda", StringComparison.Ordinal)..html.IndexOf("id=\"mapa-resumen\"", StringComparison.Ordinal)];

        Assert.Equal(5, Regex.Matches(leyenda, "leyenda-estado").Count);
        Assert.Equal(5, Regex.Matches(leyenda, "<svg class=\"icono icono-estado\"").Count);
        foreach (var estado in new[] { "Aprobada", "Exenta", "En curso", "Disponible", "Bloqueada" }) Assert.Contains(estado, leyenda);
    }

    [Fact]
    public async Task LasTarjetasLlevanSusRequisitosYLoQueDesbloquean()
    {
        var (_, html) = await _app.GetAsync("/Pensum/Mapa");

        string Atributo(string codigo, string nombre) =>
            Regex.Match(html, $"data-codigo=\"{codigo}\".*?data-{nombre}=\"([^\"]*)\"", RegexOptions.Singleline).Groups[1].Value;

        Assert.Equal("INF165", Atributo("ISO615", "prereq"));
        Assert.Equal("ISO720", Atributo("ISO615", "desbloquea"));
        Assert.Equal("E077", Atributo("E078", "prereq"));       // el 67 % no es una materia
        Assert.Equal("E079", Atributo("E078", "desbloquea"));
        Assert.Equal("", Atributo("ISO100", "prereq"));
        Assert.Contains("id=\"mapa-resumen\"", html);
        Assert.Contains("aria-live=\"polite\"", html);          // el resaltado también se anuncia
    }
}
