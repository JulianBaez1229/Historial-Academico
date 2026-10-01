using System.Text.RegularExpressions;
using HistorialAcademico.Core.Pensum;
using HistorialAcademico.Web.Data;
using HistorialAcademico.Web.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Xunit;

namespace HistorialAcademico.Tests;

/// <summary>Solo corre con la variable de entorno HA_CAPTURAS=1: no es una prueba, es el generador de las capturas del README.</summary>
public sealed class CapturasFactAttribute : FactAttribute
{
    public CapturasFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("HA_CAPTURAS") != "1")
            Skip = "Generador de capturas del README: se ejecuta con HA_CAPTURAS=1 (ver CONTRIBUTING.md).";
    }
}

/// <summary>La aplicación con el histórico anonimizado, el pénsum del CSV y un plan generado: lo que se ve en las capturas es ficticio.</summary>
public sealed class AppParaCapturasFactory : AppFactory
{
    public async Task PrepararAsync()
    {
        _ = Server;
        using var scope = Services.CreateScope();
        var r = await scope.ServiceProvider.GetRequiredService<SincronizacionService>().AplicarHtmlAsync(Muestras.LeerAnonimizado());
        Assert.True(r.Exito, r.Mensaje);
        var db = scope.ServiceProvider.GetRequiredService<HistorialContext>();
        db.MateriasPensum.AddRange(DatosLab.Pensum());
        db.Equivalencias.AddRange(EquivalenciasIniciales.Valores);
        await db.SaveChangesAsync();
    }
}

/// <summary>
/// Genera docs/capturas/*.png dibujando la aplicación real (con datos ficticios) en Chromium. Las peticiones del navegador se
/// atienden directamente con el servidor de pruebas: no se abre ningún puerto ni se sale a internet.
/// </summary>
public class CapturasReadmeTests
{
    private static string Salida
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && dir.GetFiles("*.sln").Length == 0) dir = dir.Parent;
            return Path.Combine(dir!.FullName, "docs", "capturas");
        }
    }

    [CapturasFact]
    public async Task GeneraLasCapturasDelReadme()
    {
        using var app = new AppParaCapturasFactory();
        await app.PrepararAsync();
        using var http = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var pagina = await http.GetStringAsync("/Planificador");
        var token = Regex.Match(pagina, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        var generar = await http.PostAsync("/Planificador/Generar", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["id"] = "1", ["carga"] = "normal", ["__RequestVerificationToken"] = token,
        }));
        Assert.True((int)generar.StatusCode < 400);

        using var pw = await Playwright.CreateAsync();
        await using var navegador = await pw.Chromium.LaunchAsync(new() { Headless = true });
        var contexto = await navegador.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 1280, Height = 800 }, ColorScheme = ColorScheme.Light, Locale = "es-DO",
            ReducedMotion = ReducedMotion.Reduce,   // las cifras del inicio no se animan: la captura muestra los valores finales
        });
        await contexto.RouteAsync("http://app.local/**", async ruta =>
        {
            var url = new Uri(ruta.Request.Url);
            var respuesta = await http.GetAsync(url.PathAndQuery);
            var tipo = respuesta.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";
            await ruta.FulfillAsync(new() { Status = (int)respuesta.StatusCode, ContentType = tipo, BodyBytes = await respuesta.Content.ReadAsByteArrayAsync() });
        });
        var pestana = await contexto.NewPageAsync();

        Directory.CreateDirectory(Salida);
        foreach (var (ruta, archivo) in new[]
        {
            ("/", "inicio.png"),
            ("/Estudiante/MateriasFaltantes", "materias-faltantes.png"),
            ("/Pensum/Mapa", "mapa-del-pensum.png"),
            ("/Planificador", "planificador.png"),
        })
        {
            await pestana.GotoAsync("http://app.local" + ruta, new() { WaitUntil = WaitUntilState.Load });
            await pestana.WaitForTimeoutAsync(300);
            await pestana.ScreenshotAsync(new() { Path = Path.Combine(Salida, archivo), Type = ScreenshotType.Png });
        }
    }
}
