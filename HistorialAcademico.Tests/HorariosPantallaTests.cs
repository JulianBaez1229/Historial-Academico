using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using HistorialAcademico.Core.Entities;
using HistorialAcademico.Core.Horarios;
using HistorialAcademico.Web.Data;
using Microsoft.Extensions.DependencyInjection;

namespace HistorialAcademico.Tests;

/// <summary>La pantalla «Horarios de Banner» con datos: cada estado (sin consultar, vacío, con secciones) y sus botones.</summary>
public class HorariosPantallaTests : IClassFixture<AppConDatosFactory>
{
    private readonly AppConDatosFactory _app;

    public HorariosPantallaTests(AppConDatosFactory app) => _app = app;

    /// <summary>Materias que la pantalla ofrece (las faltantes que se pueden consultar).</summary>
    private async Task<List<string>> OfrecidasAsync()
    {
        var (_, html) = await _app.GetAsync("/Horarios");
        return Regex.Matches(html, "<option value=\"([A-Z]{2,4}\\d{3})\"").Select(m => m.Groups[1].Value).Distinct().ToList();
    }

    private async Task GuardarAsync(string periodo, string codigo, int secciones, Action<SeccionOfertada>? ajustar = null)
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HistorialContext>();
        db.ConsultasSecciones.Add(new ConsultaSecciones { Periodo = periodo, Codigo = codigo, Secciones = secciones, Fecha = DateTime.UtcNow });
        for (var i = 0; i < secciones; i++)
        {
            var s = new SeccionOfertada
            {
                Periodo = periodo, Nrc = $"{periodo}{codigo}{i}", Codigo = codigo, Titulo = "DISENO DE PRUEBA", Seccion = (i + 1).ToString(), Creditos = 3,
                Campus = "CAMPUS - PRUEBA", Metodo = "TEORIA", Profesor = i == 0 ? "Pedro Ejemplo Prueba Uno" : "", CupoMaximo = 30, Inscritos = 29 - i * 29,
                CuposDisponibles = 1 + i * 29, Abierta = true, Consultada = DateTime.UtcNow,
                BloquesJson = JsonSerializer.Serialize(i == 0
                    ? new[]
                    {
                        new BloqueBanner { Dias = DiasSemana.Martes | DiasSemana.Jueves, Inicio = new TimeOnly(8, 0), Fin = new TimeOnly(10, 0), Edificio = "EDIF-03", Aula = "12" },
                        new BloqueBanner { Dias = DiasSemana.Ninguno, Inicio = new TimeOnly(18, 0), Fin = new TimeOnly(20, 0) },
                    }
                    : Array.Empty<BloqueBanner>()),
            };
            ajustar?.Invoke(s);
            db.SeccionesOfertadas.Add(s);
        }
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task SinElegirNadaInvitaAElegirUnaMateria()
    {
        var (estado, html) = await _app.GetAsync("/Horarios");

        Assert.Equal(HttpStatusCode.OK, estado);
        Assert.Contains("Horarios de Banner", html);
        Assert.Contains("Elige una materia y un período", html);
        Assert.Contains("Banner solo se consulta cuando pulsas el botón", html);
        Assert.Contains("Horarios de Banner", html[html.IndexOf("id=\"menu-lateral\"", StringComparison.Ordinal)..]);   // está en el menú
        Assert.NotEmpty(await OfrecidasAsync());
        Assert.Contains("No se consultan por horario", html);   // TFG y pasantía se explican, no se ofrecen
        Assert.DoesNotContain("<option value=\"TFG\"", html);
        Assert.DoesNotContain("<option value=\"PAS261\"", html);
    }

    [Fact]
    public async Task ElPeriodoPorDefectoEsElPrimeroEnElQueSePuedeInscribirYSeOfrecenVarios()
    {
        var (_, html) = await _app.GetAsync("/Horarios");

        // El histórico sintético termina con un curso en progreso en SEP-DIC 2025: se inscribe desde ENE-ABR 2026.
        Assert.Matches("<option value=\"202610\" selected[^>]*>ENE-ABR 2026</option>", html);
        Assert.Contains(">SEP-DIC 2025</option>", html);
        Assert.Contains(">MAY-AGO 2026</option>", html);
        Assert.Contains(">SEP-DIC 2026</option>", html);
    }

    [Fact]
    public async Task UnaMateriaNuncaConsultadaOfreceConsultarEnBanner()
    {
        var materia = (await OfrecidasAsync()).First();

        var (_, html) = await _app.GetAsync($"/Horarios?materia={materia}&periodo=202610");

        Assert.Contains($"Aún no has consultado {materia} en ENE-ABR 2026", html);
        Assert.Contains("Consultar en Banner", html);
        Assert.Matches("action=\"/Horarios/Consultar\"", html);
        Assert.Contains("__RequestVerificationToken", html);                  // el POST lleva token antifalsificación
        Assert.DoesNotContain("Sin secciones publicadas por ahora", html);   // «sin consultar» no es «sin secciones»
    }

    [Fact]
    public async Task ElEstadoVacioEsNeutroYOfreceConsultarDeNuevoYBuscarAntes()
    {
        var materia = (await OfrecidasAsync()).First();
        await GuardarAsync("202620", materia, 0);

        var (_, html) = await _app.GetAsync($"/Horarios?materia={materia}&periodo=202620");

        Assert.Contains("Sin secciones publicadas por ahora", html);
        Assert.Contains("Es normal", html);
        Assert.Contains("Consultar de nuevo", html);
        Assert.Contains("Ver cuándo se ofreció antes", html);
        Assert.Contains("action=\"/Horarios/BuscarAnteriores\"", html);
        Assert.DoesNotContain("alert-danger", html);           // no se presenta como un error
        Assert.DoesNotContain("id=\"tabla-secciones\"", html);
    }

    [Fact]
    public async Task LasSeccionesSeMuestranConProfesorHorarioAulaYCupos()
    {
        var materia = (await OfrecidasAsync()).Skip(1).First();
        await GuardarAsync("202630", materia, 2);

        var (_, html) = await _app.GetAsync($"/Horarios?materia={materia}&periodo=202630");

        Assert.Contains("id=\"tabla-secciones\"", html);
        Assert.Contains("2 secciones", html);
        Assert.Contains("Pedro Ejemplo Prueba Uno", html);
        Assert.Contains("Sin asignar", html);                                  // la segunda sección aún no tiene profesor
        Assert.Contains("Mar y Jue 8:00 – 10:00 a. m. · EDIF-03 aula 12", html);
        Assert.Contains("Virtual, sin día fijo", html);                         // el bloque sin día no se pinta como un día
        Assert.Contains("Sin horario publicado", html);
        Assert.Contains("1 libres", html);
        Assert.Contains("30 libres", html);
        Assert.Contains("Diseno de Prueba", html);                              // título de Banner arreglado
        Assert.DoesNotContain("Sin secciones publicadas por ahora", html);
    }

    [Fact]
    public async Task UnCursoEspecialSinCupoNoSeMuestraComoLleno()
    {
        var materia = (await OfrecidasAsync()).Skip(3).First();
        await GuardarAsync("202620", materia, 1, s => { s.CupoMaximo = 0; s.Inscritos = 0; s.CuposDisponibles = 0; s.Abierta = false; s.Seccion = "TU1"; });

        var (_, html) = await _app.GetAsync($"/Horarios?materia={materia}&periodo=202620");

        Assert.Contains("Sin cupo asignado", html);
        Assert.DoesNotContain(">Llena<", html);
    }

    [Fact]
    public async Task LaOfertaDePeriodosAnterioresSeMencionaEnLaMateria()
    {
        var materia = (await OfrecidasAsync()).Skip(2).First();
        await GuardarAsync("202530", materia, 1);

        var (_, html) = await _app.GetAsync($"/Horarios?materia={materia}&periodo=202610");

        Assert.Contains("id=\"oferta-previa\"", html);
        Assert.Contains($"{materia}</strong> se ofreció en", html);
        Assert.Contains("SEP-DIC 2025", html);
    }

    [Theory]
    [InlineData("TFG")]
    [InlineData("PAS261")]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("NOEXISTE999")]
    public async Task UnaMateriaQueNoSeOfreceNoRompeLaPantallaNiSeRefleja(string materia)
    {
        var (estado, html) = await _app.GetAsync("/Horarios?materia=" + Uri.EscapeDataString(materia) + "&periodo=202610");

        Assert.Equal(HttpStatusCode.OK, estado);
        Assert.Contains("Elige una materia y un período", html);
        Assert.DoesNotContain("<script>alert(1)</script>", html);
    }

    [Fact]
    public async Task UnPeriodoQueNoEsDeLaListaCaeEnElPeriodoPorDefecto()
    {
        var (estado, html) = await _app.GetAsync("/Horarios?periodo=202635");

        Assert.Equal(HttpStatusCode.OK, estado);
        Assert.Matches("<option value=\"202610\" selected", html);
    }

    [Fact]
    public async Task ConsultarSinTokenAntifalsificacionSeRechaza()
    {
        using var cliente = _app.CreateClient();

        var r = await cliente.PostAsync("/Horarios/Consultar", new FormUrlEncodedContent(new Dictionary<string, string> { ["materia"] = "ISO725", ["periodo"] = "202610" }));

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }
}
