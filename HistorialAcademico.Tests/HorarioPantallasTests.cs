using System.Net;
using System.Text.RegularExpressions;
using HistorialAcademico.Core.Entities;
using HistorialAcademico.Core.Horarios;
using HistorialAcademico.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HistorialAcademico.Tests;

/// <summary>
/// Las pantallas de horario tentativo, horas no disponibles y solicitud de apertura, recorridas como las usa el navegador
/// (cookies, token antifalsificación y redirecciones), con la aplicación completa y datos sintéticos.
/// </summary>
public class HorarioPantallasTests : IClassFixture<AppConDatosFactory>
{
    private const DiasSemana MJ = DiasSemana.Martes | DiasSemana.Jueves;
    private const string P = "202610";   // ENE-ABR 2026: el primer período en que se puede inscribir con el histórico sintético

    private readonly AppConDatosFactory _app;

    public HorarioPantallasTests(AppConDatosFactory app) => _app = app;

    private static string Decodificar(string html) => WebUtility.HtmlDecode(html);

    private async Task<string> GetAsync(string url) => (await _app.GetAsync(url)).Html;

    private async Task<string> TokenAsync(HttpClient c) =>
        Regex.Match(await c.GetStringAsync("/Horarios/NoDisponible"), "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;

    private async Task<(HttpResponseMessage Respuesta, string Html)> PostAsync(string url, params (string, string)[] campos)
    {
        using var c = _app.CreateClient();
        var datos = campos.Select(x => new KeyValuePair<string, string>(x.Item1, x.Item2)).Append(new("__RequestVerificationToken", await TokenAsync(c)));
        var r = await c.PostAsync(url, new FormUrlEncodedContent(datos));
        return (r, Decodificar(await r.Content.ReadAsStringAsync()));
    }

    private async Task SembrarSeccionesAsync()
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HistorialContext>();
        if (await db.SeccionesOfertadas.AnyAsync(s => s.Nrc == "T1001")) return;
        db.SeccionesOfertadas.AddRange(
            SeccionesDePrueba.Fila(P, "T1001", "ISO625", "1", "Pedro Uno", SeccionesDePrueba.Bloque(MJ, "08:00", "10:00")),
            SeccionesDePrueba.Fila(P, "T1002", "ISO625", "2", "Marta Dos", SeccionesDePrueba.Bloque(DiasSemana.Lunes | DiasSemana.Miercoles, "18:00", "20:00")),
            SeccionesDePrueba.Fila(P, "T2001", "ISO800", "1", "Juan Tres", SeccionesDePrueba.Bloque(DiasSemana.Jueves, "09:00", "11:00")),
            SeccionesDePrueba.Fila(P, "T2002", "ISO800", "2", "", SeccionesDePrueba.Bloque(DiasSemana.Sabado, "08:00", "12:00")));
        await db.SaveChangesAsync();
    }

    /// <summary>Materias que la pantalla de secciones ofrece (las faltantes del histórico sintético que se pueden consultar).</summary>
    private async Task<List<string>> OfrecidasAsync() =>
        Regex.Matches(await GetAsync("/Horarios"), "<option value=\"([A-Z]{2,4}\\d{3})\"").Select(m => m.Groups[1].Value).Distinct().ToList();

    private static int IdDelHorario(string html, string nombre) =>
        int.Parse(Regex.Match(html, "<option value=\"(\\d+)\" selected[^>]*>" + Regex.Escape(nombre)).Groups[1].Value);

    // ── Horario tentativo ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SinHorariosInvitaACrearUnoYNoMuestraNingunaGrilla()
    {
        using var nueva = new AppConDatosFactory();

        var (estado, html) = await nueva.GetAsync("/Horarios/Tentativo");

        Assert.Equal(HttpStatusCode.OK, estado);
        Assert.Contains("Aún no tienes ningún horario", html);
        Assert.Contains("action=\"/Horarios/CrearTentativo\"", html);
        Assert.DoesNotContain("id=\"grilla-horario\"", html);
        Assert.Contains("Mi horario tentativo", html);
        Assert.Contains("Solicitar apertura", html);           // las cuatro pestañas
        Assert.Contains("Horas no disponibles", html);
    }

    [Fact]
    public async Task ArmarUnHorarioDePuntaAPunta_ChoquesEscenarioYBorrado()
    {
        await SembrarSeccionesAsync();

        // Crear.
        var (_, html) = await PostAsync("/Horarios/CrearTentativo", ("nombre", "Flujo completo"), ("periodo", P));
        Assert.Contains("Horario «Flujo completo» creado para ENE-ABR 2026", html);
        var id = IdDelHorario(html, "Flujo completo").ToString();
        Assert.Contains("Todavía no elegiste ninguna sección", html);
        Assert.Contains("id=\"opciones\"", html);
        Assert.Contains("Pedro Uno", html);                                  // las opciones muestran el profesor
        Assert.DoesNotContain("T9999", html);

        // Una sección: se ve en la grilla, sin choques.
        (_, html) = await PostAsync("/Horarios/AgregarSeccion", ("id", id), ("nrc", "T1001"));
        Assert.Contains("Agregada ISO625-1.", html);
        Assert.Contains("id=\"grilla-horario\"", html);
        Assert.Contains("horario-clase", html);
        Assert.DoesNotContain("horario-choque", html);
        Assert.DoesNotContain("id=\"choques\"", html);
        Assert.Contains("1 materia · 3 créditos", html);

        // Una segunda que se cruza el jueves: ambas en rojo, con el motivo.
        (_, html) = await PostAsync("/Horarios/AgregarSeccion", ("id", id), ("nrc", "T2001"));
        Assert.Contains("id=\"choques\"", html);
        Assert.Contains("Hay un choque de horario", html);
        Assert.Contains("ISO625-1 y ISO800-1 coinciden Jue de 9:00 a. m. a 10:00 a. m.", html);
        Assert.Equal(3, Regex.Matches(html, "class=\"horario-celda horario-clase horario-choque\"").Count);   // martes y jueves de una, jueves de la otra
        Assert.Contains("horario-celda-choque", html);                        // además del color, dice «Choque»

        // Asociarlo a un escenario del planificador (el plan por defecto se crea al abrir el planificador por primera vez).
        await GetAsync("/Planificador");
        (_, html) = await PostAsync("/Horarios/AsociarPlan", ("id", id), ("planId", "1"));
        Assert.Contains("Horario asociado al escenario", html);
        Assert.Contains("id=\"plan-asociado\"", html);
        Assert.Contains("Horarios tentativos de este escenario", await GetAsync("/Planificador"));
        Assert.Contains("Flujo completo", await GetAsync("/Planificador"));

        // Quitar la sección que choca: se acaba el choque.
        (_, html) = await PostAsync("/Horarios/QuitarSeccion", ("id", id), ("nrc", "T2001"));
        Assert.Contains("Quitada ISO800-1.", html);
        Assert.DoesNotContain("id=\"choques\"", html);

        // Borrar el horario.
        (_, html) = await PostAsync("/Horarios/EliminarTentativo", ("id", id));
        Assert.Contains("eliminado", html);
        Assert.DoesNotContain("Flujo completo ·", html);
    }

    [Fact]
    public async Task UnaSeccionQueNoExisteSeRechazaConUnMensaje()
    {
        await SembrarSeccionesAsync();
        var (_, html) = await PostAsync("/Horarios/CrearTentativo", ("nombre", "Rechazos"), ("periodo", P));
        var id = IdDelHorario(html, "Rechazos").ToString();

        (_, html) = await PostAsync("/Horarios/AgregarSeccion", ("id", id), ("nrc", "NOEXISTE"));

        Assert.Contains("no está entre las consultadas", html);
        Assert.Contains("alert-danger", html);
    }

    [Fact]
    public async Task CrearUnHorarioSinNombreOConPeriodoDePosgradoMuestraElError()
    {
        var (_, sinNombre) = await PostAsync("/Horarios/CrearTentativo", ("nombre", ""), ("periodo", P));
        var (_, posgrado) = await PostAsync("/Horarios/CrearTentativo", ("nombre", "Posgrado"), ("periodo", "202635"));

        Assert.Contains("Escribe un nombre para el horario", sinNombre);
        Assert.Contains("período de Grado", posgrado);
    }

    // ── Horas no disponibles ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task LaCuadriculaDeHorasNoDisponiblesTieneUnaCasillaPorDiaYHora()
    {
        var html = await GetAsync("/Horarios/NoDisponible");

        Assert.Equal(6 * 17, Regex.Matches(html, "name=\"celdas\"").Count);          // lunes a sábado, de 6:00 a. m. a 11:00 p. m.
        Assert.Contains("aria-label=\"Lunes 8:00 a. m.\"", html);
        Assert.Contains("value=\"Miercoles-14\"", html);
        Assert.Contains("Lunes a viernes, 8:00 a. m. – 5:00 p. m.", html);          // el atajo del ejemplo de la historia
        Assert.Contains("horario.js", html);
    }

    [Fact]
    public async Task LasHorasMarcadasSeGuardanYAtenuanLasSeccionesQueChocan()
    {
        await SembrarSeccionesAsync();
        var (_, html) = await PostAsync("/Horarios/GuardarNoDisponible", ("celdas", "Martes-8"), ("celdas", "Martes-9"));
        Assert.Contains("Guardadas 2 horas no disponibles", html);
        Assert.Equal(2, Regex.Matches(html, "checked=\"checked\"").Count);

        var (_, crear) = await PostAsync("/Horarios/CrearTentativo", ("nombre", "Con preferencias"), ("periodo", P));
        var id = IdDelHorario(crear, "Con preferencias");
        var tentativo = await GetAsync($"/Horarios/Tentativo?id={id}");

        Assert.Matches("<tr class=\"seccion-atenuada\" data-nrc=\"T1001\"", tentativo);
        Assert.Contains("Choca con tus horas no disponibles", tentativo);
        Assert.DoesNotContain("<tr class=\"seccion-atenuada\" data-nrc=\"T1002\"", tentativo);
        Assert.Contains("horario-nodisp", tentativo);                                 // también se ven en la grilla
        // La compatible (sección 2) va antes que la atenuada (sección 1) dentro de ISO625.
        Assert.True(tentativo.IndexOf("data-nrc=\"T1002\"", StringComparison.Ordinal) < tentativo.IndexOf("data-nrc=\"T1001\"", StringComparison.Ordinal));

        // Dejarlo limpio para las demás pruebas.
        var (_, vacio) = await PostAsync("/Horarios/GuardarNoDisponible");
        Assert.Contains("Ya no tienes horas marcadas", vacio);
        Assert.DoesNotContain("checked=\"checked\"", vacio);
    }

    [Theory]
    [InlineData("Lunes-99")]
    [InlineData("Foo-8")]
    [InlineData("Lunes-x")]
    [InlineData("Lunes")]
    [InlineData("Lunes-8-9")]
    [InlineData("0-8")]
    public async Task LasCeldasInvalidasSeIgnoran(string celda)
    {
        var (r, html) = await PostAsync("/Horarios/GuardarNoDisponible", ("celdas", celda));

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Contains("Ya no tienes horas marcadas", html);
    }

    // ── Solicitar apertura ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task MarcarUnaMateriaListaSuNotaYGeneraElTextoParaElCorreo()
    {
        var (_, html) = await PostAsync("/Horarios/MarcarApertura", ("codigo", "ISO800"), ("nota", "la necesito para graduarme"), ("periodo", P));

        Assert.Contains("ISO800 marcada para solicitar su apertura", html);
        Assert.Contains("id=\"tabla-apertura\"", html);
        Assert.Contains("la necesito para graduarme", html);
        Assert.Contains("Solicitud de apertura de secciones – ENE-ABR 2026", html);     // el texto del correo, en el cuadro
        Assert.Contains("ISO800 – ", html);
        Assert.Contains("Nota: la necesito para graduarme", html);
        Assert.Contains("id=\"copiar-texto\"", html);
        Assert.Contains("Descargar como .txt", html);

        var quitar = await PostAsync("/Horarios/QuitarApertura", ("codigo", "ISO800"), ("periodo", P));
        Assert.Contains("ISO800 quitada de la lista", quitar.Html);
    }

    [Fact]
    public async Task ElTextoSeDescargaComoArchivoDeTexto()
    {
        await PostAsync("/Horarios/MarcarApertura", ("codigo", "ISO900"), ("nota", ""), ("periodo", P));
        using var c = _app.CreateClient();

        var r = await c.GetAsync("/Horarios/AperturaTexto?periodo=202620");
        var texto = await r.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("text/plain", r.Content.Headers.ContentType!.MediaType);
        Assert.Equal("solicitud-apertura.txt", r.Content.Headers.ContentDisposition!.FileName);
        Assert.Contains("MAY-AGO 2026", texto);            // el período pedido
        Assert.Contains("ISO900 – ", texto);
        Assert.Contains("Gracias.", texto);

        await PostAsync("/Horarios/QuitarApertura", ("codigo", "ISO900"), ("periodo", P));
    }

    [Fact]
    public async Task UnaMateriaQueNoEsDelPensumNoSeMarca()
    {
        var (_, html) = await PostAsync("/Horarios/MarcarApertura", ("codigo", "XYZ999"), ("nota", ""), ("periodo", P));

        Assert.Contains("no está en tu pénsum", html);
        Assert.Contains("alert-danger", html);
    }

    [Fact]
    public async Task DesdeUnaMateriaSinSeccionesSePuedeMarcarYVolverALaMismaPantalla()
    {
        var materia = (await OfrecidasAsync()).First();
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HistorialContext>();
            db.ConsultasSecciones.Add(new ConsultaSecciones { Periodo = "202620", Codigo = materia, Secciones = 0, Fecha = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        var url = $"/Horarios?materia={materia}&periodo=202620";
        var antes = await GetAsync(url);
        Assert.Contains("Sin secciones publicadas por ahora", antes);
        Assert.Contains("Solicitar apertura", antes);
        Assert.Contains("id=\"apertura-materia\"", antes);

        var (r, despues) = await PostAsync("/Horarios/MarcarApertura", ("codigo", materia), ("nota", "urgente"), ("volverA", url));

        Assert.Equal("/Horarios", r.RequestMessage!.RequestUri!.AbsolutePath);      // volvió a la pantalla de secciones, no a la lista
        Assert.Contains($"{materia}</strong> está marcada para solicitar su apertura", despues);
        Assert.Contains("Quitar de la lista", despues);

        var (_, quitada) = await PostAsync("/Horarios/QuitarApertura", ("codigo", materia), ("volverA", url));
        Assert.DoesNotContain("está marcada para solicitar su apertura", quitada);
    }

    [Theory]
    [InlineData("https://sitio-malicioso.example/robar")]
    [InlineData("//sitio-malicioso.example/robar")]
    public async Task ElRetornoNoPuedeLlevarFueraDeLaAplicacion(string volverA)
    {
        var (r, html) = await PostAsync("/Horarios/MarcarApertura", ("codigo", "ISO940"), ("nota", ""), ("volverA", volverA));

        Assert.Equal("localhost", r.RequestMessage!.RequestUri!.Host);
        Assert.Equal("/Horarios/Apertura", r.RequestMessage.RequestUri.AbsolutePath);
        Assert.Contains("ISO940 marcada", html);
        await PostAsync("/Horarios/QuitarApertura", ("codigo", "ISO940"));
    }

    // ── Profesores por materia ────────────────────────────────────────────────────────────

    [Fact]
    public async Task LaMateriaMuestraLosProfesoresQueLaHanDadoSegunLoConsultado()
    {
        var materia = (await OfrecidasAsync()).Skip(1).First();
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HistorialContext>();
            db.ConsultasSecciones.Add(new ConsultaSecciones { Periodo = "202530", Codigo = materia, Secciones = 1, Fecha = DateTime.UtcNow });
            db.SeccionesOfertadas.Add(SeccionesDePrueba.Fila("202530", "H1", materia, "1", "Profesora Historial Uno", SeccionesDePrueba.Bloque(MJ, "08:00", "10:00")));
            await db.SaveChangesAsync();
        }

        var html = await GetAsync($"/Horarios?materia={materia}&periodo=202610");

        Assert.Contains("id=\"profesores-previos\"", html);
        Assert.Contains($"Profesores que han dado {materia}", html);
        Assert.Contains("Profesora Historial Uno", html);
        Assert.Contains("SEP-DIC 2025", html);
        Assert.Contains("solo el nombre publicado", html);
        Assert.DoesNotContain("calificación", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("opinión", html, StringComparison.OrdinalIgnoreCase);
    }

    // ── Seguridad y navegación ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("/Horarios/CrearTentativo")]
    [InlineData("/Horarios/RenombrarTentativo")]
    [InlineData("/Horarios/EliminarTentativo")]
    [InlineData("/Horarios/AsociarPlan")]
    [InlineData("/Horarios/AgregarSeccion")]
    [InlineData("/Horarios/QuitarSeccion")]
    [InlineData("/Horarios/GuardarNoDisponible")]
    [InlineData("/Horarios/MarcarApertura")]
    [InlineData("/Horarios/QuitarApertura")]
    public async Task TodasLasAccionesQueCambianDatosExigenElTokenAntifalsificacion(string url)
    {
        using var c = _app.CreateClient();

        var r = await c.PostAsync(url, new FormUrlEncodedContent(new Dictionary<string, string> { ["id"] = "1", ["codigo"] = "ISO725" }));

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task LasPestanasMarcanLaActivaYElMenuLateralSigueEnHorarios()
    {
        foreach (var (ruta, pestana) in new[] { ("/Horarios", "Secciones de Banner"), ("/Horarios/Tentativo", "Mi horario tentativo"),
                                                ("/Horarios/NoDisponible", "Horas no disponibles"), ("/Horarios/Apertura", "Solicitar apertura") })
        {
            var html = await GetAsync(ruta);
            Assert.Matches("<a class=\"nav-link active\"[^>]*>" + Regex.Escape(pestana) + "</a>", html);
            Assert.Single(Regex.Matches(html, "class=\"menu-item activo\""));
            Assert.Matches("class=\"menu-item activo\"[^>]*title=\"Horarios de Banner\"", html);
        }
    }

    [Fact]
    public async Task LasPantallasNuevasNoHablanConBanner()
    {
        // Con la app de pruebas (Banner:BaseUrl inválido y sin sesión guardada) cualquier intento de conectarse fallaría o abriría el
        // navegador: aquí solo se leen y guardan datos locales, así que todo responde 200 rápido.
        foreach (var ruta in new[] { "/Horarios/Tentativo", "/Horarios/NoDisponible", "/Horarios/Apertura", "/Horarios/AperturaTexto" })
            Assert.Equal(HttpStatusCode.OK, (await _app.GetAsync(ruta)).Estado);
    }
}
