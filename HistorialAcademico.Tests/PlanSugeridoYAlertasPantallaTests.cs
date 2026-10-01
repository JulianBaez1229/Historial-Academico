using System.Net;
using System.Text.RegularExpressions;
using HistorialAcademico.Core.Planificacion;
using HistorialAcademico.Web.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HistorialAcademico.Tests;

/// <summary>La pregunta antes de generar el plan y las alertas con su botón, de punta a punta con los datos de prueba.</summary>
public class PlanSugeridoYAlertasPantallaTests
{
    private static async Task<string> TokenAsync(HttpClient c) =>
        Regex.Match(await c.GetStringAsync("/Planificador"), "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;

    private static async Task<string> PostAsync(HttpClient c, string url, params (string, string)[] campos)
    {
        var datos = campos.Select(x => new KeyValuePair<string, string>(x.Item1, x.Item2)).Append(new("__RequestVerificationToken", await TokenAsync(c)));
        return WebUtility.HtmlDecode(await (await c.PostAsync(url, new FormUrlEncodedContent(datos))).Content.ReadAsStringAsync());
    }

    private static async Task<EstadoPlanificador> EstadoAsync(AppConDatosFactory app, int plan = 1)
    {
        using var scope = app.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<PlanificadorService>().ObtenerAsync(plan);
    }

    [Fact]
    public async Task ElPanelPreguntaLaCargaConSusPrevisionesYLosPeriodosEnQueNoSeEstudia()
    {
        using var app = new AppConDatosFactory();
        await app.InitializeAsync();
        using var c = app.CreateClient();

        var html = WebUtility.HtmlDecode(await c.GetStringAsync("/Planificador"));

        Assert.Contains("id=\"generarPlan\"", html);
        Assert.Contains("Generar plan sugerido…", html);
        Assert.Contains("¿Qué carga quieres llevar?", html);
        Assert.Matches("name=\"carga\" id=\"carga-Ligera\" value=\"ligera\"", html);
        Assert.Matches("id=\"carga-Normal\" value=\"normal\" checked", html);   // por defecto, normal
        Assert.Matches("name=\"carga\" id=\"carga-Pesada\" value=\"pesada\"", html);
        Assert.Matches(@"Ligera</strong> · hasta \d+ créditos por período ·\s+\d+ cuatrimestres?", html);
        Assert.Contains("¿Hay períodos en los que no vas a estudiar?", html);
        Assert.Equal(6, Regex.Matches(html, "name=\"omitir\"").Count);
        Assert.Contains("Cada materia del plan dice por qué va ahí", html);
    }

    [Fact]
    public async Task LasPrevisionesMuestranQueConMenosCargaSeTardaMasSinPasarDelLimiteElegido()
    {
        using var app = new AppConDatosFactory();
        await app.InitializeAsync();

        var st = await EstadoAsync(app);

        var previsiones = st.Previsiones.ToDictionary(p => p.Carga);
        Assert.Equal(3, previsiones.Count);
        Assert.True(previsiones[CargaDeseada.Ligera].Limite < previsiones[CargaDeseada.Normal].Limite);
        Assert.True(previsiones[CargaDeseada.Normal].Limite <= previsiones[CargaDeseada.Pesada].Limite);
        Assert.Equal(st.Contexto!.Limite, previsiones[CargaDeseada.Pesada].Limite);
        Assert.True(previsiones[CargaDeseada.Ligera].Cuatrimestres >= previsiones[CargaDeseada.Normal].Cuatrimestres);
        Assert.True(previsiones[CargaDeseada.Normal].Cuatrimestres >= previsiones[CargaDeseada.Pesada].Cuatrimestres);
        Assert.True(previsiones[CargaDeseada.Ligera].Graduacion >= previsiones[CargaDeseada.Pesada].Graduacion);
        Assert.Equal(6, st.PeriodosOmitibles.Count);
        Assert.Equal(st.Contexto.Primero, st.PeriodosOmitibles[0]);
    }

    [Fact]
    public async Task GenerarConCargaLigeraYUnPeriodoOmitidoLoRespetaYLoDice()
    {
        using var app = new AppConDatosFactory();
        await app.InitializeAsync();
        using var c = app.CreateClient();
        var antes = await EstadoAsync(app);
        var omitido = antes.PeriodosOmitibles[1];
        var tope = antes.Previsiones.Single(p => p.Carga == CargaDeseada.Ligera).Limite;

        var html = await PostAsync(c, "/Planificador/Generar", ("id", "1"), ("carga", "ligera"), ("omitir", omitido.Nombre));

        Assert.Contains("Plan sugerido generado (carga ligera)", html);
        Assert.Contains($"Sin estudiar en {omitido.Nombre}", html);
        var st = await EstadoAsync(app);
        Assert.DoesNotContain(st.Plan, p => p.Periodo == omitido && p.Codigos.Count > 0);
        Assert.All(st.Evaluacion!.Periodos, p => Assert.True(p.Creditos <= tope, $"{p.Periodo.Nombre} tiene {p.Creditos} créditos y el tope de la carga ligera es {tope}"));
        Assert.True(st.Evaluacion.TodoPlanificado);
        Assert.Equal(0, st.Evaluacion.Problemas);   // el plan que se genera es válido
    }

    [Fact]
    public async Task SinDecirLaCargaSeSigueGenerandoHastaElLimiteComoAntes()
    {
        using var app = new AppConDatosFactory();
        await app.InitializeAsync();
        using var c = app.CreateClient();

        var html = await PostAsync(c, "/Planificador/Generar", ("id", "1"));

        Assert.Contains("Plan sugerido generado (carga pesada)", html);
        Assert.DoesNotContain("Sin estudiar en", html);
    }

    [Fact]
    public async Task UnPeriodoOmitidoQueNoSeEntiendeSeIgnoraSinRomperNada()
    {
        using var app = new AppConDatosFactory();
        await app.InitializeAsync();
        using var c = app.CreateClient();

        var html = await PostAsync(c, "/Planificador/Generar", ("id", "1"), ("carga", "lo-que-sea"), ("omitir", "no es un período"));

        Assert.Contains("Plan sugerido generado (carga pesada)", html);
        Assert.DoesNotContain("Sin estudiar en", html);
    }

    [Fact]
    public async Task UnPlanGeneradoNoTieneAlertasGravesDeLimiteNiDeMateriasQueNoPuedenCursarse()
    {
        using var app = new AppConDatosFactory();
        await app.InitializeAsync();
        using var c = app.CreateClient();
        await PostAsync(c, "/Planificador/Generar", ("id", "1"), ("carga", "normal"));

        var st = await EstadoAsync(app);

        Assert.DoesNotContain(st.Alertas, a => a.Tipo is TipoAlerta.SobreLimite or TipoAlerta.ConProblema);
    }

    [Fact]
    public async Task ElBotonDeUnaAlertaLaResuelveYDiceQueHizo()
    {
        using var app = new AppConDatosFactory();
        await app.InitializeAsync();
        using var c = app.CreateClient();
        await PostAsync(c, "/Planificador/Generar", ("id", "1"), ("carga", "pesada"));

        // Se saca del plan una materia de ruta crítica del primer período: ahora hay una alerta que dice que falta.
        var st = await EstadoAsync(app);
        var ctx = st.Contexto!;
        var critica = st.Prioridades!.Materias.First(m => m.Etiquetas.Contains("Ruta crítica")
            && ctx.EstaDisponible(m.Materia.Codigo, ctx.Aprobadas, ctx.CreditosAprobados)
            && st.Plan.First(p => p.Codigos.Count > 0).Codigos.Contains(m.Materia.Codigo, StringComparer.OrdinalIgnoreCase)).Materia.Codigo;
        await PostAsync(c, "/Planificador/Asignar", ("id", "1"), ("codigo", critica), ("periodo", ""));

        var html = await PostAsync(c, "/Planificador/Configurar", ("id", "1"), ("limiteBase", "25"), ("limiteAlto", "27"), ("umbralIndice", "3.40"), ("minimo", "0"), ("asumirEnCurso", "true"));
        Assert.Contains("id=\"alertas-plan\"", html);
        var alerta = Regex.Match(html, "data-tipo=\"RutaCritica\"[\\s\\S]*?name=\"codigo\" value=\"" + critica + "\"[\\s\\S]*?name=\"periodo\" value=\"([^\"]*)\"[\\s\\S]*?name=\"avisar\" value=\"true\"[\\s\\S]*?>([^<]+)</button>");
        Assert.True(alerta.Success, "la alerta de ruta crítica debe traer su botón");
        Assert.Equal(ctx.Primero.Nombre, alerta.Groups[1].Value);
        Assert.Contains($"Agregar a {ctx.Primero.Nombre}", alerta.Groups[2].Value);

        var despues = await PostAsync(c, "/Planificador/Asignar", ("id", "1"), ("codigo", critica), ("periodo", alerta.Groups[1].Value), ("avisar", "true"));

        Assert.Contains($"{critica} → {ctx.Primero.Nombre}.", despues);   // se dice qué se hizo
        Assert.DoesNotContain(( await EstadoAsync(app)).Alertas, a => a.Tipo == TipoAlerta.RutaCritica && a.Mensaje.StartsWith(critica));
    }

    [Fact]
    public async Task SinAvisarElMovimientoSigueSiendoSilencioso()
    {
        using var app = new AppConDatosFactory();
        await app.InitializeAsync();
        using var c = app.CreateClient();
        await PostAsync(c, "/Planificador/Generar", ("id", "1"));
        var codigo = (await EstadoAsync(app)).Plan.First(p => p.Codigos.Count > 0).Codigos[0];

        var html = await PostAsync(c, "/Planificador/Asignar", ("id", "1"), ("codigo", codigo), ("periodo", ""));

        Assert.DoesNotContain($"{codigo} quitada del plan", html);   // como siempre: el tablero ya lo muestra
    }

    [Fact]
    public async Task SinNadaPlanificadoNoHayAlertasNiElMensajeDeSinAlertas()
    {
        using var app = new AppConDatosFactory();
        await app.InitializeAsync();
        using var c = app.CreateClient();

        var html = WebUtility.HtmlDecode(await c.GetStringAsync("/Planificador"));

        Assert.DoesNotContain("id=\"alertas-plan\"", html);
        Assert.DoesNotContain("id=\"sin-alertas\"", html);
    }
}
