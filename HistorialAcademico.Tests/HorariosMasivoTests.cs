using System.Net;
using System.Text.RegularExpressions;
using HistorialAcademico.Banner;
using HistorialAcademico.Core.Horarios;
using HistorialAcademico.Web.Data;
using HistorialAcademico.Web.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HistorialAcademico.Tests;

[CollectionDefinition(Nombre)]
public class ColeccionConsultas
{
    /// <summary>Las pruebas que consultan a Banner (falso) por el servicio comparten un candado estático: se corren una a la vez.</summary>
    public const string Nombre = "consultas a Banner";
}

/// <summary>Utilidades para pruebas con tareas en segundo plano.</summary>
internal static class Espera
{
    public static async Task HastaAsync(Func<bool> condicion, int milisegundos = 8000)
    {
        var limite = DateTime.UtcNow.AddMilliseconds(milisegundos);
        while (!condicion())
        {
            if (DateTime.UtcNow > limite) throw new TimeoutException("La condición no se cumplió a tiempo.");
            await Task.Delay(20);
        }
    }
}

/// <summary>Consulta de varias materias con un Banner falso: guardado por materia, omitidas, errores, sesión y cancelación.</summary>
[Collection(ColeccionConsultas.Nombre)]
public class ConsultarVariasTests : IDisposable
{
    private const string P = "202710";
    private readonly BdPrueba _bd = new();
    private readonly BannerFalso _banner = new();
    private readonly HorariosService _servicio;
    private readonly List<ItemLote> _items = new();
    private readonly List<string> _fases = new();

    public ConsultarVariasTests()
    {
        _servicio = new HorariosService(_bd.Db, _banner);
        _banner.Secciones = (periodo, consultas) => Task.FromResult(Uno(periodo, consultas));
    }

    public void Dispose() => _bd.Dispose();

    /// <summary>Una sección por consulta (lo bastante para saber que se guardó).</summary>
    private static ResultadoBusqueda Uno(string periodo, IReadOnlyList<ConsultaBanner> consultas)
    {
        var secciones = consultas.Select((c, i) => new SeccionBanner
        {
            Periodo = periodo, Nrc = periodo + c.Codigo, Codigo = c.Codigo, Materia = c.Materia, Curso = c.Curso ?? "", Seccion = "1", Titulo = "PRUEBA",
            Creditos = 3, CupoMaximo = 30, Inscritos = 5, CuposDisponibles = 25, Abierta = true, Profesores = new() { "Profe Ejemplo" },
        }).ToList();
        return new ResultadoBusqueda { Total = secciones.Count, Secciones = secciones };
    }

    private static readonly (string, string)[] Dos = { ("ISO725", "Integración"), ("ISO800", "Calidad") };

    private Task<ResultadoLoteGuardado> Consultar(IReadOnlyList<(string, string)>? materias = null, bool omitir = false, bool login = false, CancellationToken ct = default) =>
        _servicio.ConsultarVariasAsync(materias ?? Dos, P, omitir, login, _fases.Add, _items.Add, ct);

    [Fact]
    public async Task ConsultaCadaMateriaConUnaSolaSesionYLasGuardaAlLlegar()
    {
        var r = await Consultar();

        Assert.True(r.Exito, r.Mensaje);
        Assert.Equal(new[] { "ISO725", "ISO800" }, _items.Select(i => i.Codigo));
        Assert.All(_items, i => Assert.Equal((ResultadoItem.ConSecciones, 1), (i.Resultado, i.Secciones)));
        Assert.Equal((P, 2), Assert.Single(_banner.Lotes));   // un solo lote = un solo navegador
        Assert.Equal(2, await _bd.Db.SeccionesOfertadas.CountAsync());
        Assert.Equal(2, await _bd.Db.ConsultasSecciones.CountAsync());
        Assert.Contains("Consultando Banner…", _fases);
    }

    [Fact]
    public async Task UnaMateriaSinSeccionesEsUnResultadoNormalYQuedaConsultada()
    {
        _banner.Secciones = (_, c) => Task.FromResult(c[0].Codigo == "ISO800" ? new ResultadoBusqueda() : Uno(P, c));

        var r = await Consultar();

        Assert.True(r.Exito);
        Assert.Equal(new[] { ResultadoItem.ConSecciones, ResultadoItem.SinSecciones }, _items.Select(i => i.Resultado));
        Assert.True((await _servicio.VerAsync("ISO800", P)).SinSecciones);
    }

    [Fact]
    public async Task LoQueNoTieneHorarioSeOmiteConSuMotivoYNoVaABanner()
    {
        var r = await Consultar(new[] { ("TFG", "Trabajo Final"), ("PAS261", "Pasantía"), ("ISO725", "Integración") });

        Assert.True(r.Exito);
        Assert.Equal(new[] { "TFG", "PAS261", "ISO725" }, _items.Select(i => i.Codigo));
        Assert.Equal(new[] { ResultadoItem.Omitida, ResultadoItem.Omitida, ResultadoItem.ConSecciones }, _items.Select(i => i.Resultado));
        Assert.All(_items.Take(2), i => Assert.False(string.IsNullOrWhiteSpace(i.Mensaje)));
        Assert.Equal(("202710", 1), Assert.Single(_banner.Lotes));   // solo ISO725 viajó a Banner
    }

    [Fact]
    public async Task OmitirLasConsultadasSaltaLasQueYaEstan()
    {
        await Consultar();
        _items.Clear();
        _banner.Lotes.Clear();

        var r = await Consultar(new[] { ("ISO725", "Integración"), ("ISO800", "Calidad"), ("ISO900", "Administración") }, omitir: true);

        Assert.True(r.Exito);
        Assert.Equal(new[] { ResultadoItem.Omitida, ResultadoItem.Omitida, ResultadoItem.ConSecciones }, _items.Select(i => i.Resultado));
        Assert.Equal(1, Assert.Single(_banner.Lotes).Materias);
    }

    [Fact]
    public async Task SiTodoEstabaConsultadoYOmitirNoLlamaABanner()
    {
        await Consultar();
        _banner.Lotes.Clear();

        var r = await Consultar(omitir: true);

        Assert.True(r.Exito);
        Assert.Contains("nada nuevo", r.Mensaje);
        Assert.Empty(_banner.Lotes);
    }

    [Fact]
    public async Task SinOmitirVuelveAConsultarYReemplazaSinDuplicar()
    {
        await Consultar();
        _banner.Lotes.Clear();

        await Consultar(omitir: false);

        Assert.Single(_banner.Lotes);
        Assert.Equal(2, await _bd.Db.SeccionesOfertadas.CountAsync());
        Assert.Equal(2, await _bd.Db.ConsultasSecciones.CountAsync());
    }

    [Fact]
    public async Task UnaMateriaQueFallaSeAnotaYSeSigueConLasDemas()
    {
        _banner.Secciones = (_, c) => c[0].Codigo == "ISO725" ? throw new BannerException("Banner respondió HTTP 500 al consultar ISO725.") : Task.FromResult(Uno(P, c));

        var r = await Consultar();

        Assert.True(r.Exito);
        Assert.Equal(new[] { ResultadoItem.Error, ResultadoItem.ConSecciones }, _items.Select(i => i.Resultado));
        Assert.Contains("HTTP 500", _items[0].Mensaje);
        Assert.False((await _servicio.VerAsync("ISO725", P)).YaConsultada);    // la que falló no queda como «consultada»
        Assert.True((await _servicio.VerAsync("ISO800", P)).YaConsultada);
    }

    [Fact]
    public async Task ConLaSesionCaducadaAMitadSinPermisoPideLoginYConservaLoYaGuardado()
    {
        _banner.Secciones = (_, c) => c[0].Codigo == "ISO800" ? throw new BannerSesionExpiradaException() : Task.FromResult(Uno(P, c));

        var r = await Consultar();

        Assert.False(r.Exito);
        Assert.True(r.RequiereLogin);
        Assert.Equal(0, _banner.Logins);
        Assert.Equal(new[] { "ISO725" }, _items.Select(i => i.Codigo));
        Assert.True((await _servicio.VerAsync("ISO725", P)).YaConsultada);   // lo hecho antes de caducar queda a salvo
    }

    [Fact]
    public async Task ConPermisoIniciaSesionUnaVezYSigueSoloConLasQueFaltan()
    {
        var caducada = true;
        _banner.Secciones = (_, c) =>
        {
            if (c[0].Codigo == "ISO800" && caducada) { caducada = false; throw new BannerSesionExpiradaException(); }
            return Task.FromResult(Uno(P, c));
        };

        var r = await Consultar(login: true);

        Assert.True(r.Exito, r.Mensaje);
        Assert.Equal(1, _banner.Logins);
        Assert.Equal(new[] { "ISO725", "ISO800" }, _items.Select(i => i.Codigo));
        Assert.Equal(new[] { 2, 1 }, _banner.Lotes.Select(l => l.Materias));                 // el reintento solo lleva la que faltaba
        Assert.Equal(new[] { "ISO725", "ISO800", "ISO800" }, _banner.ConsultasSecciones.Select(c => c.Consultas));
        Assert.Contains(_fases, f => f.Contains("inicies sesión"));
    }

    [Fact]
    public async Task UnaSesionQueSigueCaducadaTrasElLoginNoEntraEnCiclo()
    {
        _banner.Secciones = (_, _) => throw new BannerSesionExpiradaException();

        var r = await Consultar(login: true);

        Assert.False(r.Exito);
        Assert.True(r.RequiereLogin);
        Assert.Equal(1, _banner.Logins);
        Assert.Equal(2, _banner.Lotes.Count);   // el intento y un solo reintento
    }

    [Fact]
    public async Task SiElLoginFallaLoDiceYNoReintenta()
    {
        _banner.Secciones = (_, _) => throw new BannerSesionExpiradaException();
        _banner.Login = () => throw new BannerException("Se agotó el tiempo esperando el inicio de sesión en Banner.");

        var r = await Consultar(login: true);

        Assert.False(r.Exito);
        Assert.Contains("No se pudo iniciar sesión en Banner: Se agotó el tiempo", r.Mensaje);
        Assert.Single(_banner.Lotes);
    }

    [Fact]
    public async Task CancelarDetieneElLoteYConservaLoYaConsultado()
    {
        using var cancelacion = new CancellationTokenSource();
        _banner.Secciones = (_, c) =>
        {
            var r = Uno(P, c);
            if (c[0].Codigo == "ISO725") cancelacion.Cancel();   // se cancela mientras se consulta la primera
            return Task.FromResult(r);
        };

        var r = await Consultar(ct: cancelacion.Token);

        Assert.False(r.Exito);
        Assert.True(r.Cancelada);
        Assert.Equal(new[] { "ISO725" }, _items.Select(i => i.Codigo));
    }

    [Fact]
    public async Task UnaExcepcionInesperadaSeConvierteEnMensaje()
    {
        _banner.Secciones = (_, _) => throw new IOException("disco lleno");

        var r = await Consultar();

        Assert.False(r.Exito);
        Assert.Contains("IOException", r.Mensaje);
    }

    [Fact]
    public async Task UnPeriodoQueNoEsDeGradoSeRechazaSinLlamarABanner()
    {
        var r = await _servicio.ConsultarVariasAsync(Dos, "202735", false, false, _ => { }, _ => { });

        Assert.False(r.Exito);
        Assert.Empty(_banner.Lotes);
    }

    [Fact]
    public async Task NoSePuedenSuperponerDosConsultasAlaVez()
    {
        var entro = new TaskCompletionSource();
        var seguir = new TaskCompletionSource();
        _banner.Secciones = async (_, c) => { entro.TrySetResult(); await seguir.Task; return Uno(P, c); };

        var primera = Consultar();
        await entro.Task;
        var segunda = await Consultar();                                     // mientras la primera sigue en Banner
        var unica = await _servicio.ConsultarAsync("ISO900", P, false);     // ni una sola materia suelta
        seguir.SetResult();
        await primera;

        Assert.False(segunda.Exito);
        Assert.Contains("Ya hay una consulta a Banner en curso", segunda.Mensaje);
        Assert.False(unica.Exito);
        Assert.Contains("Ya hay una consulta a Banner en curso", unica.Mensaje);
    }

    // ── Panorama ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ElPanoramaDistingueSinConsultarSinSeccionesYConSecciones()
    {
        _banner.Secciones = (_, c) => Task.FromResult(c[0].Codigo == "ISO800" ? new ResultadoBusqueda() : Uno(P, c));
        await Consultar();

        var filas = await _servicio.PanoramaAsync(new[] { ("ISO725", "A"), ("ISO800", "B"), ("ISO900", "C"), ("TFG", "D") }, P);

        Assert.Equal(new[] { "ISO725", "ISO800", "ISO900" }, filas.Select(f => f.Codigo));   // el TFG no se consulta: no aparece
        Assert.Equal(new[] { EstadoPanorama.ConSecciones, EstadoPanorama.SinSecciones, EstadoPanorama.SinConsultar }, filas.Select(f => f.Estado));
        Assert.Equal(1, filas[0].Secciones);
        Assert.NotNull(filas[0].Consultada);
        Assert.Null(filas[2].Consultada);
    }

    [Fact]
    public async Task UnaElectivaSoloEstaConsultadaCuandoSeConsultaronTodasSusOpciones()
    {
        await _servicio.ConsultarAsync("E077", P, false);
        var todas = await _servicio.PanoramaAsync(new[] { ("E077", "Electiva") }, P);
        Assert.Equal(EstadoPanorama.ConSecciones, Assert.Single(todas).Estado);

        _bd.Db.ConsultasSecciones.Remove(_bd.Db.ConsultasSecciones.First(c => c.Codigo == "ADM536"));
        await _bd.Db.SaveChangesAsync();
        Assert.Equal(EstadoPanorama.SinConsultar, Assert.Single(await _servicio.PanoramaAsync(new[] { ("E077", "Electiva") }, P)).Estado);
    }

    [Fact]
    public async Task ElPanoramaDeUnPeriodoNoMezclaLosOtros()
    {
        await Consultar();

        var otro = await _servicio.PanoramaAsync(Dos, "202720");

        Assert.All(otro, f => Assert.Equal(EstadoPanorama.SinConsultar, f.Estado));
    }
}

/// <summary>El trabajo que consulta todas las materias en segundo plano (a petición): estado, avance, resumen y cancelación.</summary>
[Collection(ColeccionConsultas.Nombre)]
public class ConsultaMasivaServiceTests : IDisposable
{
    private const string P = "202710";
    private readonly BdPrueba _bd = new();
    private readonly BannerFalso _banner = new();
    private readonly ServiceProvider _proveedor;
    private readonly ConsultaMasivaService _masiva;

    public ConsultaMasivaServiceTests()
    {
        _banner.Secciones = (periodo, c) => Task.FromResult(Una(periodo, c));
        _proveedor = new ServiceCollection().AddSingleton(_bd.Db).AddSingleton<BannerClient>(_banner).AddScoped<HorariosService>().BuildServiceProvider();
        _masiva = new ConsultaMasivaService(_proveedor.GetRequiredService<IServiceScopeFactory>());
    }

    public void Dispose() { _proveedor.Dispose(); _bd.Dispose(); }

    private static ResultadoBusqueda Una(string periodo, IReadOnlyList<ConsultaBanner> consultas)
    {
        var s = consultas.Select(c => new SeccionBanner { Periodo = periodo, Nrc = periodo + c.Codigo, Codigo = c.Codigo, Materia = c.Materia, Curso = c.Curso ?? "", Seccion = "1", CupoMaximo = 30, CuposDisponibles = 10, Abierta = true }).ToList();
        return new ResultadoBusqueda { Total = s.Count, Secciones = s };
    }

    private static readonly (string, string)[] Tres = { ("ISO725", "Integración"), ("ISO800", "Calidad"), ("ISO900", "Administración") };

    [Fact]
    public async Task ConsultaTodasYDejaUnResumenEnLenguajeLlano()
    {
        _banner.Secciones = (p, c) => Task.FromResult(c[0].Codigo == "ISO800" ? new ResultadoBusqueda() : Una(p, c));

        var (iniciada, mensaje) = _masiva.Iniciar(Tres, P, omitirConsultadas: false);
        await _masiva.Tarea;

        Assert.True(iniciada, mensaje);
        var e = _masiva.Estado!;
        Assert.False(e.Activa);
        Assert.True(e.Terminada && e.Exito && !e.Cancelada);
        Assert.Equal((3, 100), (e.Hechas, e.Porcentaje));
        Assert.Equal((2, 1, 0, 0, 2), (e.ConSecciones, e.SinSecciones, e.ConError, e.Omitidas, e.TotalSecciones));
        Assert.Equal("Consultadas 3 de 3 materias en ENE-ABR 2027: 2 con secciones (2 en total), 1 sin secciones publicadas por ahora.", e.Mensaje);
        Assert.Equal("Terminada", e.Fase);
        Assert.NotNull(e.Fin);
    }

    [Fact]
    public async Task ElResumenCuentaLosErroresYLasOmitidas()
    {
        _banner.Secciones = (p, c) => c[0].Codigo == "ISO800" ? throw new BannerException("Banner respondió HTTP 500.") : Task.FromResult(Una(p, c));

        _masiva.Iniciar(new[] { ("ISO725", "A"), ("ISO800", "B"), ("TFG", "C") }, P, false);
        await _masiva.Tarea;

        var e = _masiva.Estado!;
        Assert.Equal("Consultadas 2 de 3 materias en ENE-ABR 2027: 1 con secciones (1 en total), 1 con error. 1 omitidas.", e.Mensaje);
        Assert.Equal(1, e.ConError);
    }

    [Fact]
    public async Task MientrasCorreMuestraElAvanceYLaMateriaQueSigue()
    {
        var segundaEntro = new TaskCompletionSource();
        var seguir = new TaskCompletionSource();
        _banner.Secciones = async (p, c) =>
        {
            if (c[0].Codigo == "ISO800") { segundaEntro.TrySetResult(); await seguir.Task; }
            return Una(p, c);
        };

        _masiva.Iniciar(Tres, P, false);
        await segundaEntro.Task;
        await Espera.HastaAsync(() => _masiva.Estado!.Hechas == 1);

        var e = _masiva.Estado!;
        Assert.True(e.Activa);
        Assert.False(e.Terminada);
        Assert.Equal(("ISO800 · Calidad", 33), (e.Actual, e.Porcentaje));
        Assert.Equal("Consultando Banner…", e.Fase);
        Assert.Equal("ISO725", Assert.Single(e.Items).Codigo);

        seguir.SetResult();
        await _masiva.Tarea;
        Assert.Equal(3, _masiva.Estado!.Hechas);
    }

    [Fact]
    public async Task NoArrancaOtraConsultaMientrasHayUnaEnCurso()
    {
        var entro = new TaskCompletionSource();
        var seguir = new TaskCompletionSource();
        _banner.Secciones = async (p, c) => { entro.TrySetResult(); await seguir.Task; return Una(p, c); };

        Assert.True(_masiva.Iniciar(Tres, P, false).Iniciada);
        await entro.Task;
        var (segunda, mensaje) = _masiva.Iniciar(Tres, P, false);
        seguir.SetResult();
        await _masiva.Tarea;

        Assert.False(segunda);
        Assert.Contains("Ya hay una consulta", mensaje);
        Assert.True(_masiva.Iniciar(Tres, P, false).Iniciada);   // ya terminada: se puede volver a pedir
        await _masiva.Tarea;
    }

    [Fact]
    public async Task CancelarDetieneLaConsultaYSeLoDice()
    {
        var primeraEntro = new TaskCompletionSource();
        var seguir = new TaskCompletionSource();
        _banner.Secciones = async (p, c) =>
        {
            if (c[0].Codigo == "ISO725") { primeraEntro.TrySetResult(); await seguir.Task; }
            return Una(p, c);
        };

        _masiva.Iniciar(Tres, P, false);
        await primeraEntro.Task;
        Assert.True(_masiva.Cancelar());
        Assert.Equal("Cancelando…", _masiva.Estado!.Fase);
        seguir.SetResult();
        await _masiva.Tarea;

        var e = _masiva.Estado!;
        Assert.True(e.Cancelada);
        Assert.False(e.Activa);
        Assert.Equal("Cancelada", e.Fase);
        Assert.StartsWith("Consulta cancelada.", e.Mensaje);
        Assert.Equal(1, e.Hechas);   // la que iba terminó y se guardó; las demás no se consultaron
        Assert.Equal(1, await _bd.Db.ConsultasSecciones.CountAsync());
        Assert.False(_masiva.Cancelar());   // ya no hay nada que cancelar
    }

    [Fact]
    public async Task UnErrorInesperadoTerminaLaConsultaSinDejarlaColgada()
    {
        _banner.Secciones = (_, _) => throw new InvalidOperationException("algo raro");

        _masiva.Iniciar(Tres, P, false);
        await _masiva.Tarea;

        var e = _masiva.Estado!;
        Assert.False(e.Activa);
        Assert.True(e.Terminada);
        Assert.False(e.Exito);
        Assert.Contains("InvalidOperationException", e.Mensaje);
        Assert.True(_masiva.Iniciar(Tres, P, false).Iniciada);   // no queda bloqueada
        await _masiva.Tarea;
    }

    [Fact]
    public async Task SinSesionYSinLoginElResumenExplicaQueHayQueIniciarSesion()
    {
        _banner.Secciones = (_, _) => throw new BannerSesionExpiradaException();
        _banner.Login = () => throw new BannerException("Se cerró la ventana de Banner antes de iniciar sesión.");

        _masiva.Iniciar(Tres, P, false);
        await _masiva.Tarea;

        var e = _masiva.Estado!;
        Assert.False(e.Exito);
        Assert.Contains("No se pudo iniciar sesión en Banner", e.Mensaje);
        Assert.Contains("No se consultó ninguna materia.", e.Mensaje);
    }

    [Theory]
    [InlineData("202735")]
    [InlineData("")]
    public void RechazaUnPeriodoQueNoEsDeGrado(string periodo)
    {
        var (iniciada, mensaje) = _masiva.Iniciar(Tres, periodo, false);

        Assert.False(iniciada);
        Assert.Contains("no es de Grado", mensaje);
        Assert.Null(_masiva.Estado);
    }

    [Fact]
    public void RechazaUnaListaVacia()
    {
        Assert.False(_masiva.Iniciar(Array.Empty<(string, string)>(), P, false).Iniciada);
        Assert.Null(_masiva.Estado);
    }

    [Fact]
    public void SinNingunaConsultaNoHayEstado() => Assert.Null(_masiva.Estado);
}

/// <summary>La aplicación completa con un Banner falso: nada de esta clase puede abrir un navegador ni tocar Banner de verdad.</summary>
public class AppConBannerFalsoFactory : AppFactory
{
    public BannerFalso Banner { get; } = new();

    protected override bool ConDatos => true;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(s =>
        {
            s.RemoveAll<BannerClient>();
            s.AddScoped<BannerClient>(_ => Banner);
        });
    }
}

/// <summary>La pantalla de horarios con el panorama y el botón «Consultar todas las disponibles», de punta a punta.</summary>
[Collection(ColeccionConsultas.Nombre)]
public class HorariosMasivoPantallaTests : IClassFixture<AppConBannerFalsoFactory>
{
    private readonly AppConBannerFalsoFactory _app;

    public HorariosMasivoPantallaTests(AppConBannerFalsoFactory app)
    {
        _app = app;
        _app.Banner.Secciones = (periodo, consultas) => Task.FromResult(new ResultadoBusqueda
        {
            Total = consultas.Count,
            Secciones = consultas.Select(c => new SeccionBanner { Periodo = periodo, Nrc = periodo + c.Codigo, Codigo = c.Codigo, Materia = c.Materia, Curso = c.Curso ?? "", Seccion = "1", Titulo = "PRUEBA", Creditos = 3, CupoMaximo = 30, CuposDisponibles = 10, Abierta = true }).ToList(),
        });
    }

    private static async Task<string> Token(HttpClient c) =>
        Regex.Match(await c.GetStringAsync("/Horarios"), "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;

    private async Task<(HttpResponseMessage, string)> Post(string url, params (string, string)[] campos)
    {
        using var c = _app.CreateClient();
        var datos = campos.Select(x => new KeyValuePair<string, string>(x.Item1, x.Item2)).Append(new("__RequestVerificationToken", await Token(c)));
        var r = await c.PostAsync(url, new FormUrlEncodedContent(datos));
        return (r, WebUtility.HtmlDecode(await r.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task ElPanoramaListaLasDisponiblesYOfreceConsultarTodas()
    {
        var (estado, html) = await _app.GetAsync("/Horarios?periodo=202610");   // ninguna otra prueba de la clase consulta este período

        Assert.Equal(HttpStatusCode.OK, estado);
        Assert.Contains("Materias disponibles en ENE-ABR 2026", html);
        Assert.Contains("id=\"tabla-panorama\"", html);
        Assert.Contains("Sin consultar", html);
        Assert.Contains("Consultar todas las disponibles", html);
        Assert.Contains("action=\"/Horarios/ConsultarTodas\"", html);
        Assert.Contains("Omitir las que ya consulté en este período", html);
        Assert.DoesNotContain("id=\"panel-progreso\"", html);
        Assert.DoesNotContain("<option value=\"TFG\"", html);
    }

    [Fact]
    public async Task ElAvanceSinNingunaConsultaResponde_SinDatos()
    {
        using var nueva = new AppFactory();   // aplicación recién arrancada: esta clase comparte la suya y otras pruebas ya consultaron
        var (estado, texto) = await nueva.GetAsync("/Horarios/Progreso");

        Assert.Equal(HttpStatusCode.OK, estado);
        Assert.Contains("\"hay\":false", texto);
    }

    [Fact]
    public async Task ConsultarTodasSinTokenSeRechaza()
    {
        using var c = _app.CreateClient();
        var lotesAntes = _app.Banner.Lotes.Count;

        var r = await c.PostAsync("/Horarios/ConsultarTodas", new FormUrlEncodedContent(new Dictionary<string, string> { ["periodo"] = "202610" }));
        await _app.Services.GetRequiredService<ConsultaMasivaService>().Tarea;

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal(lotesAntes, _app.Banner.Lotes.Count);   // no se consultó nada
    }

    [Fact]
    public async Task ConsultarTodasCorreElLoteYLaPantallaMuestraElResumenYElPanoramaActualizado()
    {
        var (respuesta, _) = await Post("/Horarios/ConsultarTodas", ("periodo", "202630"));
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);   // tras la redirección

        await _app.Services.GetRequiredService<ConsultaMasivaService>().Tarea;
        var (_, html) = await _app.GetAsync("/Horarios?periodo=202630");

        var lote = _app.Banner.Lotes.Last();
        Assert.Equal(("202630", true), (lote.Periodo, lote.Materias > 0));
        Assert.Contains("id=\"resumen-consulta\"", html);
        Assert.Contains("Consulta terminada", html);
        Assert.Matches("Consultadas \\d+ de \\d+ materias en SEP-DIC 2026", html);
        Assert.Contains("con secciones", html);
        Assert.DoesNotContain("id=\"panel-progreso\"", html);              // ya no corre
        Assert.Contains("1 sección", html);                                // el panorama se actualizó con lo guardado
        Assert.DoesNotContain("alert-danger", html);
    }

    [Fact]
    public async Task CancelarSinNingunaConsultaEnCursoAvisaSinRomperNada()
    {
        var (r, html) = await Post("/Horarios/Cancelar", ("periodo", "202630"));

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Contains("No hay ninguna consulta en curso", html);
    }

    [Fact]
    public async Task MientrasCorreLaPantallaMuestraLaBarraDeAvanceYElBotonDeCancelar()
    {
        var entro = new TaskCompletionSource();
        var seguir = new TaskCompletionSource();
        _app.Banner.Secciones = async (p, c) => { entro.TrySetResult(); await seguir.Task; return new ResultadoBusqueda(); };

        await Post("/Horarios/ConsultarTodas", ("periodo", "202620"));
        await entro.Task;
        var (_, html) = await _app.GetAsync("/Horarios?periodo=202620");
        var (_, json) = await _app.GetAsync("/Horarios/Progreso");
        seguir.SetResult();
        await _app.Services.GetRequiredService<ConsultaMasivaService>().Tarea;

        Assert.Contains("id=\"panel-progreso\"", html);
        Assert.Contains("data-activa=\"true\"", html);
        Assert.Contains("role=\"progressbar\"", html);
        Assert.Contains("Cancelar la consulta", html);
        Assert.Contains("horarios.js", html);
        Assert.Contains("disabled", Regex.Match(html, "<button[^>]*>Consultar todas las disponibles</button>").Value);   // no se puede lanzar otra
        Assert.Contains("\"activa\":true", json);
        Assert.Contains("\"periodo\":\"202620\"", json);
    }
}
