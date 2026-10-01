using System.Net;
using HistorialAcademico.Core.Entities;
using HistorialAcademico.Web.Data;
using Microsoft.Extensions.DependencyInjection;

namespace HistorialAcademico.Tests;

/// <summary>La pantalla de equivalencias con las que declara el pénsum y las personales, y la convalidación en «Carrera y pénsum».</summary>
public class EquivalenciasPantallaTests
{
    private static async Task<AppConDatosFactory> NuevaAppAsync()
    {
        var app = new AppConDatosFactory();
        await app.InitializeAsync();   // al crearla a mano hay que llamar a InitializeAsync (carga los datos de prueba)
        return app;
    }

    private static Task ActivarUnapecAsync(AppConDatosFactory app) =>
        ClienteCarrera.PostAsync(app, "/Carrera/Activar", ("clave", "unapec/ingenieria-software-11.json"));

    [Fact]
    public async Task SinPensumDelCatalogoDiceQueNoTraeEquivalenciasPropiasYYaNoOfreceCargarValoresIniciales()
    {
        using var app = await NuevaAppAsync();

        var (estado, html) = await app.GetAsync("/Equivalencias");

        Assert.Equal(HttpStatusCode.OK, estado);
        Assert.Contains("id=\"declaradas\"", html);
        Assert.Contains("id=\"sin-pensum-activo\"", html);
        Assert.Contains("Tu pénsum se cargó a mano, así que no trae equivalencias propias", html);
        Assert.DoesNotContain("Cargar valores iniciales", html);
        Assert.DoesNotContain("id=\"tabla-declaradas\"", html);
        Assert.Contains("ING701", html);                       // las personales siguen ahí
    }

    [Fact]
    public async Task ConElPensumDelCatalogoActivoSeVenLasEquivalenciasQueDeclara()
    {
        using var app = await NuevaAppAsync();
        await ActivarUnapecAsync(app);

        var (_, html) = await app.GetAsync("/Equivalencias");

        Assert.Contains("id=\"tabla-declaradas\"", html);
        Assert.Contains("Equivalencias de tu pénsum", html);
        Assert.Contains("Las declara el pénsum de <strong>Ingeniería de Software</strong> (plan 11)", html);
        var declaradas = html[html.IndexOf("id=\"tabla-declaradas\"", StringComparison.Ordinal)..];
        Assert.Contains("ING701", declaradas);
        Assert.Contains("ING716", declaradas);
        Assert.Contains("ING717", declaradas);
        Assert.Contains("Física I y Laboratorio del plan anterior.", declaradas);
        Assert.Contains("<em>sin equivalente</em>", declaradas);          // ESP102 y MAT126
        Assert.DoesNotContain("id=\"sin-pensum-activo\"", html);
    }

    [Fact]
    public async Task LasPersonalesQueElPensumYaDeclaraSeMarcanComoRepetidas()
    {
        using var app = await NuevaAppAsync();   // la base de prueba trae las iniciales como personales
        await ActivarUnapecAsync(app);

        var (_, html) = await app.GetAsync("/Equivalencias");

        Assert.Equal(4, System.Text.RegularExpressions.Regex.Matches(html, "data-aviso=\"ya-declarada\"").Count);   // ING701 ×2, ESP102 y MAT126
        Assert.Contains("Tu pénsum ya declara esta equivalencia; puedes borrar la tuya.", html);
        Assert.DoesNotContain("data-aviso=\"sin-efecto\"", html);
    }

    [Fact]
    public async Task UnaPersonalQueApuntaAUnaMateriaQueYaNoExisteSeMarcaSinEfecto()
    {
        using var app = await NuevaAppAsync();
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HistorialContext>();
            db.Equivalencias.Add(new Equivalencia { CodigoBanner = "OLD001", CodigoPensum = "ZZZ999", Nota = "de otro plan" });
            await db.SaveChangesAsync();
        }

        var (_, html) = await app.GetAsync("/Equivalencias");

        Assert.Single(System.Text.RegularExpressions.Regex.Matches(html, "data-aviso=\"sin-efecto\""));
        Assert.Contains("Sin efecto: ZZZ999 no está en el pénsum actual.", html);
    }

    [Fact]
    public async Task SinPensumCargadoNoSeMarcaNadaComoSinEfecto()
    {
        using var app = new AppFactory();   // sin datos: no hay pénsum cargado
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HistorialContext>();
            db.Equivalencias.Add(new Equivalencia { CodigoBanner = "OLD001", CodigoPensum = "ZZZ999" });
            await db.SaveChangesAsync();
        }

        var (_, html) = await app.GetAsync("/Equivalencias");

        Assert.DoesNotContain("data-aviso=\"sin-efecto\"", html);
    }

    [Fact]
    public async Task LaListaDeCarrerasMuestraCuantoCubreTuHistoricoYQueMateriasSeConvalidan()
    {
        using var app = await NuevaAppAsync();

        var (_, html) = await app.GetAsync("/Carrera");

        Assert.Contains("<th>Con tu histórico</th>", html);
        Assert.Matches("<strong>\\d+</strong> de 218 créditos \\(\\d+[.,]\\d%\\)", html);
        Assert.Contains("Se convalidan", html);
        Assert.Contains("<summary class=\"small\">", html);
    }

    [Fact]
    public async Task ElPensumDeUnapecConvalidaPorEquivalenciaLoDelPlanAnteriorDelHistorico()
    {
        using var app = await NuevaAppAsync();
        await ActivarUnapecAsync(app);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HistorialContext>();
            // Un histórico con una materia del plan anterior (ING701 = Física I y Laboratorio) que no está en el histórico sintético.
            if (!db.MateriasCursadas.Any(m => m.Codigo == "ING701"))
            {
                db.Periodos.Add(new Periodo { Nombre = "ENE-ABR 2024", Orden = 0, Materias = new() { new MateriaCursada { Codigo = "ING701", Calificacion = "B", HorasCredito = 4 } } });
                await db.SaveChangesAsync();
            }
        }

        var (_, html) = await app.GetAsync("/Carrera");

        Assert.Contains("ING716", html);
        Assert.Contains("por equivalencia con ING701", html);
        Assert.Matches("Se convalidan \\d+ materias \\(\\d+ por equivalencia\\)", html);
    }
}
