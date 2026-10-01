using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Playwright;

namespace HistorialAcademico.Web.Services;

/// <summary>El navegador que dibuja el PDF y la imagen no está instalado (o no arrancó): se explica cómo arreglarlo.</summary>
public sealed class ExportacionNoDisponibleException : Exception
{
    public ExportacionNoDisponibleException(string mensaje, Exception? causa = null) : base(mensaje, causa) { }
}

/// <summary>Convierte una vista Razor en su HTML (para dibujarlo después con el navegador).</summary>
public class VistaATexto
{
    private readonly IRazorViewEngine _motor;
    private readonly ITempDataProvider _tempData;

    public VistaATexto(IRazorViewEngine motor, ITempDataProvider tempData)
    {
        _motor = motor;
        _tempData = tempData;
    }

    public async Task<string> RenderizarAsync(ControllerContext contexto, string vista, object modelo)
    {
        var resultado = _motor.FindView(contexto, vista, isMainPage: false);
        if (!resultado.Success) throw new InvalidOperationException($"No encontré la vista «{vista}»: {string.Join(", ", resultado.SearchedLocations)}");

        using var salida = new StringWriter();
        var datos = new ViewDataDictionary(new Microsoft.AspNetCore.Mvc.ModelBinding.EmptyModelMetadataProvider(), new ModelStateDictionary()) { Model = modelo };
        var vistaContexto = new ViewContext(contexto, resultado.View, datos, new TempDataDictionary(contexto.HttpContext, _tempData), salida, new HtmlHelperOptions());
        await resultado.View.RenderAsync(vistaContexto);
        return salida.ToString();
    }
}

/// <summary>
/// Exporta el plan como PDF (una página) o como imagen PNG. Usa el mismo Chromium que Banner: dibuja el HTML de la hoja imprimible sin
/// abrir ninguna ventana y sin salir a internet (la hoja no carga nada de fuera).
/// </summary>
public class ExportadorPlanService
{
    // A4 apaisado a 96 ppp: 1123 × 794 px; con márgenes de 10 mm (38 px) el papel útil es 1047 × 718 px, que es también el ancho de la hoja.
    private const int AnchoPagina = 1123, AltoPagina = 794, Margen = 38;

    private static async Task<T> ConNavegadorAsync<T>(Func<IBrowser, Task<T>> trabajo)
    {
        try
        {
            using var pw = await Playwright.CreateAsync();
            await using var navegador = await pw.Chromium.LaunchAsync(new() { Headless = true });
            return await trabajo(navegador);
        }
        catch (PlaywrightException ex) when (ex.Message.Contains("Executable doesn't exist", StringComparison.OrdinalIgnoreCase))
        {
            throw new ExportacionNoDisponibleException(HistorialAcademico.Banner.InstaladorNavegador.MensajeFalta, ex);
        }
        catch (PlaywrightException ex)
        {
            throw new ExportacionNoDisponibleException($"El navegador falló al dibujar el plan: {ex.Message.Split('\n')[0].Trim()}", ex);
        }
    }

    /// <summary>Un PDF con el plan en una sola página: si la hoja es más alta que la página, se reduce lo justo para que quepa (no menos de un 30 %).</summary>
    public virtual Task<byte[]> PdfAsync(string html) => ConNavegadorAsync(async navegador =>
    {
        var pagina = await navegador.NewPageAsync(new() { ViewportSize = new() { Width = AnchoPagina, Height = AltoPagina } });
        await pagina.SetContentAsync(html, new() { WaitUntil = WaitUntilState.Load });
        await pagina.EmulateMediaAsync(new() { Media = Media.Print });
        var alto = await pagina.EvaluateAsync<double>("() => document.getElementById('hoja').getBoundingClientRect().height");
        var escala = alto <= AltoPagina - 2 * Margen ? 1.0 : Math.Max(0.3, Math.Floor((AltoPagina - 2 * Margen) / alto * 100) / 100);

        return await pagina.PdfAsync(new()
        {
            Format = "A4", Landscape = true, PrintBackground = true, Scale = (float)escala,
            Margin = new() { Top = "10mm", Bottom = "10mm", Left = "10mm", Right = "10mm" },
        });
    });

    /// <summary>Una imagen PNG (con el doble de detalle, para que se lea al compartirla) de la hoja del plan.</summary>
    public virtual Task<byte[]> PngAsync(string html) => ConNavegadorAsync(async navegador =>
    {
        var contexto = await navegador.NewContextAsync(new() { ViewportSize = new() { Width = AnchoPagina, Height = AltoPagina }, DeviceScaleFactor = 2 });
        var pagina = await contexto.NewPageAsync();
        await pagina.SetContentAsync(html, new() { WaitUntil = WaitUntilState.Load });
        await pagina.EvaluateAsync("() => document.querySelectorAll('.imprimir').forEach(b => b.remove())");   // el botón «Imprimir» es de la pantalla, no del plan
        return await pagina.Locator("#hoja").ScreenshotAsync(new() { Type = ScreenshotType.Png });
    });

    /// <summary>plan-carga-ligera-2027-03-01.pdf: sin acentos ni caracteres que un sistema de archivos no acepte.</summary>
    public static string NombreDeArchivo(string nombrePlan, DateTime fecha, string extension)
    {
        var limpio = new string(nombrePlan.Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
            .Select(c => char.IsAsciiLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-').ToArray());
        while (limpio.Contains("--")) limpio = limpio.Replace("--", "-");
        limpio = limpio.Trim('-');
        return $"plan-{(limpio.Length == 0 ? "estudios" : limpio)}-{fecha:yyyy-MM-dd}.{extension}";
    }
}
