using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using HistorialAcademico.Web.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace HistorialAcademico.Tests;

/// <summary>Un exportador que dice que no hay navegador, para ver el mensaje sin tocar el Chromium de verdad.</summary>
public class ExportadorSinNavegador : ExportadorPlanService
{
    public override Task<byte[]> PdfAsync(string html) => throw new ExportacionNoDisponibleException("Falta el navegador de Playwright. Instálalo con: pwsh playwright.ps1 install chromium");
    public override Task<byte[]> PngAsync(string html) => throw new ExportacionNoDisponibleException("Falta el navegador de Playwright. Instálalo con: pwsh playwright.ps1 install chromium");
}

public class AppSinNavegadorFactory : AppConDatosFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(s => { s.RemoveAll<ExportadorPlanService>(); s.AddSingleton<ExportadorPlanService, ExportadorSinNavegador>(); });
    }
}

/// <summary>Exportar el plan: la hoja para imprimir, el PDF de una página y la imagen.</summary>
public class ExportarPlanTests
{
    private static async Task<string> TokenAsync(HttpClient c) =>
        Regex.Match(await c.GetStringAsync("/Planificador"), "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;

    private static async Task PostAsync(HttpClient c, string url, params (string, string)[] campos)
    {
        var datos = campos.Select(x => new KeyValuePair<string, string>(x.Item1, x.Item2)).Append(new("__RequestVerificationToken", await TokenAsync(c)));
        await c.PostAsync(url, new FormUrlEncodedContent(datos));
    }

    private static async Task<HttpClient> ConPlanAsync(AppConDatosFactory app, string carga = "pesada")
    {
        await app.InitializeAsync();
        var c = app.CreateClient();
        await PostAsync(c, "/Planificador/Generar", ("id", "1"), ("carga", carga));
        return c;
    }

    // ── La hoja imprimible ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LaHojaTraeElResumenYTodasLasMateriasPeriodoPorPeriodoSinMenuNiRecursosDeFuera()
    {
        using var app = new AppConDatosFactory();
        using var c = await ConPlanAsync(app);

        var html = WebUtility.HtmlDecode(await c.GetStringAsync("/Planificador/Imprimible?id=1"));

        Assert.Contains("id=\"hoja\"", html);
        Assert.Contains("Plan de estudios: Plan 1", html);
        Assert.Contains("Graduación según este plan", html);
        Assert.Contains("cuatrimestres planificados", html);
        Assert.Contains("créditos faltantes en total", html);
        Assert.Contains("créditos por período, en promedio", html);
        Assert.Contains("período más pesado", html);
        Assert.Contains("confírmalo con tu asesor académico", html);
        Assert.DoesNotContain("menu-lateral", html);
        Assert.DoesNotContain("Actualizar desde Banner", html);
        Assert.DoesNotMatch(@"<link\b|<script[^>]+src=|src=""http|href=""http|url\(", html);   // nada se carga de fuera: se dibuja sin internet

        using var scope = app.Services.CreateScope();
        var st = await scope.ServiceProvider.GetRequiredService<PlanificadorService>().ObtenerAsync(1);
        foreach (var periodo in st.Plan.Where(p => p.Codigos.Count > 0))
        {
            Assert.Contains(periodo.Periodo.Nombre, html);
            foreach (var codigo in periodo.Codigos) Assert.Contains($"<b>{codigo}</b>", html);
        }
    }

    [Fact]
    public async Task LaHojaMarcaElPlanActivoYSiElPlanEstaVacioLoDice()
    {
        using var app = new AppConDatosFactory();
        using var c = await ConPlanAsync(app);
        await PostAsync(c, "/Planificador/Activar", ("id", "1"));
        Assert.Contains("Plan activo", await c.GetStringAsync("/Planificador/Imprimible?id=1"));

        await PostAsync(c, "/Planificador/Limpiar", ("id", "1"));
        var vacia = await c.GetStringAsync("/Planificador/Imprimible?id=1");

        Assert.Contains("todavía no tiene materias planificadas", vacia);
        Assert.Contains("Sin planificar", vacia);   // y todo lo que falta figura como sin planificar
    }

    [Fact]
    public async Task UnNombreDePlanConHtmlSeEscapa()
    {
        using var app = new AppConDatosFactory();
        using var c = await ConPlanAsync(app);
        await PostAsync(c, "/Planificador/Renombrar", ("id", "1"), ("nombre", "<b>x</b> & <script>alert(1)</script>"));

        var html = await c.GetStringAsync("/Planificador/Imprimible?id=1");

        Assert.DoesNotContain("<script>alert(1)</script>", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public async Task UnPlanConProblemasAvisaEnLaHoja()
    {
        using var app = new AppConDatosFactory();
        await app.InitializeAsync();
        using var c = app.CreateClient();
        await c.GetStringAsync("/Planificador");
        // Una materia con prerrequisitos en el primer período: no cumple sus requisitos.
        string conPrerrequisito;
        using (var scope = app.Services.CreateScope())
        {
            var st = await scope.ServiceProvider.GetRequiredService<PlanificadorService>().ObtenerAsync(1);
            conPrerrequisito = st.PorPlanificar.First(m => !st.Contexto!.EstaDisponible(m.Materia.Codigo, st.Contexto.Aprobadas, st.Contexto.CreditosAprobados)).Materia.Codigo;
            await PostAsync(c, "/Planificador/Asignar", ("id", "1"), ("codigo", conPrerrequisito), ("periodo", st.Contexto!.Primero.Nombre));
        }

        var html = WebUtility.HtmlDecode(await c.GetStringAsync("/Planificador/Imprimible?id=1"));

        Assert.Contains("Ojo:", html);
        Assert.Contains("class=\"mal\"", html);
    }

    [Fact]
    public async Task ElBotonDeExportarEstaEnLaPantallaDelPlan()
    {
        using var app = new AppConDatosFactory();
        using var c = await ConPlanAsync(app);

        var html = WebUtility.HtmlDecode(await c.GetStringAsync("/Planificador"));

        Assert.Contains("Exportar PDF", html);
        Assert.Contains("Exportar imagen", html);
        Assert.Contains("/Planificador/Exportar/1?formato=pdf", html);
        Assert.Contains("/Planificador/Exportar/1?formato=png", html);
        Assert.Contains("/Planificador/Imprimible/1", html);
    }

    [Fact]
    public async Task SinHistoricoLaHojaYLaExportacionExplicanQueFalta()
    {
        using var app = new AppFactory();
        await app.InitializeAsync();
        using var c = app.CreateClient();

        var imprimible = WebUtility.HtmlDecode(await c.GetStringAsync("/Planificador/Imprimible"));
        var exportar = WebUtility.HtmlDecode(await c.GetStringAsync("/Planificador/Exportar?formato=pdf"));

        Assert.Contains("Primero sincroniza tu histórico", imprimible);   // se vuelve a la pantalla del plan con el motivo
        Assert.Contains("Primero sincroniza tu histórico", exportar);
    }

    // ── PDF e imagen ──────────────────────────────────────────────────────────────────────

    private static int Paginas(byte[] pdf) => Regex.Matches(Encoding.Latin1.GetString(pdf), @"/Type\s*/Page(?![s\w])").Count;

    [Fact]
    public async Task ElPdfEsUnSoloArchivoPdfDeUnaPaginaConNombreLegible()
    {
        using var app = new AppConDatosFactory();
        using var c = await ConPlanAsync(app);

        var r = await c.GetAsync("/Planificador/Exportar?id=1&formato=pdf");
        var bytes = await r.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("application/pdf", r.Content.Headers.ContentType!.MediaType);
        Assert.Equal("%PDF-", Encoding.ASCII.GetString(bytes, 0, 5));
        Assert.Equal(1, Paginas(bytes));
        var nombre = (r.Content.Headers.ContentDisposition!.FileNameStar ?? r.Content.Headers.ContentDisposition.FileName)!.Trim('"');
        Assert.Matches(@"^plan-plan-1-\d{4}-\d{2}-\d{2}\.pdf$", nombre);
    }

    [Fact]
    public async Task UnPlanLargoConMuchosPeriodosTambienCabeEnUnaPagina()
    {
        using var app = new AppConDatosFactory();
        using var c = await ConPlanAsync(app, carga: "ligera");

        var bytes = await c.GetByteArrayAsync("/Planificador/Exportar?id=1&formato=pdf");

        Assert.Equal(1, Paginas(bytes));
    }

    [Fact]
    public async Task LaImagenEsUnPngConDobleDetalle()
    {
        using var app = new AppConDatosFactory();
        using var c = await ConPlanAsync(app);

        var r = await c.GetAsync("/Planificador/Exportar?id=1&formato=png");
        var bytes = await r.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("image/png", r.Content.Headers.ContentType!.MediaType);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, bytes.Take(8).ToArray());
        var ancho = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4));
        var alto = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4));
        Assert.Equal(1047 * 2, ancho);   // la hoja mide 1047 px y se dibuja al doble
        Assert.True(alto > 400, $"alto {alto}");
        Assert.Matches(@"^plan-plan-1-\d{4}-\d{2}-\d{2}\.png$", (r.Content.Headers.ContentDisposition!.FileNameStar ?? r.Content.Headers.ContentDisposition.FileName)!.Trim('"'));
    }

    [Fact]
    public async Task UnFormatoDesconocidoDaPdf()
    {
        using var app = new AppConDatosFactory();
        using var c = await ConPlanAsync(app);

        var r = await c.GetAsync("/Planificador/Exportar?id=1&formato=xyz");

        Assert.Equal("application/pdf", r.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task SiFaltaElNavegadorSeExplicaComoInstalarloYNoSeRompeLaPantalla()
    {
        using var app = new AppSinNavegadorFactory();
        using var c = await ConPlanAsync(app);

        var html = WebUtility.HtmlDecode(await c.GetStringAsync("/Planificador/Exportar?id=1&formato=png"));

        Assert.Contains("Falta el navegador de Playwright. Instálalo con:", html);
        Assert.Contains("Planificador de cuatrimestres", html);   // se volvió a la pantalla del plan
    }

    // ── El nombre del archivo ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Carga ligera", "plan-carga-ligera-2027-03-01.pdf")]
    [InlineData("Año 2ª opción!", "plan-ano-2-opcion-2027-03-01.pdf")]
    [InlineData("  ---  ", "plan-estudios-2027-03-01.pdf")]
    [InlineData("", "plan-estudios-2027-03-01.pdf")]
    [InlineData("Plan/con\\barras:raras*", "plan-plan-con-barras-raras-2027-03-01.pdf")]
    public void ElNombreDelArchivoNoTieneAcentosNiCaracteresRaros(string plan, string esperado) =>
        Assert.Equal(esperado, ExportadorPlanService.NombreDeArchivo(plan, new DateTime(2027, 3, 1), "pdf"));
}
