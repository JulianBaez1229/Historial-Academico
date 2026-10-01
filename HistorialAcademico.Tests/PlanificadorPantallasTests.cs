using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace HistorialAcademico.Tests;

/// <summary>Recorre el planificador como lo haría el navegador: con cookies, token antifalsificación y redirecciones.</summary>
public class PlanificadorPantallasTests : IClassFixture<AppConDatosFactory>
{
    private readonly AppConDatosFactory _app;

    public PlanificadorPantallasTests(AppConDatosFactory app) => _app = app;

    private static string Decodificar(string html) => WebUtility.HtmlDecode(html);

    private static async Task<string> Get(HttpClient c, string url) => Decodificar(await c.GetStringAsync(url));

    private static async Task<string> Token(HttpClient c)
    {
        var html = await c.GetStringAsync("/Planificador");
        return Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
    }

    private static async Task<(HttpResponseMessage Respuesta, string Html)> Post(HttpClient c, string url, string token, params (string, string)[] campos)
    {
        var datos = campos.Select(x => new KeyValuePair<string, string>(x.Item1, x.Item2)).Append(new("__RequestVerificationToken", token));
        var r = await c.PostAsync(url, new FormUrlEncodedContent(datos));
        return (r, Decodificar(await r.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task ElPlanificadorMuestraElLimiteElPrimerPeriodoYLosControles()
    {
        using var c = _app.CreateClient();
        var html = await Get(c, "/Planificador");

        Assert.Contains("Planificador de cuatrimestres", html);
        Assert.Contains("Plan por cuatrimestres", html);
        Assert.Contains("Qué priorizar", html);
        Assert.Contains("Comparar escenarios", html);
        Assert.Contains("Generar plan sugerido", html);
        Assert.Contains("Límite: 25 créditos por cuatrimestre", html);   // índice sintético 2.31: no supera 3.40
        Assert.Contains("Primer período planificable: <strong>ENE-ABR 2026</strong>", html);   // el curso en progreso es SEP-DIC 2025
        Assert.Contains("Asumir que apruebo los cursos en progreso", html);
        Assert.Contains("Por planificar", html);
        Assert.Contains("Créditos faltantes", html);
        Assert.Contains("Graduación", html);
        Assert.Contains("Plan 1", html);
    }

    [Fact]
    public async Task ElPlanSugeridoSeGeneraYCumpleLasReglas()
    {
        using var c = _app.CreateClient();
        var token = await Token(c);
        var (r, html) = await Post(c, "/Planificador/Generar", token, ("id", "1"));

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);   // tras la redirección
        Assert.Contains("Plan sugerido generado", html);
        Assert.Contains("Este plan cumple todas las reglas", html);
        Assert.Contains("Prioridad", html);                       // cada materia muestra su razón
        Assert.Contains("TFG: siempre va en el último período", html);
        Assert.DoesNotContain("✖", html);                         // ninguna materia con problemas
        Assert.Contains("Todas las materias están en el plan", html);
    }

    [Fact]
    public async Task ArrastrarYSoltarUsaLaMismaAccionYValidaEnTiempoReal()
    {
        using var c = _app.CreateClient();
        var token = await Token(c);
        await Post(c, "/Planificador/Limpiar", token, ("id", "1"));

        // Lo que hace el JavaScript al soltar una tarjeta: POST con X-Requested-With y respuesta JSON.
        var solicitud = new HttpRequestMessage(HttpMethod.Post, "/Planificador/Asignar")
        {
            Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("id", "1"), new("codigo", "ISO615"), new("periodo", "ENE-ABR 2026"),
                new("__RequestVerificationToken", token),
            }),
        };
        solicitud.Headers.Add("X-Requested-With", "XMLHttpRequest");
        var r = await c.SendAsync(solicitud);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Contains("\"ok\":true", await r.Content.ReadAsStringAsync());

        // ISO615 requiere INF165, que no está aprobada: la pantalla lo marca en rojo.
        var html = await Get(c, "/Planificador");
        Assert.Contains("ISO615", html);
        Assert.Contains("✖ Falta INF165", html);
        Assert.Contains("Este plan tiene 1 problema", html);
        Assert.Contains("plan-card invalida", html);

        // Una materia que no existe se rechaza con 400 y un mensaje.
        var mala = new HttpRequestMessage(HttpMethod.Post, "/Planificador/Asignar")
        {
            Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("id", "1"), new("codigo", "ZZZ999"), new("periodo", "ENE-ABR 2026"),
                new("__RequestVerificationToken", token),
            }),
        };
        mala.Headers.Add("X-Requested-With", "XMLHttpRequest");
        var rMala = await c.SendAsync(mala);
        Assert.Equal(HttpStatusCode.BadRequest, rMala.StatusCode);
        Assert.Contains("no existe en el pénsum", Decodificar(await rMala.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task AgregarAUnPeriodoConElBotonYQuitarlo()
    {
        using var c = _app.CreateClient();
        var token = await Token(c);
        await Post(c, "/Planificador/Limpiar", token, ("id", "1"));

        var (_, conMateria) = await Post(c, "/Planificador/Asignar", token, ("id", "1"), ("codigo", "ISO100"), ("periodo", "MAY-AGO 2026"));
        Assert.Contains("aria-label=\"Quitar ISO100 del plan\"", conMateria);
        Assert.Contains("MAY-AGO 2026", conMateria);

        var (_, sinMateria) = await Post(c, "/Planificador/Asignar", token, ("id", "1"), ("codigo", "ISO100"), ("periodo", ""));
        Assert.DoesNotContain("Quitar ISO100 del plan", sinMateria);
    }

    [Fact]
    public async Task GuardaEscenariosYLosComparaLadoALado()
    {
        using var c = _app.CreateClient();
        var token = await Token(c);
        await Post(c, "/Planificador/Generar", token, ("id", "1"));

        var (_, guardado) = await Post(c, "/Planificador/GuardarComo", token, ("id", "1"), ("nombre", "Carga normal"));
        Assert.Contains("guardado como escenario", guardado);
        Assert.Contains("Carga normal", guardado);

        var (_, duplicado) = await Post(c, "/Planificador/Crear", token, ("nombre", "carga NORMAL"));
        Assert.Contains("Ya existe un plan llamado", duplicado);

        await Post(c, "/Planificador/Crear", token, ("nombre", "Vacío"));

        var comparar = await Get(c, "/Planificador/Comparar");
        Assert.Contains("Comparar escenarios", comparar);
        foreach (var nombre in new[] { "Plan 1", "Carga normal", "Vacío" }) Assert.Contains(nombre, comparar);
        Assert.Contains("Cuatrimestres planificados", comparar);
        Assert.Contains("Período de graduación", comparar);
        Assert.Contains("estimada", comparar);           // el escenario vacío solo tiene una estimación
        Assert.Contains("Sin problemas", comparar);
    }

    [Fact]
    public async Task ConfigurarElLimiteCambiaLaPlanificacion_YRechazaValoresInvalidos()
    {
        using var c = _app.CreateClient();
        var token = await Token(c);

        var (_, invalido) = await Post(c, "/Planificador/Configurar", token, ("id", "1"), ("limiteBase", "0"), ("limiteAlto", "27"),
            ("umbralIndice", "3.40"), ("minimo", "0"), ("asumirEnCurso", "true"));
        Assert.Contains("El límite de créditos debe estar entre 1 y 60", invalido);

        var (_, ok) = await Post(c, "/Planificador/Configurar", token, ("id", "1"), ("limiteBase", "18"), ("limiteAlto", "20"),
            ("umbralIndice", "3.40"), ("minimo", "9"), ("asumirEnCurso", "true"));
        Assert.Contains("Configuración guardada", ok);
        Assert.Contains("Límite: 18 créditos por cuatrimestre", ok);

        // Se restablece para no afectar a las demás pruebas de esta clase.
        await Post(c, "/Planificador/Configurar", token, ("id", "1"), ("limiteBase", "25"), ("limiteAlto", "27"),
            ("umbralIndice", "3.40"), ("minimo", "0"), ("asumirEnCurso", "true"));
    }

    [Fact]
    public async Task QuePriorizarListaTodasLasFaltantesConSuMotivo()
    {
        using var c = _app.CreateClient();
        var html = await Get(c, "/Planificador/Prioridades");

        Assert.Contains("Qué priorizar", html);
        Assert.Contains("Ruta crítica", html);
        Assert.Contains("Materias que desbloquea", html);
        Assert.Contains("Rezagada", html);
        Assert.Contains("Bloqueada por %", html);
        Assert.Contains("ISO100", html);
        Assert.Contains("Inicia la cadena", html);
        Assert.Contains("Requiere 59% de los créditos", html);   // la Electiva I exige un porcentaje
    }

    [Fact]
    public async Task TodasLasAccionesExigenElTokenAntifalsificacion()
    {
        using var c = _app.CreateClient();
        foreach (var accion in new[] { "Crear", "GuardarComo", "Renombrar", "Eliminar", "Asignar", "Generar", "Limpiar", "Configurar" })
        {
            var r = await c.PostAsync("/Planificador/" + accion, new FormUrlEncodedContent(new Dictionary<string, string>()));
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        }
    }
}

public class PlanificadorSinDatosTests : IClassFixture<AppFactory>
{
    private readonly AppFactory _app;

    public PlanificadorSinDatosTests(AppFactory app) => _app = app;

    [Theory]
    [InlineData("/Planificador", "Primero sincroniza tu histórico")]
    [InlineData("/Planificador/Prioridades", "Primero sincroniza tu histórico")]
    [InlineData("/Planificador/Comparar", "Primero sincroniza tu histórico")]
    public async Task SinDatosExplicaQueHacerYNoFalla(string url, string texto)
    {
        var (estado, html) = await _app.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, estado);
        Assert.Contains(texto, html);
    }
}
