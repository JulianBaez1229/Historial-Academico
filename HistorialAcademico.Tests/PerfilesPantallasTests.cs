using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using HistorialAcademico.Banner;
using HistorialAcademico.Core.Horarios;
using HistorialAcademico.Core.Perfiles;
using HistorialAcademico.Core.Pensum;
using HistorialAcademico.Web.Controllers;
using HistorialAcademico.Web.Data;
using HistorialAcademico.Web.Perfiles;
using HistorialAcademico.Web.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace HistorialAcademico.Tests;

/// <summary>La aplicación completa CON perfiles (sin la base fija de las demás pruebas), todo en carpetas temporales.</summary>
public class AppConPerfilesFactory : WebApplicationFactory<Program>
{
    public string Raiz { get; } = Path.Combine(Path.GetTempPath(), "perfiles-app-" + Guid.NewGuid().ToString("N"));
    public string CarpetaUsuario => Path.Combine(Raiz, "usuario");
    public string CarpetaAnterior => Path.Combine(Raiz, "anterior");
    public string BaseAnterior => Path.Combine(CarpetaAnterior, "historial.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(Raiz);
        builder.UseSetting("Perfiles:Carpeta", CarpetaUsuario);
        builder.UseSetting("Perfiles:BaseAnterior", BaseAnterior);
        builder.UseSetting("Perfiles:CarpetaAnterior", CarpetaAnterior);
        builder.UseSetting("Perfiles:IteracionesPin", "1000");
        builder.UseSetting("Banner:BaseUrl", BaseUrl);
        if (Banner is not null)
            builder.ConfigureServices(s =>
            {
                s.RemoveAll<BannerClient>();
                s.AddScoped<BannerClient>(_ => Banner);
            });
    }

    /// <summary>Lo que hay en «Banner:BaseUrl»; vacío para probar que la dirección sale de la universidad elegida.</summary>
    protected virtual string BaseUrl => "https://alumnos.invalid/";

    /// <summary>Un Banner falso (null = el cliente real, que nunca se usa en estas pruebas).</summary>
    public virtual BannerFalso? Banner => null;

    public GestorPerfiles Gestor => Services.GetRequiredService<GestorPerfiles>();

    public HttpClient Cliente(bool cookies = true) =>
        CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = cookies });

    /// <summary>Abre un ámbito ya dentro del perfil (con su base migrada), como lo haría una petición.</summary>
    public IServiceScope Abrir(string id)
    {
        var scope = Services.CreateScope();
        var actual = scope.ServiceProvider.GetRequiredService<PerfilActual>();
        actual.Establecer(Gestor.Almacen.Obtener(id)!);
        Gestor.AsegurarBase(actual, scope.ServiceProvider.GetRequiredService<HistorialContext>());
        return scope;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(Raiz, recursive: true); } catch (IOException) { /* Windows aún puede tener un archivo abierto */ }
    }
}

/// <summary>Elegir, crear y cerrar perfiles como lo haría el navegador; y que los datos de cada perfil no se mezclen.</summary>
public class PerfilesPantallasTests
{
    private static async Task<string> TokenAsync(HttpClient c, string url) =>
        Regex.Match(await c.GetStringAsync(url), "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;

    internal static async Task<(HttpResponseMessage Respuesta, string Html)> PostAsync(HttpClient c, string paginaDelToken, string url, params (string, string)[] campos)
    {
        var datos = campos.Select(x => new KeyValuePair<string, string>(x.Item1, x.Item2)).Append(new("__RequestVerificationToken", await TokenAsync(c, paginaDelToken)));
        var r = await c.PostAsync(url, new FormUrlEncodedContent(datos));
        return (r, WebUtility.HtmlDecode(await r.Content.ReadAsStringAsync()));
    }

    /// <summary>Crea el perfil (paso 2 del asistente) y, salvo que se pida lo contrario, termina el asistente para poder usar la aplicación.</summary>
    internal static async Task<(HttpResponseMessage, string)> CrearAsync(HttpClient c, string nombre, string? pin = null, bool traer = false, bool terminar = true)
    {
        var (r, html) = await PostAsync(c, "/Perfiles/Crear", "/Perfiles/Crear",
            ("Nombre", nombre), ("SinPin", pin is null ? "true" : "false"), ("Pin", pin ?? ""), ("ConfirmarPin", pin ?? ""), ("TraerAnteriores", traer ? "true" : "false"));
        if (terminar && r.StatusCode == HttpStatusCode.Redirect) await PostAsync(c, "/Asistente/Banner", "/Asistente/Terminar");
        return (r, html);
    }

    private static async Task<string> PaginaAsync(HttpClient c, string url) => WebUtility.HtmlDecode(await c.GetStringAsync(url));

    [Fact]
    public async Task SinPerfilesLaPortadaLlevaAcrearElPrimeroYNoSeCreaNingunaBase()
    {
        using var app = new AppConPerfilesFactory();
        using var c = app.Cliente();

        var r = await c.GetAsync("/");
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Equal("/Asistente/Bienvenida", r.Headers.Location!.OriginalString);

        var crear = await PaginaAsync(c, "/Perfiles/Crear");
        Assert.Contains("Bienvenido", crear);
        Assert.Contains("Tu nombre", crear);
        Assert.DoesNotContain("Traer los datos", crear);   // no hay datos anteriores que ofrecer

        Assert.Equal(HttpStatusCode.Redirect, (await c.GetAsync("/Estudiante/MateriasTomadas")).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await c.GetAsync("/Horarios")).StatusCode);
        Assert.False(Directory.Exists(Path.Combine(app.CarpetaUsuario, "perfiles")), "Sin perfil no se abre ni se crea ninguna base.");
    }

    [Fact]
    public async Task ElIndiceSinPerfilesTambienLlevaACrear()
    {
        using var app = new AppConPerfilesFactory();
        using var c = app.Cliente();

        var r = await c.GetAsync("/Perfiles");

        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Equal("/Asistente/Bienvenida", r.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task CrearUnPerfilSinPinEntraDirectoYCreaSuBaseEnLaCarpetaDelUsuario()
    {
        using var app = new AppConPerfilesFactory();
        using var c = app.Cliente();

        var (r, _) = await CrearAsync(c, "Ana");

        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        var perfil = Assert.Single(app.Gestor.Almacen.Listar());
        var portada = await PaginaAsync(c, "/");
        Assert.Contains("Ana", portada);                       // el indicador de perfil de la barra
        Assert.Contains("Actualizar desde Banner", portada);   // la aplicación normal
        Assert.True(File.Exists(Path.Combine(app.CarpetaUsuario, "perfiles", perfil.Id, "historial.db")));
        Assert.DoesNotContain("Bloquear", portada);            // sin PIN el botón es «Cambiar»
        Assert.Contains("Cambiar", portada);
    }

    [Fact]
    public async Task ConUnSoloPerfilSinPinUnNavegadorNuevoEntraSinPreguntar()
    {
        using var app = new AppConPerfilesFactory();
        using (var primero = app.Cliente()) await CrearAsync(primero, "Ana");

        using var otro = app.Cliente();   // sin cookie
        var r = await otro.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Contains("Ana", await r.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ConPinSeExigeElegirYPonerElPinYElPinNoSeDevuelveALaPagina()
    {
        using var app = new AppConPerfilesFactory();
        using (var c0 = app.Cliente()) await CrearAsync(c0, "Ana", "clave-1234");

        using var c = app.Cliente();
        var r = await c.GetAsync("/");
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Equal("/Perfiles", r.Headers.Location!.OriginalString);

        var lista = await PaginaAsync(c, "/Perfiles");
        Assert.Contains("Ana", lista);
        Assert.Contains("Con PIN", lista);

        var id = app.Gestor.Almacen.Listar().Single().Id;
        var (mal, htmlMal) = await PostAsync(c, "/Perfiles", "/Perfiles/Entrar", ("id", id), ("pin", "otra-clave"));
        Assert.Equal(HttpStatusCode.OK, mal.StatusCode);
        Assert.Contains("PIN incorrecto. Te quedan 3 intentos", htmlMal);
        Assert.DoesNotContain("otra-clave", htmlMal);
        Assert.Equal(HttpStatusCode.Redirect, (await c.GetAsync("/")).StatusCode);   // sigue sin entrar

        var (bien, _) = await PostAsync(c, "/Perfiles", "/Perfiles/Entrar", ("id", id), ("pin", "clave-1234"));
        Assert.Equal(HttpStatusCode.Redirect, bien.StatusCode);
        var portada = await PaginaAsync(c, "/");
        Assert.Contains("Actualizar desde Banner", portada);
        Assert.Contains("Bloquear", portada);
    }

    [Fact]
    public async Task VariosPinIncorrectosSeguidosHacenEsperarAunqueDespuesSePongaElCorrecto()
    {
        using var app = new AppConPerfilesFactory();
        using (var c0 = app.Cliente()) await CrearAsync(c0, "Ana", "clave-1234");
        var id = app.Gestor.Almacen.Listar().Single().Id;
        using var c = app.Cliente();

        string html = "";
        for (var i = 0; i < 5; i++) (_, html) = await PostAsync(c, "/Perfiles", "/Perfiles/Entrar", ("id", id), ("pin", "mal"));
        Assert.Contains("PIN incorrecto. Espera 30 segundos", html);

        var (r, despues) = await PostAsync(c, "/Perfiles", "/Perfiles/Entrar", ("id", id), ("pin", "clave-1234"));

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);   // no entró
        Assert.Contains("Demasiados intentos con el PIN", despues);
        Assert.Equal(HttpStatusCode.Redirect, (await c.GetAsync("/")).StatusCode);
        Assert.Contains("Bloqueado por varios PIN incorrectos", await PaginaAsync(c, "/Perfiles"));
    }

    [Fact]
    public async Task UnPerfilQueYaNoExisteSeAvisa()
    {
        using var app = new AppConPerfilesFactory();
        using var c = app.Cliente();
        await CrearAsync(c, "Ana", "clave-1234");

        var (r, html) = await PostAsync(c, "/Perfiles", "/Perfiles/Entrar", ("id", "ffffffff"), ("pin", "x"));

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Contains("Ese perfil ya no existe", html);
    }

    [Fact]
    public async Task SalirCierraElPerfilYHaceFaltaElPinOtraVez()
    {
        using var app = new AppConPerfilesFactory();
        using var c = app.Cliente();
        await CrearAsync(c, "Ana", "clave-1234");
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/")).StatusCode);

        var (r, _) = await PostAsync(c, "/", "/Perfiles/Salir");

        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        var despues = await c.GetAsync("/");
        Assert.Equal(HttpStatusCode.Redirect, despues.StatusCode);
        Assert.Equal("/Perfiles", despues.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task UnaCookieInventadaOAlteradaNoDaAcceso()
    {
        using var app = new AppConPerfilesFactory();
        using (var c0 = app.Cliente()) await CrearAsync(c0, "Ana", "clave-1234");
        var id = app.Gestor.Almacen.Listar().Single().Id;
        using var c = app.Cliente(cookies: false);

        foreach (var valor in new[] { "basura", id, "CfDJ8AAAAAAAAAAAAAAAAAAAAAA" })
        {
            var peticion = new HttpRequestMessage(HttpMethod.Get, "/Estudiante/MateriasTomadas");
            peticion.Headers.Add("Cookie", $"{GestorPerfiles.NombreCookie}={valor}");
            var r = await c.SendAsync(peticion);
            Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
            Assert.Equal("/Perfiles", r.Headers.Location!.OriginalString);
        }
    }

    [Fact]
    public async Task ElFormularioValidaNombrePinYConfirmacionSinCrearNada()
    {
        using var app = new AppConPerfilesFactory();
        using var c = app.Cliente();

        var (vacio, htmlVacio) = await CrearAsync(c, "  ");
        Assert.Equal(HttpStatusCode.OK, vacio.StatusCode);
        Assert.Contains("Escribe tu nombre.", htmlVacio);

        var (corto, htmlCorto) = await CrearAsync(c, "Ana", "12");
        Assert.Contains("al menos 4 caracteres", htmlCorto);

        var (distinto, htmlDistinto) = await PostAsync(c, "/Perfiles/Crear", "/Perfiles/Crear",
            ("Nombre", "Ana"), ("SinPin", "false"), ("Pin", "1234"), ("ConfirmarPin", "1235"), ("TraerAnteriores", "false"));
        Assert.Contains("Los dos PIN no son iguales", htmlDistinto);
        Assert.DoesNotContain("value=\"1234\"", htmlDistinto);   // el PIN nunca vuelve a la página

        Assert.Empty(app.Gestor.Almacen.Listar());
    }

    [Fact]
    public async Task NoSePuedeCrearUnSegundoPerfilConElMismoNombre()
    {
        using var app = new AppConPerfilesFactory();
        using var c = app.Cliente();
        await CrearAsync(c, "Ana");

        var (r, html) = await CrearAsync(c, "ana");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Contains("Ya hay un perfil llamado", html);
        Assert.Single(app.Gestor.Almacen.Listar());
    }

    [Fact]
    public async Task ConDosPerfilesSeElige_YCadaUnoVeSoloSusDatos()
    {
        using var app = new AppConPerfilesFactory();
        using var ana = app.Cliente();
        using var beto = app.Cliente();
        await CrearAsync(ana, "Ana");
        await CrearAsync(beto, "Beto");
        var idAna = app.Gestor.Almacen.Listar().Single(p => p.Nombre == "Ana").Id;
        var idBeto = app.Gestor.Almacen.Listar().Single(p => p.Nombre == "Beto").Id;

        // El histórico se sincroniza solo en el perfil de Ana.
        using (var scope = app.Abrir(idAna))
        {
            var r = await scope.ServiceProvider.GetRequiredService<SincronizacionService>().AplicarHtmlAsync(Muestras.LeerSintetico());
            Assert.True(r.Exito, r.Mensaje);
        }

        Assert.Contains("ISO200", await PaginaAsync(ana, "/Estudiante/MateriasTomadas"));
        var deBeto = await PaginaAsync(beto, "/Estudiante/MateriasTomadas");
        Assert.DoesNotContain("ISO200", deBeto);
        Assert.Contains("Beto", deBeto);

        // Dos bases distintas, cada una en la carpeta de su perfil.
        var bases = new[] { idAna, idBeto }.Select(id => app.Gestor.Almacen.RutaBase(id)).ToList();
        Assert.All(bases, b => Assert.True(File.Exists(b)));
        Assert.NotEqual(bases[0], bases[1]);
        Assert.True(new FileInfo(bases[0]).Length > 0);
    }

    [Fact]
    public async Task CadaPerfilTieneSuPropiaCarpetaDeSesionDeBanner()
    {
        using var app = new AppConPerfilesFactory();
        using var c = app.Cliente();
        await CrearAsync(c, "Ana");
        await CrearAsync(c, "Beto");
        var ids = app.Gestor.Almacen.Listar().Select(p => p.Id).ToList();

        var sesiones = new List<string>();
        foreach (var id in ids)
        {
            using var scope = app.Abrir(id);
            var opciones = scope.ServiceProvider.GetRequiredService<BannerOptions>();
            Assert.Equal(app.Gestor.Almacen.CarpetaDe(id), opciones.RaizDatos);
            Assert.Equal("https://alumnos.invalid/", opciones.BaseUrl);   // lo demás sigue siendo la configuración común
            sesiones.Add(opciones.RutaSesion);
        }

        Assert.Equal(2, sesiones.Distinct().Count());
        Assert.All(sesiones, s => Assert.StartsWith(app.CarpetaUsuario, s));
    }

    [Fact]
    public async Task LaUniversidadElegidaSeRecuerdaEnElPerfilYNoEnOtro()
    {
        using var app = new AppConPerfilesFactory();
        using var c = app.Cliente();
        await CrearAsync(c, "Ana");
        await CrearAsync(c, "Beto");
        var idAna = app.Gestor.Almacen.Listar().Single(p => p.Nombre == "Ana").Id;
        var idBeto = app.Gestor.Almacen.Listar().Single(p => p.Nombre == "Beto").Id;

        using (var scope = app.Abrir(idAna))
        {
            var reglas = scope.ServiceProvider.GetRequiredService<ReglasUniversidadService>();
            Assert.True(reglas.Establecer("unapec"));
        }

        Assert.Equal("unapec", app.Gestor.Almacen.Obtener(idAna)!.UniversidadId);
        Assert.Null(app.Gestor.Almacen.Obtener(idBeto)!.UniversidadId);
        using var otra = app.Abrir(idAna);
        Assert.Equal("unapec", otra.ServiceProvider.GetRequiredService<PerfilActual>().UniversidadId);
    }

    // ── Datos de la versión anterior ──────────────────────────────────────────────────────

    private static void CrearBaseAnterior(AppConPerfilesFactory app)
    {
        Directory.CreateDirectory(Path.Combine(app.CarpetaAnterior, ".auth"));
        var opciones = new DbContextOptionsBuilder<HistorialContext>().UseSqlite($"Data Source={app.BaseAnterior};Pooling=False").Options;
        using (var db = new HistorialContext(opciones))
        {
            db.Database.Migrate();
            db.MateriasPensum.AddRange(DatosLab.Pensum());
            db.SaveChanges();
        }
        File.WriteAllText(Path.Combine(app.CarpetaAnterior, ".auth", "banner.json"), "{\"cookies\":[],\"origins\":[]}");
    }

    private static string Huella(string ruta) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(ruta)));

    [Fact]
    public async Task LosDatosAnterioresSeOfrecenSeCopianYLosOriginalesNoSeTocan()
    {
        using var app = new AppConPerfilesFactory();
        CrearBaseAnterior(app);
        var huellaBase = Huella(app.BaseAnterior);
        var sesionAnterior = Path.Combine(app.CarpetaAnterior, ".auth", "banner.json");
        var huellaSesion = Huella(sesionAnterior);
        using var c = app.Cliente();

        var formulario = await PaginaAsync(c, "/Perfiles/Crear");
        Assert.Contains("Traer los datos que ya tenía en esta computadora", formulario);

        var (r, _) = await CrearAsync(c, "Ana", traer: true);
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        var id = app.Gestor.Almacen.Listar().Single().Id;

        using (var scope = app.Abrir(id))
        {
            var db = scope.ServiceProvider.GetRequiredService<HistorialContext>();
            Assert.Equal(DatosLab.Pensum().Count, await db.MateriasPensum.CountAsync());
        }
        Assert.True(File.Exists(Path.Combine(app.Gestor.Almacen.CarpetaDe(id), ".auth", "banner.json")));

        // Los originales siguen ahí, idénticos.
        Assert.Equal(huellaBase, Huella(app.BaseAnterior));
        Assert.Equal(huellaSesion, Huella(sesionAnterior));

        // Se ofrece una sola vez.
        Assert.True(app.Gestor.Almacen.DatosAnterioresAdoptados);
        Assert.DoesNotContain("Traer los datos", await PaginaAsync(c, "/Perfiles/Crear"));
    }

    [Fact]
    public async Task SiNoSeQuierenTraerNoSeCopiaNadaYSeSigueOfreciendo()
    {
        using var app = new AppConPerfilesFactory();
        CrearBaseAnterior(app);
        using var c = app.Cliente();

        await CrearAsync(c, "Ana", traer: false);
        var id = app.Gestor.Almacen.Listar().Single().Id;

        using (var scope = app.Abrir(id))
            Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<HistorialContext>().MateriasPensum.CountAsync());
        Assert.False(File.Exists(Path.Combine(app.Gestor.Almacen.CarpetaDe(id), ".auth", "banner.json")));
        Assert.False(app.Gestor.Almacen.DatosAnterioresAdoptados);
        Assert.Contains("Traer los datos", await PaginaAsync(c, "/Perfiles/Crear"));
    }

    [Fact]
    public async Task UnaBaseAnteriorQueNoSePuedeCopiarNoDejaUnPerfilAMedias()
    {
        using var app = new AppConPerfilesFactory();
        Directory.CreateDirectory(app.CarpetaAnterior);
        File.WriteAllText(app.BaseAnterior, "esto no es una base de datos SQLite");
        using var c = app.Cliente();

        var (r, html) = await CrearAsync(c, "Ana", traer: true);

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Contains("No pude traer los datos anteriores", html);
        Assert.Contains("el perfil no se creó", html);
        Assert.Empty(app.Gestor.Almacen.Listar());
        Assert.False(app.Gestor.Almacen.DatosAnterioresAdoptados);
        Assert.Equal("esto no es una base de datos SQLite", File.ReadAllText(app.BaseAnterior));
    }

    // ── Las pantallas de perfiles no dependen de un perfil ────────────────────────────────

    [Fact]
    public async Task LaPantallaDePerfilesUsaSuPropioDisenoSinMenuNiBarraDeSincronizacion()
    {
        using var app = new AppConPerfilesFactory();
        using var c = app.Cliente();
        await CrearAsync(c, "Ana", "clave-1234");
        using var otro = app.Cliente();

        var html = await PaginaAsync(otro, "/Perfiles");

        Assert.Contains("¿Quién eres?", html);
        Assert.DoesNotContain("Actualizar desde Banner", html);
        Assert.DoesNotContain("Datos Personales", html);
        Assert.Contains("no cifra los archivos", html);   // se dice con claridad qué protege el PIN y qué no
    }

    [Theory]
    [InlineData(0, "1 segundo")]
    [InlineData(1, "1 segundo")]
    [InlineData(29.2, "30 segundos")]
    [InlineData(30, "30 segundos")]
    [InlineData(61, "2 minutos")]
    [InlineData(60, "1 minuto")]
    [InlineData(900, "15 minutos")]
    public void LaEsperaSeDiceEnLenguajeLlano(double segundos, string esperado)
        => Assert.Equal(esperado, PerfilesController.TextoEspera(TimeSpan.FromSeconds(segundos)));
}

/// <summary>La consulta masiva a Banner es de cada perfil: no se mezclan ni se bloquean entre sí.</summary>
[Collection(ColeccionConsultas.Nombre)]
public class ConsultaMasivaPorPerfilTests : IDisposable
{
    private const string PeriodoAna = "202710", PeriodoBeto = "202720";
    private static readonly (string, string)[] Materias = { ("ISO725", "Integración"), ("ISO800", "Calidad") };

    private readonly BdPrueba _bd = new();
    private readonly BannerFalso _banner = new();
    private readonly ServiceProvider _proveedor;
    private readonly ConsultaMasivaService _masiva;
    private readonly GestorPerfiles _gestor;
    private readonly TaskCompletionSource _puertaAna = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _entroAna = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ConsultaMasivaPorPerfilTests()
    {
        _gestor = new GestorPerfiles(
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Perfiles:Carpeta"] = Path.Combine(Path.GetTempPath(), "perfil-masiva-" + Guid.NewGuid().ToString("N")) }).Build(),
            new EntornoFalso(Path.GetTempPath()), DataProtectionProvider.Create("prueba"));
        _banner.Secciones = async (periodo, consultas) =>
        {
            _entroAna.TrySetResult();
            await _puertaAna.Task;   // «en curso» hasta que la prueba suelte la puerta
            return new ResultadoBusqueda { Total = 0, Secciones = new() };
        };
        _proveedor = new ServiceCollection().AddSingleton(_bd.Db).AddSingleton<BannerClient>(_banner).AddSingleton(_gestor)
            .AddScoped<PerfilActual>().AddScoped<HorariosService>().BuildServiceProvider();
        _masiva = new ConsultaMasivaService(_proveedor.GetRequiredService<IServiceScopeFactory>());
    }

    public void Dispose()
    {
        _puertaAna.TrySetResult();
        try { _masiva.Tarea.Wait(TimeSpan.FromSeconds(5)); } catch (AggregateException) { }
        _proveedor.Dispose();
        _bd.Dispose();
    }

    private PerfilActual Perfil(string id)
    {
        var p = new PerfilActual(_gestor);
        p.Establecer(new Perfil { Id = id, Nombre = id });
        return p;
    }

    [Fact]
    public async Task DetenerYOlvidarEsperaAQueLaConsultaSueltePeroNoParaSiempre()
    {
        var ana = Perfil("aaaa0001");
        _masiva.Iniciar(Materias, PeriodoAna, false, ana);
        await _entroAna.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // La consulta no suelta la base (está esperando a Banner): con poco tiempo, el borrado se rechaza y el resumen sigue.
        Assert.False(await _masiva.DetenerYOlvidarAsync(ana, TimeSpan.FromMilliseconds(150)));
        Assert.NotNull(_masiva.EstadoDe(ana));

        _puertaAna.TrySetResult();
        Assert.True(await _masiva.DetenerYOlvidarAsync(ana, TimeSpan.FromSeconds(10)));
        Assert.Null(_masiva.EstadoDe(ana));   // ya no queda nada de ese perfil en memoria
        Assert.True(await _masiva.DetenerYOlvidarAsync(Perfil("bbbb0002"), TimeSpan.FromMilliseconds(50)));   // uno sin consulta: nada que esperar
    }

    [Fact]
    public async Task CadaPerfilTieneSuPropiaConsultaYSuPropioResumen()
    {
        var ana = Perfil("aaaa0001");
        var beto = Perfil("bbbb0002");

        Assert.True(_masiva.Iniciar(Materias, PeriodoAna, false, ana).Iniciada);
        await _entroAna.Task.WaitAsync(TimeSpan.FromSeconds(10));   // (la base de la prueba es una sola)

        Assert.True(_masiva.EstadoDe(ana)!.Activa);
        Assert.Null(_masiva.EstadoDe(beto));   // Beto no ve la consulta de Ana
        Assert.Null(_masiva.Estado);

        // Ana no puede empezar otra mientras la suya corre; Beto sí puede pedir la suya (cada uno ve solo lo suyo)...
        var otraDeAna = _masiva.Iniciar(Materias, PeriodoAna, false, ana);
        Assert.False(otraDeAna.Iniciada);
        Assert.Contains("Ya hay una consulta", otraDeAna.Mensaje);
        Assert.True(_masiva.Iniciar(Materias, PeriodoBeto, false, beto).Iniciada);
        Assert.Equal(PeriodoAna, _masiva.EstadoDe(ana)!.Periodo);

        // ...aunque las consultas a Banner van de a una en toda la aplicación: la de Beto termina enseguida, avisando que hay otra en curso.
        await Espera.HastaAsync(() => _masiva.EstadoDe(beto)!.Terminada);
        var deBeto = _masiva.EstadoDe(beto)!;
        Assert.Equal(PeriodoBeto, deBeto.Periodo);
        Assert.False(deBeto.Exito);
        Assert.Contains("Ya hay una consulta a Banner en curso", deBeto.Mensaje);
        Assert.True(_masiva.EstadoDe(ana)!.Activa);   // la de Ana no se enteró
        Assert.False(_masiva.Cancelar(beto));         // la de Beto ya terminó: no hay nada que cancelar

        // Cancelar es de cada uno: cancelar la de Ana no toca el resumen de Beto.
        Assert.True(_masiva.Cancelar(ana));
        Assert.Equal("Cancelando…", _masiva.EstadoDe(ana)!.Fase);
        Assert.False(_masiva.EstadoDe(beto)!.Cancelada);

        _puertaAna.TrySetResult();
        await _masiva.Tarea;
        Assert.True(_masiva.EstadoDe(ana)!.Cancelada);
        Assert.False(_masiva.EstadoDe(beto)!.Cancelada);
        Assert.False(_masiva.Cancelar(ana));
    }
}