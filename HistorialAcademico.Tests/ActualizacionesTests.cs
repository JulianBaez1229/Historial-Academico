using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using HistorialAcademico.Core.Actualizaciones;
using HistorialAcademico.Core.Perfiles;
using HistorialAcademico.Web.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HistorialAcademico.Tests;

public class VersionAppTests
{
    [Theory]
    [InlineData("1.2.3", 1, 2, 3, null)]
    [InlineData("v1.2.3", 1, 2, 3, null)]
    [InlineData("V10.0.25", 10, 0, 25, null)]
    [InlineData("1.2.3-beta.1", 1, 2, 3, "beta.1")]
    [InlineData("v2.0.0-rc1+abc123", 2, 0, 0, "rc1")]
    [InlineData("0.0.0-desarrollo", 0, 0, 0, "desarrollo")]
    [InlineData(" 1.0.0 ", 1, 0, 0, null)]
    public void LeeLasVersionesConYSinV(string texto, int mayor, int menor, int parche, string? prueba)
    {
        Assert.True(VersionApp.TryParse(texto, out var v));
        Assert.Equal(new VersionApp(mayor, menor, parche, prueba), v);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("1.2")]
    [InlineData("1.2.x")]
    [InlineData("latest")]
    [InlineData("v1.2.3.4")]
    [InlineData("1.2.3-")]
    [InlineData("1.2.3 && calc")]
    [InlineData("99999999.0.0")]
    public void RechazaLoQueNoEsUnaVersion(string? texto) => Assert.False(VersionApp.TryParse(texto, out _));

    [Theory]
    [InlineData("1.0.0", "1.0.1", true)]
    [InlineData("1.0.9", "1.1.0", true)]
    [InlineData("1.9.9", "2.0.0", true)]
    [InlineData("1.10.0", "1.9.0", false)]           // se compara como número, no como texto
    [InlineData("1.0.0", "1.0.0", false)]
    [InlineData("1.0.0-beta.1", "1.0.0", true)]      // la estable es más nueva que su prueba
    [InlineData("1.0.0", "1.0.0-beta.1", false)]
    [InlineData("1.0.0-beta.2", "1.0.0-beta.10", true)]
    [InlineData("1.0.0-alpha", "1.0.0-beta", true)]
    [InlineData("1.0.0-beta", "1.0.0-beta.1", true)]
    public void SabeCualEsMasNueva(string actual, string publicada, bool hayNueva) => Assert.Equal(hayNueva, VersionApp.HayNueva(actual, publicada));

    [Theory]
    [InlineData("0.0.0-desarrollo")]
    [InlineData("0.0.0")]
    [InlineData("no es versión")]
    public void QuienCompilaDesdeElCodigoOTieneUnaVersionRaraNoRecibeAvisos(string actual) => Assert.False(VersionApp.HayNueva(actual, "9.9.9"));

    [Fact]
    public void UnaPublicadaQueNoSeEntiendeNoAvisaDeNada() => Assert.False(VersionApp.HayNueva("1.0.0", "muy nueva"));

    [Fact]
    public void ToStringNoLlevaLaV() => Assert.Equal("1.2.3-beta.1", new VersionApp(1, 2, 3, "beta.1").ToString());
}

public class AlmacenPreferenciasTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ha-pref-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    [Fact]
    public void SinArchivoLosAvisosEstanEncendidos()
    {
        var p = new AlmacenPreferencias(_dir).Cargar();

        Assert.True(p.AvisarActualizaciones);
        Assert.Null(p.VersionIgnorada);
    }

    [Fact]
    public void LoQueSeCambiaSeConservaEntreInstancias()
    {
        Assert.True(new AlmacenPreferencias(_dir).Cambiar(p => { p.AvisarActualizaciones = false; p.VersionIgnorada = "2.0.0"; }));

        var otra = new AlmacenPreferencias(_dir).Cargar();

        Assert.False(otra.AvisarActualizaciones);
        Assert.Equal("2.0.0", otra.VersionIgnorada);
        Assert.False(File.Exists(Path.Combine(_dir, "preferencias.json.tmp")));   // no queda archivo temporal
    }

    [Fact]
    public void UnArchivoDanadoEquivaleALasPreferenciasPorOmisionYNoImpideArrancar()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "preferencias.json"), "{ esto no es json");
        var almacen = new AlmacenPreferencias(_dir);

        Assert.True(almacen.Cargar().AvisarActualizaciones);
        Assert.True(almacen.Cambiar(p => p.AvisarActualizaciones = false));      // y se puede volver a guardar encima
        Assert.False(almacen.Cargar().AvisarActualizaciones);
    }

    [Fact]
    public void SiNoSePuedeEscribirDiceFalseEnVezDeRomperse()
    {
        Directory.CreateDirectory(_dir);
        Directory.CreateDirectory(Path.Combine(_dir, "preferencias.json"));   // una carpeta donde debería ir el archivo

        Assert.False(new AlmacenPreferencias(_dir).Cambiar(p => p.AvisarActualizaciones = false));
    }
}

/// <summary>Un servidor de mentira: cuenta las peticiones, guarda la última y responde lo que se le diga.</summary>
public sealed class ManejadorFalso : HttpMessageHandler
{
    public int Peticiones;
    public HttpRequestMessage? Ultima;
    public Func<HttpResponseMessage> Responder = () => Release("v2.0.0");

    public static HttpResponseMessage Json(string json, HttpStatusCode estado = HttpStatusCode.OK) =>
        new(estado) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public static HttpResponseMessage Release(string etiqueta, string repo = "dueno/repo", bool prueba = false, bool borrador = false, string? url = null) =>
        Json($"{{\"tag_name\":\"{etiqueta}\",\"prerelease\":{prueba.ToString().ToLowerInvariant()},\"draft\":{borrador.ToString().ToLowerInvariant()}," +
             $"\"html_url\":\"{url ?? $"https://github.com/{repo}/releases/tag/{etiqueta}"}\"}}");

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref Peticiones);
        Ultima = request;
        return Task.FromResult(Responder());
    }
}

public class ActualizacionesServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ha-act-" + Guid.NewGuid().ToString("N"));
    private readonly AlmacenPreferencias _pref;
    private readonly ManejadorFalso _http = new();

    public ActualizacionesServiceTests() => _pref = new AlmacenPreferencias(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private static IConfiguration Config(bool activas = true, string? repo = null) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Actualizaciones:Activas"] = activas.ToString(),
            ["Actualizaciones:Repositorio"] = repo,
        }).Build();

    private ActualizacionesService Servicio(bool activas = true, string? repo = "dueno/repo", string version = "1.0.0", string? incorporado = null) =>
        new(Config(activas, repo), _pref, _http, version, incorporado);

    // ── Cuándo consulta y cuándo no ───────────────────────────────────────────────────────

    [Fact]
    public async Task UnaVersionMasNuevaDejaUnAvisoConSuEnlaceAlRelease()
    {
        using var s = Servicio();

        await s.ConsultarAsync();

        Assert.Equal(EstadoConsulta.Nueva, s.Estado);
        var aviso = s.Aviso!;
        Assert.Equal("2.0.0", aviso.Version);
        Assert.Equal("1.0.0", aviso.Actual);
        Assert.Equal("https://github.com/dueno/repo/releases/tag/v2.0.0", aviso.Url);
    }

    [Fact]
    public async Task HaceUnaSolaPeticionGetSinCredencialesNiDatosDeLaPersona()
    {
        using var s = Servicio();

        await s.ConsultarAsync();
        await s.ConsultarAsync();                                    // abrir otra vez no repite la consulta

        Assert.Equal(1, _http.Peticiones);
        var pedido = _http.Ultima!;
        Assert.Equal(HttpMethod.Get, pedido.Method);
        Assert.Equal("https://api.github.com/repos/dueno/repo/releases/latest", pedido.RequestUri!.ToString());
        Assert.Null(pedido.Headers.Authorization);
        Assert.False(pedido.Headers.Contains("Cookie"));
        Assert.Equal("HistorialAcademico/1.0.0", pedido.Headers.UserAgent.ToString());
        Assert.Null(pedido.Content);
    }

    [Fact]
    public async Task ForzarVuelveAConsultarComoElBotonBuscarAhora()
    {
        using var s = Servicio();
        await s.ConsultarAsync();

        await s.ConsultarAsync(forzar: true);

        Assert.Equal(2, _http.Peticiones);
    }

    [Theory]
    [InlineData("v1.0.0")]          // la misma
    [InlineData("v0.9.0")]          // una anterior
    public async Task SiLaUltimaNoEsMasNuevaEstaAlDia(string etiqueta)
    {
        _http.Responder = () => ManejadorFalso.Release(etiqueta);
        using var s = Servicio();

        await s.ConsultarAsync();

        Assert.Equal(EstadoConsulta.AlDia, s.Estado);
        Assert.Null(s.Aviso);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task UnaPruebaOUnBorradorNoSeOfrecenComoActualizacion(bool prueba, bool borrador)
    {
        _http.Responder = () => ManejadorFalso.Release("v9.0.0-beta.1", prueba: prueba, borrador: borrador);
        using var s = Servicio();

        await s.ConsultarAsync();

        Assert.Null(s.Aviso);
    }

    [Fact]
    public async Task SiLaDireccionDelReleaseNoEsDeEseRepositorioSeUsaLaDeReleasesDelRepositorio()
    {
        _http.Responder = () => ManejadorFalso.Release("v2.0.0", url: "https://sitio-malicioso.example/descarga.exe");
        using var s = Servicio();

        await s.ConsultarAsync();

        Assert.Equal("https://github.com/dueno/repo/releases/latest", s.Aviso!.Url);
    }

    [Fact]
    public async Task SinLaActivacionDelProgramaPublicadoNoSeConectaANadaNiHayAviso()
    {
        using var s = Servicio(activas: false);

        await s.ConsultarAsync(forzar: true);

        Assert.Equal(0, _http.Peticiones);
        Assert.False(s.Disponible);
        Assert.Null(s.Aviso);
    }

    [Fact]
    public async Task ConLosAvisosApagadosNoSeConectaANada()
    {
        _pref.Cambiar(p => p.AvisarActualizaciones = false);
        using var s = Servicio();

        await s.ConsultarAsync(forzar: true);

        Assert.Equal(0, _http.Peticiones);
        Assert.True(s.Disponible);
        Assert.False(s.Habilitada);
    }

    [Theory]
    [InlineData("0.0.0-desarrollo")]
    [InlineData("0.0.0")]
    public async Task QuienCompilaDesdeElCodigoNoConsulta(string version)
    {
        using var s = Servicio(version: version);

        await s.ConsultarAsync(forzar: true);

        Assert.Equal(0, _http.Peticiones);
        Assert.False(s.Disponible);
    }

    [Fact]
    public async Task SinRepositorioNoHayAAdondeConsultar()
    {
        using var s = Servicio(repo: null);

        await s.ConsultarAsync(forzar: true);

        Assert.Equal(0, _http.Peticiones);
        Assert.Null(s.Repositorio);
    }

    [Fact]
    public void ElRepositorioIncorporadoAlArmarElProgramaSeUsaSiLaConfiguracionNoDiceOtro()
    {
        using var propio = Servicio(repo: null, incorporado: "usuario/historial");
        using var configurado = Servicio(repo: "otra/cosa", incorporado: "usuario/historial");

        Assert.Equal("usuario/historial", propio.Repositorio);
        Assert.Equal("otra/cosa", configurado.Repositorio);
    }

    [Theory]
    [InlineData("https://evil.example/x")]
    [InlineData("a/b/c")]
    [InlineData("../../etc")]
    [InlineData("solo-un-nombre")]
    [InlineData("dueno/repo?x=1")]
    [InlineData("dueno/repo#frag")]
    [InlineData("dueno /repo")]
    [InlineData("-dueno/repo")]
    public void UnRepositorioConFormaRaraSeIgnoraEnVezDeArmarUnaDireccionConEl(string repo)
    {
        using var s = Servicio(repo: repo);

        Assert.Null(s.Repositorio);
        Assert.False(s.Disponible);
    }

    [Theory]
    [InlineData("dueno/repo")]
    [InlineData("Dueno-1/mi.repo_2")]
    public void UnRepositorioNormalSeAcepta(string repo)
    {
        using var s = Servicio(repo: repo);

        Assert.Equal(repo, s.Repositorio);
    }

    // ── Cuando algo sale mal: nada de avisos y nada que se rompa ─────────────────────────

    [Fact]
    public async Task UnRepositorioSinReleasesDiceQueTodaviaNoHay()
    {
        _http.Responder = () => ManejadorFalso.Json("{\"message\":\"Not Found\"}", HttpStatusCode.NotFound);
        using var s = Servicio();

        await s.ConsultarAsync();

        Assert.Equal(EstadoConsulta.Fallo, s.Estado);
        Assert.Contains("ninguna versión publicada", s.UltimoError);
        Assert.Null(s.Aviso);
    }

    [Fact]
    public async Task UnErrorDelServidorSeCuentaConSuCodigo()
    {
        _http.Responder = () => ManejadorFalso.Json("{}", HttpStatusCode.Forbidden);
        using var s = Servicio();

        await s.ConsultarAsync();

        Assert.Equal("GitHub respondió 403.", s.UltimoError);
    }

    [Fact]
    public async Task SinInternetNoSeMuestraNadaYNoSeLanzaNada()
    {
        _http.Responder = () => throw new HttpRequestException("sin red");
        using var s = Servicio();

        await s.ConsultarAsync();

        Assert.Equal(EstadoConsulta.Fallo, s.Estado);
        Assert.Contains("sin internet", s.UltimoError);
        Assert.Null(s.Aviso);
    }

    [Fact]
    public async Task SiGitHubNoRespondeATiempoSeRindeSinLanzar()
    {
        _http.Responder = () => throw new TaskCanceledException("tiempo");
        using var s = Servicio();

        await s.ConsultarAsync();

        Assert.Equal("GitHub no respondió a tiempo.", s.UltimoError);
    }

    [Theory]
    [InlineData("esto no es json")]
    [InlineData("[]")]
    [InlineData("{\"tag_name\":42}")]
    [InlineData("{\"tag_name\":\"nada\"}")]
    [InlineData("{}")]
    public async Task UnaRespuestaRaraNuncaDejaUnAviso(string cuerpo)
    {
        _http.Responder = () => ManejadorFalso.Json(cuerpo);
        using var s = Servicio();

        await s.ConsultarAsync();

        Assert.Null(s.Aviso);
    }

    [Fact]
    public async Task UnaRespuestaEnormeSeRechaza()
    {
        _http.Responder = () => ManejadorFalso.Json("{\"tag_name\":\"v2.0.0\",\"relleno\":\"" + new string('x', 300 * 1024) + "\"}");
        using var s = Servicio();

        await s.ConsultarAsync();

        Assert.Equal(EstadoConsulta.Fallo, s.Estado);
        Assert.Null(s.Aviso);
    }

    [Fact]
    public async Task DespuesDeUnFalloSePuedeVolverAConsultarAMano()
    {
        _http.Responder = () => throw new HttpRequestException("sin red");
        using var s = Servicio();
        await s.ConsultarAsync();
        _http.Responder = () => ManejadorFalso.Release("v2.0.0");

        await s.ConsultarAsync(forzar: true);

        Assert.Equal(EstadoConsulta.Nueva, s.Estado);
        Assert.Null(s.UltimoError);
    }

    // ── Ignorar y apagar ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task IgnorarUnaVersionQuitaElAvisoHastaQueSalgaUnaMasNueva()
    {
        using var s = Servicio();
        await s.ConsultarAsync();

        Assert.True(s.Ignorar("2.0.0"));
        Assert.Null(s.Aviso);

        _http.Responder = () => ManejadorFalso.Release("v2.1.0");
        await s.ConsultarAsync(forzar: true);
        Assert.Equal("2.1.0", s.Aviso!.Version);
    }

    [Fact]
    public async Task ApagarLosAvisosOlvidaLoQueSabiaYNoVuelveAConsultar()
    {
        using var s = Servicio();
        await s.ConsultarAsync();

        Assert.True(s.Avisar(false));
        await s.ConsultarAsync(forzar: true);

        Assert.Null(s.Aviso);
        Assert.Equal(EstadoConsulta.NoConsultada, s.Estado);
        Assert.Equal(1, _http.Peticiones);
    }

    [Fact]
    public void LaVersionDelProgramaCompiladoDesdeElCodigoEsDeDesarrolloYNoLlevaElHashDelCommit()
    {
        var version = ActualizacionesService.VersionDelPrograma();

        Assert.DoesNotContain("+", version);
        Assert.True(VersionApp.TryParse(version, out _), version);
    }
}

/// <summary>La aplicación como programa publicado: con los avisos encendidos y un GitHub de mentira.</summary>
public class AppConActualizacionesFactory : AppConDatosFactory
{
    public ManejadorFalso Http { get; } = new();
    public string Version { get; set; } = "1.0.0";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Actualizaciones:Activas", "true");
        builder.UseSetting("Actualizaciones:Repositorio", "dueno/repo");
        builder.ConfigureServices(s =>
        {
            s.RemoveAll<ActualizacionesService>();
            s.AddSingleton(sp => new ActualizacionesService(sp.GetRequiredService<IConfiguration>(), sp.GetRequiredService<AlmacenPreferencias>(), Http, Version));
        });
    }
}

public class ActualizacionesPantallasTests
{
    private static async Task<string> TokenAsync(HttpClient c, string url = "/MisDatos") =>
        Regex.Match(await c.GetStringAsync(url), "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;

    private static async Task<HttpResponseMessage> PostAsync(HttpClient c, string url, params (string, string)[] campos)
    {
        var datos = campos.Select(x => new KeyValuePair<string, string>(x.Item1, x.Item2)).Append(new("__RequestVerificationToken", await TokenAsync(c)));
        return await c.PostAsync(url, new FormUrlEncodedContent(datos));
    }

    private static HttpClient Cliente(AppFactory app) =>
        app.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static async Task<string> PaginaAsync(HttpClient c, string url) => WebUtility.HtmlDecode(await c.GetStringAsync(url));

    private static async Task<AppConActualizacionesFactory> AbiertaAsync(string version = "1.0.0", string? release = "v2.0.0")
    {
        var app = new AppConActualizacionesFactory { Version = version };
        if (release is not null) app.Http.Responder = () => ManejadorFalso.Release(release);
        await app.InitializeAsync();
        await app.Services.GetRequiredService<ActualizacionesService>().ConsultarAsync();   // lo que hace Program al arrancar
        return app;
    }

    [Fact]
    public async Task ConUnaVersionNuevaTodasLasPantallasMuestranUnaFranjaDiscretaConSuEnlace()
    {
        using var app = await AbiertaAsync();
        using var c = Cliente(app);

        foreach (var url in new[] { "/", "/MisDatos", "/Estudiante/MateriasFaltantes" })
        {
            var html = await PaginaAsync(c, url);
            Assert.Contains("Hay una versión nueva", html);
            Assert.Contains("2.0.0", html);
            Assert.Contains("tienes la 1.0.0", html);
            Assert.Contains("href=\"https://github.com/dueno/repo/releases/tag/v2.0.0\"", html);
            Assert.Contains("rel=\"noopener noreferrer\"", html);
            Assert.Contains("Ignorar esta versión", html);
        }
    }

    [Fact]
    public async Task SiEstaAlDiaNoSeMuestraNada()
    {
        using var app = await AbiertaAsync(release: "v1.0.0");
        using var c = Cliente(app);

        Assert.DoesNotContain("Hay una versión nueva", await PaginaAsync(c, "/"));
    }

    [Fact]
    public async Task SinLaActivacionNoHayAvisoNiConsultaYMisDatosLoExplica()
    {
        using var app = new AppConDatosFactory();
        await app.InitializeAsync();
        using var c = Cliente(app);

        var html = await PaginaAsync(c, "/MisDatos");

        Assert.DoesNotContain("Hay una versión nueva", await PaginaAsync(c, "/"));
        Assert.Contains("Avisos de versiones nuevas", html);
        Assert.Contains("Este programa no consulta versiones nuevas", html);
        Assert.DoesNotContain("Apagar los avisos", html);
    }

    [Fact]
    public async Task IgnorarLaVersionQuitaLaFranjaYVuelveDondeEstabas()
    {
        using var app = await AbiertaAsync();
        using var c = Cliente(app);

        var r = await PostAsync(c, "/Actualizaciones/Ignorar", ("version", "2.0.0"), ("volverA", "/Estudiante/MateriasFaltantes"));

        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Equal("/Estudiante/MateriasFaltantes", r.Headers.Location!.OriginalString);
        Assert.DoesNotContain("Hay una versión nueva", await PaginaAsync(c, "/"));
    }

    [Theory]
    [InlineData("https://sitio-malicioso.example/")]
    [InlineData("//sitio-malicioso.example/")]
    [InlineData("javascript:alert(1)")]
    public async Task IgnorarNuncaRedirigeAUnaDireccionDeFuera(string volverA)
    {
        using var app = await AbiertaAsync();
        using var c = Cliente(app);

        var r = await PostAsync(c, "/Actualizaciones/Ignorar", ("version", "2.0.0"), ("volverA", volverA));

        Assert.Equal("/MisDatos", r.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task ApagarLosAvisosQuitaLaFranjaYMisDatosDiceQueNoSeConectaANada()
    {
        using var app = await AbiertaAsync();
        using var c = Cliente(app);
        Assert.Contains("Apagar los avisos", await PaginaAsync(c, "/MisDatos"));

        var r = await PostAsync(c, "/Actualizaciones/Preferencia", ("avisar", "false"));
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);

        var html = await PaginaAsync(c, "/MisDatos");
        Assert.DoesNotContain("Hay una versión nueva", html);
        Assert.Contains("ya no consulta si hay versiones nuevas", html);
        Assert.Contains("Los avisos están <strong>apagados</strong>", html);
        Assert.Contains("Encender los avisos", html);
        Assert.DoesNotContain("Buscar ahora", html);
        Assert.Equal(1, app.Http.Peticiones);
    }

    [Fact]
    public async Task AlEncenderLosAvisosDeNuevoSeConsultaUnaVezSinReiniciar()
    {
        using var app = await AbiertaAsync();
        using var c = Cliente(app);
        await PostAsync(c, "/Actualizaciones/Preferencia", ("avisar", "false"));

        await PostAsync(c, "/Actualizaciones/Preferencia", ("avisar", "true"));

        Assert.Contains("Hay una versión nueva", await PaginaAsync(c, "/"));
        Assert.Equal(2, app.Http.Peticiones);
    }

    [Fact]
    public async Task MisDatosExplicaQueSeEnviaYQueNo()
    {
        using var app = await AbiertaAsync();
        using var c = Cliente(app);

        var html = await PaginaAsync(c, "/MisDatos");

        Assert.Contains("dueno/repo", html);
        Assert.Contains("Versión que estás usando: <strong>1.0.0</strong>", html);
        Assert.Contains("GitHub ve tu dirección IP y el nombre y la versión del programa, nada más", html);
        Assert.Contains("Esta preferencia vale para todos los perfiles", html);
    }

    [Fact]
    public async Task BuscarAhoraDiceSiEstaAlDiaSiHayNuevaOSiFallo()
    {
        using var app = await AbiertaAsync(release: "v1.0.0");
        using var c = Cliente(app);

        await PostAsync(c, "/Actualizaciones/BuscarAhora");
        Assert.Contains("Estás al día", await PaginaAsync(c, "/MisDatos"));

        app.Http.Responder = () => ManejadorFalso.Release("v3.0.0");
        await PostAsync(c, "/Actualizaciones/BuscarAhora");
        Assert.Contains("Hay una versión nueva: 3.0.0", await PaginaAsync(c, "/MisDatos"));

        app.Http.Responder = () => throw new HttpRequestException("sin red");
        await PostAsync(c, "/Actualizaciones/BuscarAhora");
        var html = await PaginaAsync(c, "/MisDatos");
        Assert.Contains("No pude consultar GitHub", html);
        Assert.DoesNotContain("Hay una versión nueva", html);
    }

    [Fact]
    public async Task LasAccionesSinTokenAntifalsificacionSeRechazan()
    {
        using var app = await AbiertaAsync();
        using var c = Cliente(app);

        var r = await c.PostAsync("/Actualizaciones/Preferencia", new FormUrlEncodedContent(new Dictionary<string, string> { ["avisar"] = "false" }));

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("Hay una versión nueva", await PaginaAsync(c, "/"));
    }
}

public class ConfiguracionDeActualizacionesTests
{
    private static string Ruta(params string[] partes) => Path.Combine(new[] { PensumEjemplo.Raiz }.Concat(partes).ToArray());

    [Fact]
    public void SoloElProgramaPublicadoActivaLasConsultasYDesarrolloYPruebasNo()
    {
        Assert.Contains("\"Actualizaciones\"", File.ReadAllText(Ruta("HistorialAcademico.Web", "appsettings.Production.json")));
        Assert.Contains("\"Activas\": true", File.ReadAllText(Ruta("HistorialAcademico.Web", "appsettings.Production.json")));
        Assert.DoesNotContain("Actualizaciones", File.ReadAllText(Ruta("HistorialAcademico.Web", "appsettings.json")));
    }

    [Fact]
    public void ElFlujoDeReleaseLeEscribeAlProgramaDeQueRepositorioSalio()
    {
        Assert.Contains("-p:RepositorioGitHub=\"$GITHUB_REPOSITORY\"", File.ReadAllText(Ruta(".github", "workflows", "release.yml")));
        Assert.Contains("RepositorioGitHub", File.ReadAllText(Ruta("HistorialAcademico.Web", "HistorialAcademico.Web.csproj")));
    }

    [Fact]
    public void ElProgramaCompiladoDesdeElCodigoNoTraeRepositorio() => Assert.Null(ActualizacionesService.RepositorioIncorporado());

    [Fact]
    public void LaFranjaNoTieneColoresFijosSinoLosDelTema()
    {
        var vista = File.ReadAllText(Ruta("HistorialAcademico.Web", "Views", "Shared", "Components", "AvisoActualizacion", "Default.cshtml"));

        Assert.Contains("alert alert-secondary", vista);                   // clase con tema claro y oscuro ya auditado
        Assert.DoesNotMatch("#[0-9A-Fa-f]{3,8}\\b|rgb\\(", vista);
    }
}
