using Microsoft.Playwright;
using Xunit;

namespace HistorialAcademico.Tests;

/// <summary>
/// El HTML real del dashboard y del mapa (con datos de prueba) se abre en un Chromium real, con el CSS y el JavaScript
/// reales, para comprobar el conteo animado y el resaltado de requisitos.
/// </summary>
public class DashboardMapaNavegadorTests : IClassFixture<AppConDatosFactory>, IAsyncLifetime
{
    private readonly AppConDatosFactory _app;
    private ServidorEstatico _servidor = null!;
    private IPlaywright _pw = null!;
    private IBrowser _navegador = null!;

    public DashboardMapaNavegadorTests(AppConDatosFactory app) => _app = app;

    public async Task InitializeAsync()
    {
        using var cliente = _app.CreateClient();
        _servidor = new ServidorEstatico(new Dictionary<string, string>
        {
            ["/inicio.html"] = await cliente.GetStringAsync("/"),
            ["/mapa.html"] = await cliente.GetStringAsync("/Pensum/Mapa"),
        });
        _pw = await Playwright.CreateAsync();
        _navegador = await _pw.Chromium.LaunchAsync(new() { Headless = true });
    }

    public async Task DisposeAsync()
    {
        await _navegador.DisposeAsync();
        _pw.Dispose();
        _servidor.Dispose();
    }

    private async Task<IPage> AbrirAsync(string ruta, ReducedMotion movimiento, string? antes = null)
    {
        var contexto = await _navegador.NewContextAsync(new() { ReducedMotion = movimiento, ViewportSize = new() { Width = 1400, Height = 900 } });
        var pagina = await contexto.NewPageAsync();
        if (antes is not null) await pagina.AddInitScriptAsync(antes);
        await pagina.GotoAsync(_servidor.Direccion(ruta));
        return pagina;
    }

    /// <summary>
    /// Registra cada texto que el JavaScript de la página le asigna al índice animado (ignora el HTML inicial del servidor),
    /// para ver la secuencia completa del conteo.
    /// </summary>
    private const string RegistrarConteo = """
        window.__valores = [];
        const original = Object.getOwnPropertyDescriptor(Node.prototype, 'textContent');
        Object.defineProperty(Node.prototype, 'textContent', {
            configurable: true,
            get() { return original.get.call(this); },
            set(v) {
                if (this.classList && this.classList.contains('contador') && this.dataset.contar === '2.31') window.__valores.push(String(v));
                original.set.call(this, v);
            },
        });
        """;

    private static Task<string[]> Valores(IPage p) => p.EvaluateAsync<string[]>("() => window.__valores");

    // ── Conteo animado ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LosNumerosCuentanDesdeCeroHastaSuValorFinal()
    {
        var p = await AbrirAsync("/inicio.html", ReducedMotion.NoPreference, RegistrarConteo);
        await p.WaitForTimeoutAsync(1500);   // la animación dura 900 ms

        var valores = (await Valores(p)).Select(v => double.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToList();
        Assert.True(valores.Count > 5, "El número no se animó");
        Assert.Equal(0.0, valores[0]);                        // empieza en 0.00
        Assert.Equal(2.31, valores[^1]);                      // termina exactamente en el valor real
        Assert.Equal(valores.OrderBy(v => v), valores);       // sube sin retroceder
        Assert.Contains(valores, v => v > 0 && v < 2.31);     // pasa por valores intermedios

        Assert.Equal("2.31", await p.Locator(".contador[data-contar='2.31']").First.TextContentAsync());
        Assert.Equal("2.31", await p.Locator("span.visually-hidden", new() { HasText = "2.31" }).First.TextContentAsync());
    }

    [Fact]
    public async Task ElPorcentajeDeLaDonaTambienCuentaYTerminaEnSuValor()
    {
        var p = await AbrirAsync("/inicio.html", ReducedMotion.NoPreference);
        var final = await p.Locator("tspan.dona-centro-valor").GetAttributeAsync("data-contar");
        await p.WaitForTimeoutAsync(1500);

        Assert.Equal(double.Parse(final!, System.Globalization.CultureInfo.InvariantCulture).ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + "%",
            await p.Locator("tspan.dona-centro-valor").TextContentAsync());
    }

    [Fact]
    public async Task ConReducirMovimientoLosNumerosSeMuestranFinalesSinAnimar()
    {
        var p = await AbrirAsync("/inicio.html", ReducedMotion.Reduce, RegistrarConteo);
        await p.WaitForTimeoutAsync(400);

        Assert.Empty(await Valores(p));   // ninguna modificación: no hubo conteo
        Assert.Equal("2.31", await p.Locator(".contador[data-contar='2.31']").First.TextContentAsync());
    }

    [Fact]
    public async Task LosNumerosSeVenAunSinJavaScript()
    {
        var contexto = await _navegador.NewContextAsync(new() { JavaScriptEnabled = false });
        var p = await contexto.NewPageAsync();
        await p.GotoAsync(_servidor.Direccion("/inicio.html"));
        Assert.Equal("2.31", await p.Locator(".contador[data-contar='2.31']").First.TextContentAsync());
    }

    // ── Mapa: resaltado de requisitos ─────────────────────────────────────────────────────

    private static async Task<bool> Tiene(IPage p, string codigo, string clase) =>
        await p.EvaluateAsync<bool>("([c, k]) => document.querySelector(`.mapa-card[data-codigo='${c}']`).classList.contains(k)", new object[] { codigo, clase });

    [Fact]
    public async Task AlPasarElCursorSeResaltanLosRequisitosYLoQueDesbloquea()
    {
        var p = await AbrirAsync("/mapa.html", ReducedMotion.NoPreference);
        await p.HoverAsync(".mapa-card[data-codigo='ISO615']");
        await p.WaitForTimeoutAsync(350);

        Assert.True(await Tiene(p, "ISO615", "mapa-actual"));
        Assert.True(await Tiene(p, "INF165", "mapa-requisito"));      // ISO615 requiere INF165
        Assert.True(await Tiene(p, "ISO720", "mapa-desbloquea"));     // y desbloquea ISO720
        Assert.False(await Tiene(p, "ISO100", "mapa-requisito"));
        Assert.True(await p.EvaluateAsync<bool>("() => document.querySelector('.mapa').classList.contains('mapa-enfoque')"));

        // Las demás se atenúan; las relacionadas siguen a plena opacidad.
        Assert.Equal("0.45", await p.EvaluateAsync<string>("() => getComputedStyle(document.querySelector(\".mapa-card[data-codigo='ISO100']\")).opacity"));
        Assert.Equal("1", await p.EvaluateAsync<string>("() => getComputedStyle(document.querySelector(\".mapa-card[data-codigo='INF165']\")).opacity"));

        // No depende solo del color: cada resaltado lleva su etiqueta de texto.
        Assert.Equal("\"Requisito\"", await p.EvaluateAsync<string>("() => getComputedStyle(document.querySelector(\".mapa-card[data-codigo='INF165']\"), '::after').content"));
        Assert.Equal("\"Desbloquea\"", await p.EvaluateAsync<string>("() => getComputedStyle(document.querySelector(\".mapa-card[data-codigo='ISO720']\"), '::after').content"));

        var resumen = await p.Locator("#mapa-resumen").TextContentAsync();
        Assert.Contains("ISO615", resumen);
        Assert.Contains("Requiere: INF165", resumen);
        Assert.Contains("Desbloquea: ISO720", resumen);
    }

    [Fact]
    public async Task AlSalirElCursorSeQuitaElResaltado()
    {
        var p = await AbrirAsync("/mapa.html", ReducedMotion.NoPreference);
        var inicial = await p.Locator("#mapa-resumen").TextContentAsync();
        await p.HoverAsync(".mapa-card[data-codigo='ISO615']");
        await p.Mouse.MoveAsync(5, 5);
        await p.WaitForTimeoutAsync(350);

        Assert.False(await p.EvaluateAsync<bool>("() => document.querySelector('.mapa').classList.contains('mapa-enfoque')"));
        Assert.Equal(0, await p.Locator(".mapa-actual, .mapa-requisito, .mapa-desbloquea").CountAsync());
        Assert.Equal(inicial!.Trim(), (await p.Locator("#mapa-resumen").TextContentAsync())!.Trim());
    }

    [Fact]
    public async Task ElTecladoTambienResaltaAlEnfocar()
    {
        var p = await AbrirAsync("/mapa.html", ReducedMotion.NoPreference);
        await p.FocusAsync(".mapa-card[data-codigo='E078']");

        Assert.True(await Tiene(p, "E077", "mapa-requisito"));         // E078 requiere E077 (el 67 % no es una materia)
        Assert.True(await Tiene(p, "E079", "mapa-desbloquea"));
        Assert.Contains("Requiere: E077 + 67% de créditos", await p.Locator("#mapa-resumen").TextContentAsync());

        await p.EvaluateAsync("() => document.activeElement.blur()");
        Assert.Equal(0, await p.Locator(".mapa-actual, .mapa-requisito, .mapa-desbloquea").CountAsync());
    }

    [Fact]
    public async Task UnaMateriaSinRequisitosNiDependientesLoDiceEnElResumen()
    {
        var p = await AbrirAsync("/mapa.html", ReducedMotion.NoPreference);
        await p.FocusAsync(".mapa-card[data-codigo='TFG']");

        var resumen = await p.Locator("#mapa-resumen").TextContentAsync();
        Assert.Contains("Requiere: ninguno", resumen);
        Assert.Contains("Desbloquea: ninguna materia", resumen);
    }

    [Fact]
    public async Task ElClicSigueAbriendoElDetalleDeLaMateria()
    {
        var p = await AbrirAsync("/mapa.html", ReducedMotion.NoPreference);
        await p.ClickAsync(".mapa-card[data-codigo='ISO615']");
        await p.WaitForSelectorAsync("#modalMateria.show");

        Assert.Contains("ISO615", await p.Locator("#modalTitulo").TextContentAsync());
        Assert.Equal("INF165", (await p.Locator("#modalPrerrequisitos").TextContentAsync())!.Trim());
    }

    [Fact]
    public async Task ConReducirMovimientoElResaltadoSigueFuncionandoSinTransiciones()
    {
        var p = await AbrirAsync("/mapa.html", ReducedMotion.Reduce);
        await p.HoverAsync(".mapa-card[data-codigo='ISO615']");

        Assert.True(await Tiene(p, "INF165", "mapa-requisito"));
        Assert.Equal("0s", await p.EvaluateAsync<string>("() => getComputedStyle(document.querySelector(\".mapa-card[data-codigo='ISO100']\")).transitionDuration"));
    }
}
