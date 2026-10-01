using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace HistorialAcademico.Tests;

/// <summary>Lo que el servidor le entrega al tablero del planificador: filtros, botones «Mover…», destinos y dónde no puede ir cada materia.</summary>
public class TableroPantallaTests
{
    /// <summary>La página del planificador con el escenario vacío (toda la bandeja «Por planificar» llena).</summary>
    private static async Task<string> PaginaConBandejaAsync(AppConDatosFactory app, bool decodificar = true)
    {
        using var c = app.CreateClient();
        var html = await c.GetStringAsync("/Planificador");
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        await c.PostAsync("/Planificador/Limpiar", new FormUrlEncodedContent(new Dictionary<string, string> { ["id"] = "1", ["__RequestVerificationToken"] = token }));
        var pagina = await c.GetStringAsync("/Planificador");
        return decodificar ? WebUtility.HtmlDecode(pagina) : pagina;
    }

    [Fact]
    public async Task LaBandejaTraeFiltrosConSusConteosYCadaTarjetaSuBotonMover()
    {
        using var app = new AppConDatosFactory();
        await app.InitializeAsync();

        var html = await PaginaConBandejaAsync(app);

        var tarjetas = Regex.Matches(html, "<div class=\"plan-card\" draggable=\"true\" data-codigo=\"([^\"]+)\" data-etiquetas=\"([^\"]*)\"").ToList();
        Assert.True(tarjetas.Count > 5);
        Assert.Contains($"Todas ({tarjetas.Count})", html);
        Assert.Contains($"Disponibles ({tarjetas.Count(t => t.Groups[2].Value.Contains("disponible"))})", html);
        Assert.Contains($"Rezagadas ({tarjetas.Count(t => t.Groups[2].Value.Contains("rezagada"))})", html);
        Assert.Contains($"Ruta crítica ({tarjetas.Count(t => t.Groups[2].Value.Contains("ruta"))})", html);
        Assert.Contains("id=\"buscar-bandeja\"", html);
        foreach (var t in tarjetas)
            Assert.Contains($"data-mover data-codigo=\"{t.Groups[1].Value}\" aria-label=\"Mover {t.Groups[1].Value} a un período\"", html);
        // «Disponible» también se dice con una insignia en la tarjeta.
        Assert.Equal(tarjetas.Count(t => t.Groups[2].Value.Contains("disponible")), Regex.Matches(html, "<span class=\"badge bg-success\">Disponible</span>").Count);
    }

    [Fact]
    public async Task CadaColumnaTrae_SuPeriodoSuMotivoSuAvisoYSuBotonMoverAqui()
    {
        using var app = new AppConDatosFactory();
        await app.InitializeAsync();

        var html = await PaginaConBandejaAsync(app);

        var columnas = Regex.Matches(html, "<section class=\"plan-col\" aria-label=\"([^\"]+)\" data-periodo=\"([^\"]+)\"").ToList();
        Assert.True(columnas.Count >= 3);
        Assert.All(columnas, c => Assert.Equal(c.Groups[1].Value, c.Groups[2].Value));
        Assert.Equal(columnas.Count, Regex.Matches(html, "data-motivo hidden").Count);
        Assert.Equal(columnas.Count, Regex.Matches(html, "data-aviso hidden").Count);
        Assert.Equal(columnas.Count, Regex.Matches(html, "data-destino data-periodo=\"[^\"]+\" hidden>Mover aquí").Count);
        Assert.Contains("data-destino data-periodo=\"\" data-bandeja hidden", html);   // para devolver una materia a la bandeja
        Assert.Contains("id=\"plan-estado\"", html);
        Assert.Contains("role=\"status\" aria-live=\"polite\"", html);
        Assert.Contains("id=\"plan-modo\"", html);
    }

    [Fact]
    public async Task LasColocacionesSonUnJsonQueDiceDondeNoPuedeIrCadaMateria()
    {
        using var app = new AppConDatosFactory();
        await app.InitializeAsync();
        var html = await PaginaConBandejaAsync(app, decodificar: false);

        var json = WebUtility.HtmlDecode(Regex.Match(html, "data-colocaciones=\"([^\"]*)\"").Groups[1].Value);
        using var doc = JsonDocument.Parse(json);

        var conBloqueo = doc.RootElement.EnumerateObject().ToList();
        Assert.NotEmpty(conBloqueo);   // con el plan vacío, las materias con prerrequisitos no pueden ir a ninguna columna todavía
        foreach (var materia in conBloqueo)
            foreach (var periodo in materia.Value.EnumerateObject())
            {
                Assert.Matches(@"^(ENE-ABR|MAY-AGO|SEP-DIC) \d{4}$", periodo.Name);
                var motivos = periodo.Value.GetProperty("m");
                Assert.True(motivos.GetArrayLength() > 0 || periodo.Value.TryGetProperty("a", out _));
            }
        // Una materia con prerrequisitos pendientes dice cuál falta.
        Assert.Contains(conBloqueo.SelectMany(m => m.Value.EnumerateObject()).SelectMany(p => p.Value.GetProperty("m").EnumerateArray()),
            motivo => motivo.GetString()!.Contains("no está aprobada ni planificada antes"));
    }

    [Fact]
    public async Task SinNadaPorPlanificarNoHayFiltros()
    {
        using var app = new AppConDatosFactory();
        await app.InitializeAsync();
        using var c = app.CreateClient();
        var html = await c.GetStringAsync("/Planificador");
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        await c.PostAsync("/Planificador/Generar", new FormUrlEncodedContent(new Dictionary<string, string> { ["id"] = "1", ["__RequestVerificationToken"] = token }));

        var pagina = WebUtility.HtmlDecode(await c.GetStringAsync("/Planificador"));

        Assert.DoesNotContain("filtros-bandeja", pagina);
        Assert.Contains("Todas las materias están en el plan", pagina);
        Assert.Contains("data-mover", pagina);   // pero las tarjetas del plan siguen pudiéndose mover
    }
}
