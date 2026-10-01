using System.Net;
using System.Text.RegularExpressions;
using HistorialAcademico.Core.Planificacion;
using HistorialAcademico.Web.Data;
using HistorialAcademico.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HistorialAcademico.Tests;

/// <summary>Qué cambió en la situación académica desde la última vez que se miró un plan.</summary>
public class ReconciliadorPlanTests
{
    private static InstantaneaPlan I(string pensum = "unapec/x-1.json", bool asumir = true, string[]? pendientes = null, string? primero = "ENE-ABR 2027", string? graduacion = "SEP-DIC 2028") =>
        new(pensum, asumir, (pendientes ?? new[] { "AAA100", "BBB200", "CCC300" }).ToList(), primero, graduacion);

    [Fact]
    public void LaPrimeraVezNoHayConQueCompararSoloSeCuentanLasQueSalieron()
    {
        var c = ReconciliadorPlan.Comparar(null, I(), new[] { "ZZZ100" });

        Assert.Equal(new[] { "ZZZ100" }, c.Salieron);
        Assert.Empty(c.Volvieron);
        Assert.False(c.PrimeroCambio);
        Assert.True(c.Hay);
        Assert.Equal("Salieron del plan porque ya están aprobadas o en curso: ZZZ100.", c.Frases()[0]);
    }

    [Fact]
    public void SinCambiosNoHayNadaQueContar()
    {
        var c = ReconciliadorPlan.Comparar(I(), I(), Array.Empty<string>());

        Assert.False(c.Hay);
        Assert.Empty(c.Frases());
    }

    [Fact]
    public void LasQueAntesNoFaltabanYAhoraSiVolvieron()
    {
        var antes = I(pendientes: new[] { "AAA100", "CCC300" });
        var ahora = I(pendientes: new[] { "AAA100", "BBB200", "CCC300" });

        var c = ReconciliadorPlan.Comparar(antes, ahora, Array.Empty<string>());

        Assert.Equal(new[] { "BBB200" }, c.Volvieron);
        Assert.Contains("Volvieron a «Por planificar» porque no quedaron aprobadas: BBB200.", c.Frases());
    }

    [Fact]
    public void SiLaGraduacionSeMovioSeDiceDeDondeADonde()
    {
        var c = ReconciliadorPlan.Comparar(I(graduacion: "SEP-DIC 2028"), I(pendientes: new[] { "AAA100", "BBB200" }, graduacion: "MAY-AGO 2028"), new[] { "CCC300" });

        Assert.Equal("Tu graduación estimada pasó de SEP-DIC 2028 a MAY-AGO 2028.", c.Frases().Last());
    }

    [Fact]
    public void SiCambioAlgoPeroLaGraduacionSigueIgualSeDiceQueSigue()
    {
        var c = ReconciliadorPlan.Comparar(I(), I(pendientes: new[] { "AAA100", "BBB200" }), new[] { "CCC300" });

        Assert.Equal("Tu graduación estimada sigue en SEP-DIC 2028.", c.Frases().Last());
    }

    [Fact]
    public void ElNuevoPrimerPeriodoSeAvisa()
    {
        var c = ReconciliadorPlan.Comparar(I(primero: "ENE-ABR 2027"), I(primero: "MAY-AGO 2027"), Array.Empty<string>());

        Assert.True(c.PrimeroCambio);
        Assert.True(c.Hay);
        Assert.Contains("El primer período planificable ahora es MAY-AGO 2027.", c.Frases());
    }

    [Fact]
    public void UnaGraduacionQueSeMuevePorSiSolaNoEsUnCambioDeLaSituacion()
    {
        // Solo cambia la graduación (por ejemplo, porque se editó el límite de créditos): no es algo que haya pasado en Banner.
        var c = ReconciliadorPlan.Comparar(I(graduacion: "SEP-DIC 2028"), I(graduacion: "MAY-AGO 2028"), Array.Empty<string>());

        Assert.False(c.Hay);
        Assert.Empty(c.Frases());
    }

    [Theory]
    [InlineData(true)]   // otro pénsum
    [InlineData(false)]  // otra opción de «asumir que apruebo lo que cursó»
    public void SiCambianElPensumOLasReglasNoSeCuentanComoMateriasQueVolvieron(bool cambiaPensum)
    {
        var antes = I(pendientes: new[] { "AAA100" });
        var ahora = cambiaPensum ? I(pensum: "otra/carrera-1.json") : I(asumir: false);

        var c = ReconciliadorPlan.Comparar(antes, ahora, Array.Empty<string>());

        Assert.Empty(c.Volvieron);
        Assert.False(c.Hay);
    }

    [Fact]
    public void LasListasLargasSeAcortan()
    {
        var muchas = Enumerable.Range(1, 12).Select(i => $"M{i:00}").ToList();

        var c = ReconciliadorPlan.Comparar(I(), I(), muchas);

        Assert.Contains("M01, M02, M03, M04, M05, M06, M07, M08 y 4 más.", c.Frases()[0]);
    }

    [Fact]
    public void LasCodigosSeOrdenanYLaComparacionNoDistingueMayusculas()
    {
        var c = ReconciliadorPlan.Comparar(I(pendientes: new[] { "aaa100" }), I(pendientes: new[] { "AAA100", "ZZZ9", "BBB2" }), new[] { "mmm1", "ccc3" });

        Assert.Equal(new[] { "BBB2", "ZZZ9" }, c.Volvieron);
        Assert.Equal(new[] { "ccc3", "mmm1" }, c.Salieron);
    }

    [Fact]
    public void LaInstantaneaSeGuardaYSeLeeYUnTextoRotoEsNadaGuardado()
    {
        var i = I();

        Assert.Equal(i.Pendientes, InstantaneaPlan.DeTexto(i.ATexto())!.Pendientes);
        Assert.Equal(i.Graduacion, InstantaneaPlan.DeTexto(i.ATexto())!.Graduacion);
        Assert.Null(InstantaneaPlan.DeTexto(null));
        Assert.Null(InstantaneaPlan.DeTexto("   "));
        Assert.Null(InstantaneaPlan.DeTexto("{ esto no es json"));
    }
}

/// <summary>Tras sincronizar (o cambiar lo que aprobaste), el plan se ajusta solo y la aplicación cuenta qué cambió.</summary>
public class PlanTrasSincronizarTests
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

    /// <summary>Una materia que el plan pone en el primer período (ni en curso ni aprobada todavía).</summary>
    private static async Task<string> UnaMateriaDelPrimerPeriodoAsync(AppConDatosFactory app)
    {
        var st = await EstadoAsync(app);
        return st.Plan.First(p => p.Codigos.Count > 0).Codigos.First(c => st.Contexto!.PorCodigo[c].Materia.Creditos > 0);   // una con créditos: así se nota en el total
    }

    /// <summary>Dice que la materia ya se aprobó en un período pasado (registrándola a mano, que es la forma de cambiar lo aprobado en las pruebas).</summary>
    private static async Task RegistrarAsync(AppConDatosFactory app, string codigo, string nota)
    {
        using var scope = app.Services.CreateScope();
        var r = await scope.ServiceProvider.GetRequiredService<MateriasManualesService>().AgregarAsync(codigo, "ENE-ABR 2000", nota);
        Assert.True(r.Ok, r.Mensaje);
    }

    private static async Task<HttpClient> ConPlanAsync(AppConDatosFactory app)
    {
        await app.InitializeAsync();
        var c = app.CreateClient();
        await PostAsync(c, "/Planificador/Generar", ("id", "1"), ("carga", "pesada"));
        await c.GetStringAsync("/Planificador");   // se mira el plan: queda guardado cómo estaba todo
        return c;
    }

    [Fact]
    public async Task SinCambiosNoHayAvisosAunqueSeMireElPlanVariasVeces()
    {
        using var app = new AppConDatosFactory();
        using var c = await ConPlanAsync(app);

        var otra = WebUtility.HtmlDecode(await c.GetStringAsync("/Planificador"));
        var y_otra = WebUtility.HtmlDecode(await c.GetStringAsync("/Planificador"));

        Assert.DoesNotContain("id=\"avisos-plan\"", otra);
        Assert.DoesNotContain("id=\"avisos-plan\"", y_otra);
    }

    [Fact]
    public async Task LoQueSeApruebaSaleDelPlanYSeCuentaConLaGraduacion()
    {
        using var app = new AppConDatosFactory();
        using var c = await ConPlanAsync(app);
        var codigo = await UnaMateriaDelPrimerPeriodoAsync(app);
        var creditosAntes = (await EstadoAsync(app)).Evaluacion!.CreditosFaltantes;

        await RegistrarAsync(app, codigo, "A");   // «la aprobé»
        var html = WebUtility.HtmlDecode(await c.GetStringAsync("/Planificador"));

        Assert.Contains("id=\"avisos-plan\"", html);
        Assert.Contains("Tu plan cambió desde la última vez que lo miraste", html);
        Assert.Contains("Plan 1", html);
        Assert.Contains($"Salieron del plan porque ya están aprobadas o en curso: {codigo}.", html);
        Assert.Matches(@"Tu graduación estimada (pasó de [A-Z]{3}-[A-Z]{3} \d{4} a [A-Z]{3}-[A-Z]{3} \d{4}|sigue en [A-Z]{3}-[A-Z]{3} \d{4})\.", html);
        var st = await EstadoAsync(app);
        Assert.DoesNotContain(st.Plan.SelectMany(p => p.Codigos), c2 => c2.Equals(codigo, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(st.PorPlanificar, m => m.Materia.Codigo == codigo);   // no falta: no vuelve a la bandeja
        Assert.True(st.Evaluacion!.CreditosFaltantes < creditosAntes);
    }

    [Fact]
    public async Task ElAvisoSeDescartaConEntendidoYNoVuelveAAparecer()
    {
        using var app = new AppConDatosFactory();
        using var c = await ConPlanAsync(app);
        await RegistrarAsync(app, await UnaMateriaDelPrimerPeriodoAsync(app), "B");
        Assert.Contains("id=\"avisos-plan\"", await c.GetStringAsync("/Planificador"));

        var despues = await PostAsync(c, "/Planificador/DescartarAvisos", ("id", "1"));

        Assert.DoesNotContain("id=\"avisos-plan\"", despues);
        Assert.DoesNotContain("id=\"avisos-plan\"", await c.GetStringAsync("/Planificador"));   // ya está ajustado: nada nuevo que avisar
        using var scope = app.Services.CreateScope();
        Assert.All(await scope.ServiceProvider.GetRequiredService<HistorialContext>().AvisosPlan.ToListAsync(), a => Assert.True(a.Leido));
    }

    [Fact]
    public async Task UnaMateriaQueNoQuedoAprobadaVuelveALaBandejaYSeAvisa()
    {
        using var app = new AppConDatosFactory();
        using var c = await ConPlanAsync(app);
        var codigo = await UnaMateriaDelPrimerPeriodoAsync(app);
        await RegistrarAsync(app, codigo, "A");
        await c.GetStringAsync("/Planificador");                                 // el plan se ajusta: la materia sale
        await PostAsync(c, "/Planificador/DescartarAvisos", ("id", "1"));

        await RegistrarAsync(app, codigo, "F");                                  // resulta que la reprobó
        var html = WebUtility.HtmlDecode(await c.GetStringAsync("/Planificador"));

        Assert.Contains($"Volvieron a «Por planificar» porque no quedaron aprobadas: {codigo.ToUpperInvariant()}.", html);
        var st = await EstadoAsync(app);
        Assert.Contains(st.PorPlanificar, m => m.Materia.Codigo == codigo);   // está en la bandeja, lista para volver a planificarla
        Assert.DoesNotContain(st.Plan.SelectMany(p => p.Codigos), x => x.Equals(codigo, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task TodosLosEscenariosSeAjustanNoSoloElQueSeMira()
    {
        using var app = new AppConDatosFactory();
        using var c = await ConPlanAsync(app);
        await PostAsync(c, "/Planificador/GuardarComo", ("id", "1"), ("nombre", "Otro escenario"));
        await c.GetStringAsync("/Planificador");
        await PostAsync(c, "/Planificador/DescartarAvisos", ("id", "1"));
        var codigo = await UnaMateriaDelPrimerPeriodoAsync(app);

        await RegistrarAsync(app, codigo, "A");
        await c.GetStringAsync("/Planificador?id=1");   // solo se mira el plan 1

        var otro = (await EstadoAsync(app)).Planes.Single(p => p.Nombre == "Otro escenario");
        Assert.DoesNotContain(otro.Periodos.SelectMany(p => p.Materias), m => m.Codigo.Equals(codigo, StringComparison.OrdinalIgnoreCase));
        using var scope = app.Services.CreateScope();
        var avisos = await scope.ServiceProvider.GetRequiredService<HistorialContext>().AvisosPlan.Where(a => !a.Leido).ToListAsync();
        Assert.Equal(new[] { "Otro escenario", "Plan 1" }, avisos.Select(a => a.PlanNombre).OrderBy(n => n).ToArray());
    }

    [Fact]
    public async Task LoQueEditasTuNoCuentaComoUnCambioDeLaSituacion()
    {
        using var app = new AppConDatosFactory();
        using var c = await ConPlanAsync(app);
        var codigo = await UnaMateriaDelPrimerPeriodoAsync(app);

        await PostAsync(c, "/Planificador/Asignar", ("id", "1"), ("codigo", codigo), ("periodo", ""));   // la sacas tú del plan
        await PostAsync(c, "/Planificador/Configurar", ("id", "1"), ("limiteBase", "20"), ("limiteAlto", "22"), ("umbralIndice", "3.40"), ("minimo", "0"), ("asumirEnCurso", "true"));
        var html = WebUtility.HtmlDecode(await c.GetStringAsync("/Planificador"));

        Assert.DoesNotContain("id=\"avisos-plan\"", html);
    }

    [Fact]
    public async Task UnaMateriaQueNoExisteEnElPenumsNoSeToca()
    {
        using var app = new AppConDatosFactory();
        using var c = await ConPlanAsync(app);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HistorialContext>();
            var plan = await db.PlanesEstudio.Include(p => p.Periodos).ThenInclude(p => p.Materias).FirstAsync(p => p.Id == 1);
            plan.Periodos.First().Materias.Add(new() { Codigo = "NOEXISTE1" });
            await db.SaveChangesAsync();
        }

        await c.GetStringAsync("/Planificador");

        var st = await EstadoAsync(app);
        Assert.Contains(st.Plan.SelectMany(p => p.Codigos), x => x == "NOEXISTE1");   // el aviso «no existe en el pénsum» lo resuelve la persona
    }

    [Fact]
    public async Task TrasSincronizarConBannerLaPantallaDicePorAdelantadoQueCambioEnElPlan()
    {
        using var app = new AppConBannerFalsoFactory();
        app.Banner.Captura = () => Task.FromResult(Muestras.Sintetico);
        await app.InitializeAsync();
        using var c = app.CreateClient();
        var t = await TokenAsync(c);
        await c.PostAsync("/Planificador/Generar", new FormUrlEncodedContent(new Dictionary<string, string> { ["id"] = "1", ["carga"] = "pesada", ["__RequestVerificationToken"] = t }));
        await c.GetStringAsync("/Planificador");
        string codigo;
        using (var scope = app.Services.CreateScope())
        {
            var st = await scope.ServiceProvider.GetRequiredService<PlanificadorService>().ObtenerAsync(1);
            codigo = st.Plan.First(p => p.Codigos.Count > 0).Codigos[0];
            Assert.True((await scope.ServiceProvider.GetRequiredService<MateriasManualesService>().AgregarAsync(codigo, "ENE-ABR 2000", "A")).Ok);
        }

        var respuesta = await c.PostAsync("/Sincronizaciones/Actualizar", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = t }));
        var html = WebUtility.HtmlDecode(await respuesta.Content.ReadAsStringAsync());

        Assert.Contains("Sincronizado:", html);
        var mensaje = Regex.Match(html, "Sincronizado:[^<]*").Value;
        Assert.True(mensaje.Contains("Tu plan cambió: Salieron del plan porque ya están aprobadas o en curso: " + codigo), $"mensaje: {mensaje}");
    }

    [Fact]
    public async Task UnaSincronizacionQueFallaNoTocaElPlan()
    {
        using var app = new AppConBannerFalsoFactory();
        app.Banner.Captura = () => throw new HistorialAcademico.Banner.BannerException("Banner no respondió.");
        await app.InitializeAsync();
        using var c = app.CreateClient();
        var t = await TokenAsync(c);

        var respuesta = await c.PostAsync("/Sincronizaciones/Actualizar", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = t }));
        var html = WebUtility.HtmlDecode(await respuesta.Content.ReadAsStringAsync());

        Assert.Contains("Banner no respondió.", html);
        Assert.DoesNotContain("Tu plan cambió", html);
    }
}
