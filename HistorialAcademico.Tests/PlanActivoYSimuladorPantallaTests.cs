using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using HistorialAcademico.Core.Planificacion;
using HistorialAcademico.Web.Data;
using HistorialAcademico.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HistorialAcademico.Tests;

/// <summary>Comparar escenarios, «Plan activo» y el simulador de índice, de punta a punta con los datos de prueba.</summary>
public class PlanActivoYSimuladorPantallaTests
{
    private static async Task<string> TokenAsync(HttpClient c) =>
        Regex.Match(await c.GetStringAsync("/Planificador"), "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;

    private static async Task<(HttpResponseMessage Respuesta, string Html)> PostAsync(HttpClient c, string url, params (string, string)[] campos)
    {
        var datos = campos.Select(x => new KeyValuePair<string, string>(x.Item1, x.Item2)).Append(new("__RequestVerificationToken", await TokenAsync(c)));
        var r = await c.PostAsync(url, new FormUrlEncodedContent(datos));
        return (r, WebUtility.HtmlDecode(await r.Content.ReadAsStringAsync()));
    }

    private static async Task<EstadoPlanificador> EstadoAsync(AppConDatosFactory app, int? plan = 1)
    {
        using var scope = app.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<PlanificadorService>().ObtenerAsync(plan);
    }

    /// <summary>Dos escenarios: el 1 (carga pesada) y el 2, «Carga ligera».</summary>
    private static async Task<HttpClient> DosEscenariosAsync(AppConDatosFactory app)
    {
        await app.InitializeAsync();
        var c = app.CreateClient();
        await PostAsync(c, "/Planificador/Generar", ("id", "1"), ("carga", "pesada"));
        await PostAsync(c, "/Planificador/GuardarComo", ("id", "1"), ("nombre", "Carga ligera"));
        var id2 = (await EstadoAsync(app)).Planes.Single(p => p.Nombre == "Carga ligera").Id;
        await PostAsync(c, "/Planificador/Generar", ("id", id2.ToString()), ("carga", "ligera"));
        return c;
    }

    private static async Task<List<int>> ActivosAsync(AppConDatosFactory app)
    {
        using var scope = app.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<HistorialContext>().PlanesEstudio.Where(p => p.Activo).Select(p => p.Id).ToListAsync();
    }

    // ── Comparar escenarios ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task LaComparacionMuestraPromedioDeCreditosYPeriodoMasPesadoDeCadaEscenario()
    {
        using var app = new AppConDatosFactory();
        using var c = await DosEscenariosAsync(app);

        var html = WebUtility.HtmlDecode(await c.GetStringAsync("/Planificador/Comparar"));

        Assert.Contains("Cuatrimestres planificados", html);
        Assert.Contains("Período de graduación", html);
        Assert.Contains("Créditos promedio por período", html);
        Assert.Contains("Período más pesado", html);
        Assert.Equal(2, Regex.Matches(html, "columna-escenario").Count);
        // Los números de la pantalla son los de la evaluación de cada escenario.
        var st = await EstadoAsync(app);
        var (baseEstado, escenarios) = (st, await ComparadosAsync(app));
        foreach (var (plan, ev) in escenarios)
        {
            Assert.Contains(plan.Nombre, html);
            var fila = Regex.Match(html, "id=\"fila-mas-pesado\"[\\s\\S]*?</tr>").Value;
            Assert.Contains($"{ev.PeriodoMasPesado!.Periodo.Nombre}", fila);
            Assert.Contains($"({ev.PeriodoMasPesado.Creditos} cr)", fila);
            Assert.Contains(ev.PromedioCreditos.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture), Regex.Match(html, "id=\"fila-promedio\"[\\s\\S]*?</tr>").Value);
        }
        // Con menos carga hay más cuatrimestres y menos créditos por período.
        var pesado = escenarios.Single(e => e.Plan.Id == 1).Evaluacion;
        var ligero = escenarios.Single(e => e.Plan.Nombre == "Carga ligera").Evaluacion;
        Assert.True(ligero.CuatrimestresPlanificados >= pesado.CuatrimestresPlanificados);
        Assert.True(ligero.PromedioCreditos <= pesado.PromedioCreditos);
    }

    private static async Task<List<(HistorialAcademico.Core.Entities.PlanEstudio Plan, EvaluacionPlan Evaluacion)>> ComparadosAsync(AppConDatosFactory app)
    {
        using var scope = app.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<PlanificadorService>().CompararAsync()).Escenarios;
    }

    [Fact]
    public async Task ConUnSoloEscenarioSeExplicaComoCrearOtroParaCompararlo()
    {
        using var app = new AppConDatosFactory();
        await app.InitializeAsync();
        using var c = app.CreateClient();
        await c.GetStringAsync("/Planificador");

        var html = WebUtility.HtmlDecode(await c.GetStringAsync("/Planificador/Comparar"));

        Assert.Contains("id=\"pocos-escenarios\"", html);
        Assert.Contains("Guardar como", html);
    }

    // ── Plan activo ───────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SoloUnEscenarioPuedeSerElPlanActivoYSeAlternaDesdeLaComparacion()
    {
        using var app = new AppConDatosFactory();
        using var c = await DosEscenariosAsync(app);
        var id2 = (await EstadoAsync(app)).Planes.Single(p => p.Nombre == "Carga ligera").Id;
        Assert.Empty(await ActivosAsync(app));   // al principio no hay ninguno

        var (_, html2) = await PostAsync(c, "/Planificador/Activar", ("id", id2.ToString()), ("desde", "comparar"));
        Assert.Contains("«Carga ligera» es ahora tu plan activo", html2);
        Assert.Equal(new[] { id2 }, await ActivosAsync(app));
        Assert.Single(Regex.Matches(html2, "<span class=\"badge bg-success\">Plan activo</span>"));
        Assert.Contains("Quitar de activo", html2);
        Assert.Contains("Marcar como activo", html2);   // el otro escenario

        var (_, html1) = await PostAsync(c, "/Planificador/Activar", ("id", "1"), ("desde", "comparar"));
        Assert.Equal(new[] { 1 }, await ActivosAsync(app));   // pasa de uno a otro, nunca dos
        Assert.Contains("«Plan 1» es ahora tu plan activo", html1);

        var (_, quitado) = await PostAsync(c, "/Planificador/Activar", ("id", "1"), ("desde", "comparar"));
        Assert.Empty(await ActivosAsync(app));
        Assert.Contains("«Plan 1» ya no es el plan activo", quitado);
    }

    [Fact]
    public async Task UnEscenarioQueNoExisteNoSePuedeActivar()
    {
        using var app = new AppConDatosFactory();
        using var c = await DosEscenariosAsync(app);

        var (_, html) = await PostAsync(c, "/Planificador/Activar", ("id", "999"), ("desde", "comparar"));

        Assert.Contains("El plan no existe", html);
        Assert.Empty(await ActivosAsync(app));
    }

    [Fact]
    public async Task ElPlanEstaActivoSeVeEnSuPantallaYSePuedeQuitarDesdeAhi()
    {
        using var app = new AppConDatosFactory();
        using var c = await DosEscenariosAsync(app);

        var antes = WebUtility.HtmlDecode(await c.GetStringAsync("/Planificador?id=1"));
        Assert.DoesNotContain("id=\"plan-activo\"", antes);
        Assert.Contains("Marcar como plan activo", antes);

        var (_, activado) = await PostAsync(c, "/Planificador/Activar", ("id", "1"));   // sin «desde»: vuelve al escenario
        Assert.Contains("id=\"plan-activo\"", activado);
        Assert.Contains("Quitar de activo", activado);
    }

    [Fact]
    public async Task UnaCopiaDelPlanActivoNoEsActivaYBorrarloDejaSinPlanActivo()
    {
        using var app = new AppConDatosFactory();
        using var c = await DosEscenariosAsync(app);
        await PostAsync(c, "/Planificador/Activar", ("id", "1"));

        await PostAsync(c, "/Planificador/GuardarComo", ("id", "1"), ("nombre", "Copia del activo"));
        Assert.Equal(new[] { 1 }, await ActivosAsync(app));   // la copia no hereda «activo»

        await PostAsync(c, "/Planificador/Eliminar", ("id", "1"));
        Assert.Empty(await ActivosAsync(app));
    }

    [Fact]
    public async Task ElInicioMuestraLaGraduacionDelPlanActivo()
    {
        using var app = new AppConDatosFactory();
        using var c = await DosEscenariosAsync(app);
        var ligero = (await ComparadosAsync(app)).Single(e => e.Plan.Nombre == "Carga ligera");
        var sugerido = WebUtility.HtmlDecode(await c.GetStringAsync("/"));
        Assert.DoesNotContain("plan activo", sugerido);   // sin plan activo: la del plan sugerido, como siempre

        await PostAsync(c, "/Planificador/Activar", ("id", ligero.Plan.Id.ToString()), ("desde", "comparar"));
        var inicio = WebUtility.HtmlDecode(await c.GetStringAsync("/"));

        Assert.Contains("Graduación estimada (plan activo «Carga ligera»)", inicio);
        Assert.Contains(ligero.Evaluacion.Graduacion!.Value.Nombre, inicio);
        Assert.Contains($">{ligero.Evaluacion.CuatrimestresPlanificados}<", Regex.Replace(inicio, @"\s+", ""));   // los cuatrimestres restantes son los de ese plan
    }

    [Fact]
    public async Task UnPlanActivoVacioNoCambiaLaFechaDelInicio()
    {
        using var app = new AppConDatosFactory();
        await app.InitializeAsync();
        using var c = app.CreateClient();
        await c.GetStringAsync("/Planificador");
        await PostAsync(c, "/Planificador/Activar", ("id", "1"));   // el plan 1 está vacío

        var inicio = WebUtility.HtmlDecode(await c.GetStringAsync("/"));

        Assert.DoesNotContain("plan activo", inicio);
    }

    // ── Notas esperadas y simulador ───────────────────────────────────────────────────────

    private static async Task<string> UnaMateriaDelPlanAsync(AppConDatosFactory app) =>
        (await EstadoAsync(app)).Simulador!.Filas.First().Codigo;

    [Fact]
    public async Task LaNotaEsperadaSeGuardaEnMayusculaYSeVuelveAVerAlRecargar()
    {
        using var app = new AppConDatosFactory();
        await app.InitializeAsync();
        using var c = app.CreateClient();
        await PostAsync(c, "/Planificador/Generar", ("id", "1"));
        var codigo = await UnaMateriaDelPlanAsync(app);

        var (r, cuerpo) = await PostAsync(c, "/Planificador/NotaEsperada", ("codigo", codigo.ToLowerInvariant()), ("nota", "b"));

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Contains($"{codigo}: se espera B.", cuerpo);
        var pagina = WebUtility.HtmlDecode(await c.GetStringAsync("/Planificador"));
        var fila = Regex.Match(pagina, $"<select[^>]*data-codigo=\"{codigo}\"[\\s\\S]*?</select>").Value;
        Assert.Matches("<option value=\"B\" selected", fila);
        Assert.DoesNotMatch("<option value=\"A\" selected", fila);
        Assert.Equal("B", (await EstadoAsync(app)).Simulador!.Filas.Single(f => f.Codigo == codigo).Letra);
    }

    [Fact]
    public async Task UnaNotaEsperadaVaciaLaQuitaYUnaInvalidaSeRechaza()
    {
        using var app = new AppConDatosFactory();
        await app.InitializeAsync();
        using var c = app.CreateClient();
        await PostAsync(c, "/Planificador/Generar", ("id", "1"));
        var codigo = await UnaMateriaDelPlanAsync(app);
        await PostAsync(c, "/Planificador/NotaEsperada", ("codigo", codigo), ("nota", "A"));

        var (mala, cuerpoMala) = await PostAsync(c, "/Planificador/NotaEsperada", ("codigo", codigo), ("nota", "Z"));
        Assert.Equal(HttpStatusCode.BadRequest, mala.StatusCode);
        Assert.Contains("no es una calificación de esta universidad", cuerpoMala);
        Assert.Equal("A", (await EstadoAsync(app)).Simulador!.Filas.Single(f => f.Codigo == codigo).Letra);   // lo anterior queda

        var (desconocida, _) = await PostAsync(c, "/Planificador/NotaEsperada", ("codigo", "ZZZ999"), ("nota", "A"));
        Assert.Equal(HttpStatusCode.BadRequest, desconocida.StatusCode);

        var (vacia, cuerpoVacia) = await PostAsync(c, "/Planificador/NotaEsperada", ("codigo", codigo), ("nota", ""));
        Assert.Equal(HttpStatusCode.OK, vacia.StatusCode);
        Assert.Contains("sin nota esperada", cuerpoVacia);
        Assert.Null((await EstadoAsync(app)).Simulador!.Filas.Single(f => f.Codigo == codigo).Letra);
    }

    [Fact]
    public async Task ElSimuladorSeDibujaConLasMateriasEnCursoYLasPlanificadasYSuFraseEsLaDelNucleo()
    {
        using var app = new AppConDatosFactory();
        await app.InitializeAsync();
        using var c = app.CreateClient();
        await PostAsync(c, "/Planificador/Generar", ("id", "1"));
        var st = await EstadoAsync(app);
        var sim = st.Simulador!;
        var codigo = sim.Filas.Last().Codigo;
        await PostAsync(c, "/Planificador/NotaEsperada", ("codigo", codigo), ("nota", "A"));

        var html = await c.GetStringAsync("/Planificador");
        var pagina = WebUtility.HtmlDecode(html);
        sim = (await EstadoAsync(app)).Simulador!;

        Assert.Contains("Simulador de índice", pagina);
        Assert.Equal(sim.Filas.Count, Regex.Matches(pagina, "class=\"form-select form-select-sm sim-nota\"").Count);
        Assert.Contains(sim.Actual.HorasPga.ToString(System.Globalization.CultureInfo.InvariantCulture), Regex.Match(pagina, "id=\"simulador\"[^>]*").Value);
        var letraMaxima = sim.Escala.Letras.Where(l => l.CuentaParaIndice).OrderByDescending(l => l.Puntos).First().Letra;
        var frase = SimuladorIndice.Describir(sim.Resultado, sim.Objetivo, letraMaxima);
        Assert.Contains(frase, pagina);
        Assert.Contains($">{sim.Resultado.IndiceProyectado.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}<", pagina);
        // Una nota esperada movió el proyectado (o lo dejó igual si esa A no lo cambia): nunca por debajo del actual con A.
        Assert.True(sim.Resultado.IndiceProyectado >= sim.Actual.Indice || sim.Resultado.IndiceProyectado == sim.Actual.Indice);
        Assert.Contains(sim.Filas, f => f.Grupo.StartsWith("En curso") || Regex.IsMatch(f.Grupo, @"^(ENE-ABR|MAY-AGO|SEP-DIC) \d{4}$"));
    }

    [Fact]
    public async Task LasMateriasSinPlanificarEntranEnLoQueFaltaAunqueNoTenganFila()
    {
        using var app = new AppConDatosFactory();
        await app.InitializeAsync();
        using var c = app.CreateClient();
        await c.GetStringAsync("/Planificador");   // escenario vacío: nada planificado

        var sim = (await EstadoAsync(app)).Simulador!;

        Assert.True(sim.MateriasSinFila > 0);
        Assert.True(sim.CreditosSinFila > 0);
        Assert.Equal(sim.CreditosSinFila + sim.Filas.Sum(f => f.Creditos), sim.Resultado.CreditosPorDefinir);   // sin notas puestas, todo falta por definir
        var pagina = WebUtility.HtmlDecode(await c.GetStringAsync("/Planificador"));
        Assert.Contains("id=\"sim-resto\"", pagina);
        Assert.Contains("entran en «lo que falta»", pagina);
    }
}
