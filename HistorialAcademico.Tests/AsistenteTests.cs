using System.Net;
using HistorialAcademico.Banner;
using HistorialAcademico.Core.Perfiles;
using HistorialAcademico.Web.Controllers;
using HistorialAcademico.Web.Data;
using HistorialAcademico.Web.Perfiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static HistorialAcademico.Tests.PerfilesPantallasTests;

namespace HistorialAcademico.Tests;

/// <summary>Una aplicación con perfiles y un Banner falso: el asistente nunca abre un navegador ni toca Banner de verdad.</summary>
public class AppConPerfilesYBannerFalsoFactory : AppConPerfilesFactory
{
    private readonly BannerFalso _banner = new();
    public override BannerFalso? Banner => _banner;
    public BannerFalso Falso => _banner;
}

/// <summary>Igual, pero sin «Banner:BaseUrl»: la dirección de Banner solo puede salir de la universidad que elija el perfil.</summary>
public class AppConPerfilesSinUrlFactory : AppConPerfilesYBannerFalsoFactory
{
    protected override string BaseUrl => "";
}

/// <summary>El asistente de primer uso, de la bienvenida a conectar Banner, como lo recorrería el navegador.</summary>
public class AsistenteTests
{
    private const string ClaveUnapec = "unapec/ingenieria-software-11.json";

    private static async Task<string> GetAsync(HttpClient c, string url) => WebUtility.HtmlDecode(await c.GetStringAsync(url));

    private static Perfil Unico(AppConPerfilesFactory app) => app.Gestor.Almacen.Listar().Single();

    private static async Task<HttpClient> ConPerfilEnElAsistenteAsync(AppConPerfilesFactory app, string nombre = "Ana", string? pin = null)
    {
        var c = app.Cliente();
        var (r, _) = await CrearAsync(c, nombre, pin, terminar: false);
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        return c;
    }

    // ── Paso 1 y 2 ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LaBienvenidaExplicaLaPrivacidadYLlevaACrearElPerfil()
    {
        using var app = new AppConPerfilesFactory();
        using var c = app.Cliente();

        var html = await GetAsync(c, "/Asistente/Bienvenida");

        Assert.Contains("Paso 1 de 4", html);
        Assert.Contains("en tu computadora", html);
        Assert.Contains("No hay servidor central", html);
        Assert.Contains("nunca ve ni guarda tu usuario ni tu contraseña", html);
        Assert.Contains(app.Gestor.Almacen.Raiz, html);   // dice dónde quedan los datos
        Assert.Contains("href=\"/Perfiles/Crear\"", html);
        Assert.DoesNotContain("Actualizar desde Banner", html);
    }

    [Fact]
    public async Task ElPrimerPerfilMuestraElPaso2YPuedeVolverALaBienvenida()
    {
        using var app = new AppConPerfilesFactory();
        using var c = app.Cliente();

        var html = await GetAsync(c, "/Perfiles/Crear");

        Assert.Contains("Paso 2 de 4", html);
        Assert.Contains("href=\"/Asistente/Bienvenida\"", html);
    }

    [Fact]
    public async Task UnPerfilAdicionalNoMuestraElPasoDeBienvenida()
    {
        using var app = new AppConPerfilesFactory();
        using var c = app.Cliente();
        await CrearAsync(c, "Ana");

        var html = await GetAsync(c, "/Perfiles/Crear");

        Assert.DoesNotContain("Paso 2 de 4", html);
    }

    // ── El progreso se guarda ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task CrearElPerfilLlevaAlPasoDeCarreraYTodoLoDemasVuelveAlAsistente()
    {
        using var app = new AppConPerfilesFactory();
        using var c = app.Cliente();

        var (r, _) = await CrearAsync(c, "Ana", terminar: false);

        Assert.Equal("/Asistente", r.Headers.Location!.OriginalString);
        Assert.Equal(PasosAsistente.Carrera, Unico(app).PasoAsistente);
        var indice = await c.GetAsync("/Asistente");
        Assert.Equal("/Asistente/Carrera", indice.Headers.Location!.OriginalString);
        foreach (var ruta in new[] { "/", "/Estudiante/MateriasTomadas", "/Horarios", "/Carrera", "/Planificador" })
        {
            var desvio = await c.GetAsync(ruta);
            Assert.Equal(HttpStatusCode.Redirect, desvio.StatusCode);
            Assert.Equal("/Asistente", desvio.Headers.Location!.OriginalString);
        }
    }

    [Fact]
    public async Task SiSeCierraLaAplicacionAMediasSeSigueDondeQuedo()
    {
        using var app = new AppConPerfilesFactory();
        using (var primero = await ConPerfilEnElAsistenteAsync(app))
        {
            var (r, _) = await PostAsync(primero, "/Asistente/Carrera", "/Asistente/OmitirCarrera");   // ya va en el paso 4
            Assert.Equal("/Asistente/Banner", r.Headers.Location!.OriginalString);
        }

        // Otro navegador (sin cookie), como al volver a abrir la aplicación: entra directo al perfil y cae en el paso 4.
        using var otro = app.Cliente();
        var desvio = await otro.GetAsync("/");
        Assert.Equal("/Asistente", desvio.Headers.Location!.OriginalString);
        Assert.Equal("/Asistente/Banner", (await otro.GetAsync("/Asistente")).Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task ConPinTambienSeSigueDondeQuedoDespuesDeEntrar()
    {
        using var app = new AppConPerfilesFactory();
        using (var primero = await ConPerfilEnElAsistenteAsync(app, "Ana", "clave-1234")) { }
        using var c = app.Cliente();

        Assert.Equal("/Perfiles", (await c.GetAsync("/")).Headers.Location!.OriginalString);
        var (r, _) = await PostAsync(c, "/Perfiles", "/Perfiles/Entrar", ("id", Unico(app).Id), ("pin", "clave-1234"));

        Assert.Equal("/", r.Headers.Location!.OriginalString);
        Assert.Equal("/Asistente", (await c.GetAsync("/")).Headers.Location!.OriginalString);
    }

    // ── Paso 3: universidad y carrera ─────────────────────────────────────────────────────

    [Fact]
    public async Task ElPasoDeCarreraListaElCatalogoYSePuedeFiltrar()
    {
        using var app = new AppConPerfilesFactory();
        using var c = await ConPerfilEnElAsistenteAsync(app);

        var todo = await GetAsync(c, "/Asistente/Carrera");
        Assert.Contains("Paso 3 de 4", todo);
        Assert.Contains("Ingeniería de Software", todo);
        Assert.Contains($"data-clave=\"{ClaveUnapec}\"", todo);

        var sinResultados = await GetAsync(c, "/Asistente/Carrera?q=zzzz-no-existe");
        Assert.Contains("Ningún pénsum coincide", sinResultados);
        Assert.DoesNotContain("tabla-carreras", sinResultados);
    }

    [Fact]
    public async Task ElegirUnaCarreraLaActivaGuardaLaUniversidadYPasaAlPasoDeBanner()
    {
        using var app = new AppConPerfilesFactory();
        using var c = await ConPerfilEnElAsistenteAsync(app);

        var (r, _) = await PostAsync(c, "/Asistente/Carrera", "/Asistente/Carrera", ("clave", ClaveUnapec));

        Assert.Equal("/Asistente/Banner", r.Headers.Location!.OriginalString);
        var perfil = Unico(app);
        Assert.Equal(PasosAsistente.Banner, perfil.PasoAsistente);
        Assert.Equal("unapec", perfil.UniversidadId);
        using var scope = app.Abrir(perfil.Id);
        Assert.Equal(ClaveUnapec, (await scope.ServiceProvider.GetRequiredService<HistorialContext>().PensumActivo.SingleAsync()).Clave);
        Assert.Contains("Elegido", await GetAsync(c, "/Asistente/Carrera"));
    }

    [Fact]
    public async Task UnaCarreraQueNoExisteSeAvisaYNoAvanza()
    {
        using var app = new AppConPerfilesFactory();
        using var c = await ConPerfilEnElAsistenteAsync(app);

        var (r, _) = await PostAsync(c, "/Asistente/Carrera", "/Asistente/Carrera", ("clave", "unapec/no-existe-1.json"));

        Assert.Equal("/Asistente/Carrera", r.Headers.Location!.OriginalString);
        Assert.Equal(PasosAsistente.Carrera, Unico(app).PasoAsistente);
        Assert.Contains("alert-danger", await GetAsync(c, "/Asistente/Carrera"));
    }

    [Fact]
    public async Task LaCarreraSePuedeOmitirYHacerDespues()
    {
        using var app = new AppConPerfilesFactory();
        using var c = await ConPerfilEnElAsistenteAsync(app);

        var (r, _) = await PostAsync(c, "/Asistente/Carrera", "/Asistente/OmitirCarrera");

        Assert.Equal("/Asistente/Banner", r.Headers.Location!.OriginalString);
        Assert.Null(Unico(app).UniversidadId);
    }

    // ── Retroceder ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SePuedeRetrocederEntrePasosYElProgresoSigueAlUltimoVisitado()
    {
        using var app = new AppConPerfilesFactory();
        using var c = await ConPerfilEnElAsistenteAsync(app);
        await PostAsync(c, "/Asistente/Carrera", "/Asistente/OmitirCarrera");
        Assert.Equal(PasosAsistente.Banner, Unico(app).PasoAsistente);

        Assert.Contains("Paso 3 de 4", await GetAsync(c, "/Asistente/Carrera"));   // atrás
        Assert.Equal(PasosAsistente.Carrera, Unico(app).PasoAsistente);
        var perfil = await GetAsync(c, "/Asistente/Perfil");                       // y otra vez atrás
        Assert.Contains("Paso 2 de 4", perfil);
        Assert.Equal(PasosAsistente.Perfil, Unico(app).PasoAsistente);

        // Cerrar la aplicación aquí retoma en el paso 2.
        Assert.Equal("/Asistente/Perfil", (await c.GetAsync("/Asistente")).Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task ElPasoDePerfilPermiteCambiarElNombreYSigueAlPasoDeCarrera()
    {
        using var app = new AppConPerfilesFactory();
        using var c = await ConPerfilEnElAsistenteAsync(app, "Ana");
        await GetAsync(c, "/Asistente/Perfil");

        var (r, _) = await PostAsync(c, "/Asistente/Perfil", "/Asistente/Perfil", ("nombre", "  Ana María "));

        Assert.Equal("/Asistente/Carrera", r.Headers.Location!.OriginalString);
        Assert.Equal("Ana María", Unico(app).Nombre);
        Assert.Equal(PasosAsistente.Carrera, Unico(app).PasoAsistente);
    }

    [Fact]
    public async Task ElPasoDePerfilRechazaUnNombreVacioOQueYaTieneOtroPerfil()
    {
        using var app = new AppConPerfilesFactory();
        using (var otro = app.Cliente()) await CrearAsync(otro, "Beto");
        using var c = await ConPerfilEnElAsistenteAsync(app, "Ana");
        await GetAsync(c, "/Asistente/Perfil");

        var (vacio, htmlVacio) = await PostAsync(c, "/Asistente/Perfil", "/Asistente/Perfil", ("nombre", " "));
        var (repetido, htmlRepetido) = await PostAsync(c, "/Asistente/Perfil", "/Asistente/Perfil", ("nombre", "beto"));

        Assert.Equal(HttpStatusCode.OK, vacio.StatusCode);
        Assert.Contains("Escribe tu nombre.", htmlVacio);
        Assert.Contains("Ya hay un perfil llamado", htmlRepetido);
        Assert.Contains("Ana", app.Gestor.Almacen.Listar().Select(p => p.Nombre));
    }

    [Fact]
    public async Task ConservarElMismoNombreNoEsUnErrorAunqueYaExista()
    {
        using var app = new AppConPerfilesFactory();
        using var c = await ConPerfilEnElAsistenteAsync(app, "Ana");
        await GetAsync(c, "/Asistente/Perfil");

        var (r, _) = await PostAsync(c, "/Asistente/Perfil", "/Asistente/Perfil", ("nombre", "Ana"));

        Assert.Equal("/Asistente/Carrera", r.Headers.Location!.OriginalString);
    }

    // ── Paso 4: conectar Banner ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ElPasoDeBannerExplicaQueElInicioDeSesionLoHacesTu()
    {
        using var app = new AppConPerfilesYBannerFalsoFactory();
        using var c = await ConPerfilEnElAsistenteAsync(app);

        var html = await GetAsync(c, "/Asistente/Banner");

        Assert.Contains("Paso 4 de 4", html);
        Assert.Contains("Opcional", html);
        Assert.Contains("alumnos.invalid", html);   // la dirección configurada
        Assert.Contains("tú mismo", html);
        Assert.Contains("nunca ve tu usuario ni tu contraseña", html);
        Assert.Contains("Omitir y terminar", html);
        Assert.Contains("Conectar Banner y traer mi histórico", html);
    }

    [Fact]
    public async Task ConectarAbreElInicioDeSesionTraeElHistoricoYTerminaElAsistente()
    {
        using var app = new AppConPerfilesYBannerFalsoFactory();
        app.Falso.Captura = () => Task.FromResult(Muestras.Sintetico);
        using var c = await ConPerfilEnElAsistenteAsync(app);

        var (r, _) = await PostAsync(c, "/Asistente/Banner", "/Asistente/Conectar");

        Assert.Equal("/", r.Headers.Location!.OriginalString);
        Assert.Equal(1, app.Falso.Capturas);
        Assert.Equal(PasosAsistente.Terminado, Unico(app).PasoAsistente);
        var portada = await GetAsync(c, "/");
        Assert.Contains("Banner quedó conectado", portada);
        Assert.Contains("Sincronizado:", portada);
        using var scope = app.Abrir(Unico(app).Id);
        Assert.True(await scope.ServiceProvider.GetRequiredService<HistorialContext>().DatosAlumno.AnyAsync());
    }

    [Fact]
    public async Task SiLaSesionNoExisteOCaducoSePideIniciarSesionYSeReintenta()
    {
        using var app = new AppConPerfilesYBannerFalsoFactory();
        var banner = app.Falso;
        banner.Captura = () => banner.Capturas == 1 ? throw new BannerSesionExpiradaException() : Task.FromResult(Muestras.Sintetico);
        using var c = await ConPerfilEnElAsistenteAsync(app);

        var (r, _) = await PostAsync(c, "/Asistente/Banner", "/Asistente/Conectar");

        Assert.Equal("/", r.Headers.Location!.OriginalString);
        Assert.Equal(1, banner.Logins);    // se abrió la ventana para que la persona inicie sesión
        Assert.Equal(2, banner.Capturas);
        Assert.Equal(PasosAsistente.Terminado, Unico(app).PasoAsistente);
    }

    [Fact]
    public async Task SiConectarFallaSeAvisaYSeSigueEnElPasoParaReintentarOOmitir()
    {
        using var app = new AppConPerfilesYBannerFalsoFactory();
        app.Falso.Captura = () => throw new BannerException("Banner no respondió.");
        using var c = await ConPerfilEnElAsistenteAsync(app);
        await GetAsync(c, "/Asistente/Banner");

        var (r, _) = await PostAsync(c, "/Asistente/Banner", "/Asistente/Conectar");

        Assert.Equal("/Asistente/Banner", r.Headers.Location!.OriginalString);
        Assert.Contains("Banner no respondió.", await GetAsync(c, "/Asistente/Banner"));
        Assert.Equal(PasosAsistente.Banner, Unico(app).PasoAsistente);   // sigue en el asistente
    }

    [Fact]
    public async Task OmitirBannerTerminaElAsistenteYLaAplicacionYaSeAbre()
    {
        using var app = new AppConPerfilesYBannerFalsoFactory();
        using var c = await ConPerfilEnElAsistenteAsync(app);
        await GetAsync(c, "/Asistente/Banner");

        var (r, _) = await PostAsync(c, "/Asistente/Banner", "/Asistente/Terminar");

        Assert.Equal("/", r.Headers.Location!.OriginalString);
        Assert.Equal(0, app.Falso.Capturas + app.Falso.Logins);   // Banner no se tocó
        var portada = await c.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, portada.StatusCode);
        Assert.Contains("Listo, tu perfil está configurado", WebUtility.HtmlDecode(await portada.Content.ReadAsStringAsync()));
        Assert.Equal(PasosAsistente.Terminado, Unico(app).PasoAsistente);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/Estudiante/MateriasTomadas")).StatusCode);
    }

    [Fact]
    public async Task ElMensajeDeTerminarSoloSeMuestraUnaVez()
    {
        using var app = new AppConPerfilesYBannerFalsoFactory();
        using var c = await ConPerfilEnElAsistenteAsync(app);
        await PostAsync(c, "/Asistente/Banner", "/Asistente/Terminar");
        Assert.Contains("Listo, tu perfil está configurado", await GetAsync(c, "/"));

        Assert.DoesNotContain("Listo, tu perfil está configurado", await GetAsync(c, "/"));
    }

    [Fact]
    public async Task UnPerfilQueYaTerminoPuedeVolverAlosPasosSinQueSeCambieSuProgreso()
    {
        using var app = new AppConPerfilesYBannerFalsoFactory();
        using var c = app.Cliente();
        await CrearAsync(c, "Ana");   // termina el asistente

        var carrera = await GetAsync(c, "/Asistente/Carrera");
        var banner = await GetAsync(c, "/Asistente/Banner");

        Assert.DoesNotContain("Paso 3 de 4", carrera);   // sin indicador de pasos: ya no es un primer uso
        Assert.DoesNotContain("Omitir y terminar", banner);
        Assert.Equal(PasosAsistente.Terminado, Unico(app).PasoAsistente);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/")).StatusCode);   // sin desvíos
    }

    [Fact]
    public async Task ConectarDesdeFueraDelAsistenteSeQuedaEnLaPantallaYAvisa()
    {
        using var app = new AppConPerfilesYBannerFalsoFactory();
        app.Falso.Captura = () => Task.FromResult(Muestras.Sintetico);
        using var c = app.Cliente();
        await CrearAsync(c, "Ana");

        var (r, _) = await PostAsync(c, "/Asistente/Banner", "/Asistente/Conectar");

        Assert.Equal("/Asistente/Banner", r.Headers.Location!.OriginalString);
        var html = await GetAsync(c, "/Asistente/Banner");
        Assert.Contains("Banner quedó conectado", html);
        Assert.Contains("Ya hay un histórico en tu perfil", html);
    }

    // ── La dirección de Banner sale de la universidad ─────────────────────────────────────

    [Fact]
    public async Task SinConfiguracionLaDireccionDeBannerEsLaDeLaUniversidadElegida()
    {
        using var app = new AppConPerfilesSinUrlFactory();
        using var c = await ConPerfilEnElAsistenteAsync(app);

        Assert.Contains("Todavía no elegiste tu universidad", await GetAsync(c, "/Asistente/Banner"));   // sin universidad no hay a dónde conectar
        var (sinBanner, _) = await PostAsync(c, "/Asistente/Banner", "/Asistente/Conectar");
        Assert.Equal("/Asistente/Banner", sinBanner.Headers.Location!.OriginalString);
        Assert.Equal(0, app.Falso.Capturas + app.Falso.Logins);
        Assert.Contains("no tiene una dirección de Banner", await GetAsync(c, "/Asistente/Banner"));

        await PostAsync(c, "/Asistente/Carrera", "/Asistente/Carrera", ("clave", ClaveUnapec));
        var html = await GetAsync(c, "/Asistente/Banner");

        Assert.Contains("alumnos.unapec.edu.do", html);
        Assert.Contains("UNAPEC", html);
        using var scope = app.Abrir(Unico(app).Id);
        var opciones = scope.ServiceProvider.GetRequiredService<BannerOptions>();
        Assert.Equal("https://alumnos.unapec.edu.do/StudentSelfService/ssb/studentCommonDashboard", opciones.BaseUrl);
        Assert.Contains("unapec.edu.do", opciones.HostsBanner);
    }

    [Fact]
    public async Task LoQueConfiguresEnBannerBaseUrlManda()
    {
        using var app = new AppConPerfilesYBannerFalsoFactory();
        using var c = await ConPerfilEnElAsistenteAsync(app);
        await PostAsync(c, "/Asistente/Carrera", "/Asistente/Carrera", ("clave", ClaveUnapec));

        using var scope = app.Abrir(Unico(app).Id);

        Assert.Equal("https://alumnos.invalid/", scope.ServiceProvider.GetRequiredService<BannerOptions>().BaseUrl);
    }

    [Theory]
    [InlineData("alumnos.unapec.edu.do", "unapec.edu.do")]
    [InlineData("banner.uni.edu", "uni.edu")]
    [InlineData("uni.edu", "uni.edu")]
    [InlineData("localhost", "localhost")]
    public void ElDominioBaseQuitaElPrimerNombreDelHost(string host, string esperado) =>
        Assert.Equal(esperado, GestorPerfiles.DominioBase(host));

    // ── Casos de borde ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SinPerfilLosPasosQueLoNecesitanLlevanALaBienvenida()
    {
        using var app = new AppConPerfilesFactory();
        using var c = app.Cliente();

        foreach (var ruta in new[] { "/Asistente", "/Asistente/Perfil", "/Asistente/Carrera", "/Asistente/Banner" })
        {
            var r = await c.GetAsync(ruta);
            Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
            Assert.Equal("/Asistente/Bienvenida", r.Headers.Location!.OriginalString);
        }
    }

    [Fact]
    public async Task ElAsistenteNoAtrapaLaAplicacionElPerfilSiempreSeEligeOtraVez()
    {
        using var app = new AppConPerfilesFactory();
        using var c = await ConPerfilEnElAsistenteAsync(app);

        // Mientras está en el asistente sí se puede ir a elegir perfil (es una pantalla exenta).
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/Perfiles")).StatusCode);
    }

    [Fact]
    public async Task EnModoDeBaseFijaElAsistenteNoDesviaNadaYLaBienvenidaSeAbre()
    {
        using var app = new AppFactory();
        await app.InitializeAsync();

        var (estado, html) = await app.GetAsync("/Asistente/Bienvenida");
        Assert.Equal(HttpStatusCode.OK, estado);
        Assert.Contains("Paso 1 de 4", html);
        using var c = app.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        Assert.Equal("/Asistente/Bienvenida", (await c.GetAsync("/Asistente")).Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/")).StatusCode);
    }
}

/// <summary>El paso del asistente que se guarda en el perfil (y cómo se lee lo que ya estaba guardado sin él).</summary>
public sealed class PasoAsistenteEnElAlmacenTests : IDisposable
{
    private readonly string _raiz = Path.Combine(Path.GetTempPath(), "almacen-paso-" + Guid.NewGuid().ToString("N"));

    private AlmacenPerfiles Nuevo() => new(_raiz, iteracionesPin: 1000);

    public void Dispose()
    {
        try { Directory.Delete(_raiz, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void UnPerfilNuevoPuedeEmpezarEnUnPasoYSeGuarda()
    {
        var almacen = Nuevo();

        var perfil = almacen.Crear("Ana", null, PasosAsistente.Carrera).Perfil!;

        Assert.Equal(PasosAsistente.Carrera, Nuevo().Obtener(perfil.Id)!.PasoAsistente);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(-3)]
    public void UnPasoQueNoExisteSeIgnoraAlCrear(int paso) =>
        Assert.Equal(PasosAsistente.Terminado, Nuevo().Crear("Ana", null, paso).Perfil!.PasoAsistente);

    [Fact]
    public void SinIndicarPasoElPerfilNoEstaEnElAsistente() =>
        Assert.Equal(PasosAsistente.Terminado, Nuevo().Crear("Ana", null).Perfil!.PasoAsistente);

    [Fact]
    public void GuardarElPasoLoCambiaYCeroLoTermina()
    {
        var almacen = Nuevo();
        var id = almacen.Crear("Ana", null, PasosAsistente.Carrera).Perfil!.Id;

        almacen.GuardarPaso(id, PasosAsistente.Banner);
        Assert.Equal(PasosAsistente.Banner, Nuevo().Obtener(id)!.PasoAsistente);
        almacen.GuardarPaso(id, PasosAsistente.Terminado);
        Assert.Equal(0, Nuevo().Obtener(id)!.PasoAsistente);
        almacen.GuardarPaso("no-existe", PasosAsistente.Banner);   // no falla
    }

    [Fact]
    public void UnPasoInvalidoAlGuardarEsUnError() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Nuevo().GuardarPaso("x", 7));

    [Fact]
    public void UnArchivoDeUnaVersionAnteriorSinPasoSeLeeComoTerminado()
    {
        Directory.CreateDirectory(_raiz);
        File.WriteAllText(Path.Combine(_raiz, "perfiles.json"),
            "{ \"version\": 1, \"perfiles\": [ { \"id\": \"aabbccdd\", \"nombre\": \"Ana\", \"creado\": \"2027-01-01T00:00:00Z\", \"fallos\": 0 } ] }");

        var perfil = Assert.Single(Nuevo().Listar());

        Assert.Equal(PasosAsistente.Terminado, perfil.PasoAsistente);   // un perfil que ya existía no cae en el asistente
    }

    [Fact]
    public void RenombrarValidaComoAlCrear()
    {
        var almacen = Nuevo();
        var ana = almacen.Crear("Ana", null).Perfil!;
        almacen.Crear("Beto", null);

        Assert.Null(almacen.Renombrar(ana.Id, "  Ana María "));
        Assert.Equal("Ana María", Nuevo().Obtener(ana.Id)!.Nombre);
        Assert.Null(almacen.Renombrar(ana.Id, "ANA MARÍA"));   // el propio nombre no cuenta como repetido
        Assert.Contains("Ya hay un perfil llamado", almacen.Renombrar(ana.Id, "beto"));
        Assert.Equal("Escribe tu nombre.", almacen.Renombrar(ana.Id, " "));
        Assert.Contains("40", almacen.Renombrar(ana.Id, new string('x', 41)));
        Assert.Equal("Ese perfil ya no existe.", almacen.Renombrar("no-existe", "Zoe"));
    }
}
