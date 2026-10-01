using HistorialAcademico.Web.Perfiles;
using Microsoft.Extensions.DependencyInjection;

namespace HistorialAcademico.Tests;

/// <summary>La aplicación con un catálogo de pénsums temporal (UNAPEC y «Universidad de Prueba»), para elegir universidad sin tocar el del repositorio.</summary>
public sealed class AppConCatalogoFactory : AppConDatosFactory
{
    private readonly CatalogoTemporal _catalogo = new();
    protected override string? CarpetaPensums => _catalogo.Carpeta;

    public void ElegirUniversidad(string id)
    {
        using var scope = Services.CreateScope();
        Services.GetRequiredService<GestorPerfiles>().GuardarUniversidad(scope.ServiceProvider.GetRequiredService<PerfilActual>(), id);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _catalogo.Dispose();
    }
}

/// <summary>«Datos Personales» dice la universidad que la persona eligió, no una escrita a mano en la pantalla.</summary>
public class DatosPersonalesUniversidadTests
{
    private static async Task<string> DatosAsync(AppConCatalogoFactory app)
    {
        await app.InitializeAsync();
        return (await app.GetAsync("/Estudiante/DatosPersonales")).Html;
    }

    [Fact]
    public async Task SinUniversidadElegidaNoSeInventaNinguna()
    {
        using var app = new AppConCatalogoFactory();

        var html = await DatosAsync(app);

        Assert.Contains("id=\"universidad-elegida\">—</dd>", html);
        Assert.DoesNotContain("UNAPEC", html);
    }

    [Fact]
    public async Task ConUnapecElegidaDiceUnapec()
    {
        using var app = new AppConCatalogoFactory();
        await app.InitializeAsync();
        app.ElegirUniversidad("unapec");

        Assert.Contains("id=\"universidad-elegida\">UNAPEC – Universidad APEC</dd>", (await app.GetAsync("/Estudiante/DatosPersonales")).Html);
    }

    [Fact]
    public async Task ConOtraUniversidadDiceElNombreDeEsaYNoElDeUnapec()
    {
        using var app = new AppConCatalogoFactory();
        await app.InitializeAsync();
        app.ElegirUniversidad("uni-prueba");

        var html = (await app.GetAsync("/Estudiante/DatosPersonales")).Html;

        Assert.Contains("id=\"universidad-elegida\">Universidad de Prueba</dd>", html);
        Assert.DoesNotContain("UNAPEC", html);
    }

    [Fact]
    public async Task SiElArchivoDeLaUniversidadElegidaNoSeEncuentraNoSeDiceQueEsUnapec()
    {
        using var app = new AppConCatalogoFactory();
        await app.InitializeAsync();
        app.ElegirUniversidad("ya-no-existe");   // se usan las reglas incorporadas de UNAPEC, pero esa no es su universidad

        var html = (await app.GetAsync("/Estudiante/DatosPersonales")).Html;

        Assert.Contains("id=\"universidad-elegida\">—</dd>", html);
        Assert.DoesNotContain("UNAPEC", html);
    }
}
