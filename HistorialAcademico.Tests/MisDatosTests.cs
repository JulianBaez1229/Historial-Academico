using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HistorialAcademico.Core.Perfiles;
using HistorialAcademico.Web.Data;
using HistorialAcademico.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static HistorialAcademico.Tests.PerfilesPantallasTests;

namespace HistorialAcademico.Tests;

/// <summary>Exportar y borrar mis datos: el archivo JSON, la sesión de Banner y el borrado total con confirmación explícita.</summary>
public class MisDatosTests
{
    private static async Task<string> PaginaAsync(HttpClient c, string url) => WebUtility.HtmlDecode(await c.GetStringAsync(url));

    private static string Sesion(AppConPerfilesFactory app, string id) => Path.Combine(app.Gestor.Almacen.CarpetaDe(id), ".auth", "banner.json");

    /// <summary>Deja en la carpeta del perfil una «sesión de Banner» y una captura de página, como las que deja Playwright.</summary>
    private static void SembrarSesion(AppConPerfilesFactory app, string id)
    {
        var carpeta = app.Gestor.Almacen.CarpetaDe(id);
        Directory.CreateDirectory(Path.Combine(carpeta, ".auth"));
        File.WriteAllText(Sesion(app, id), "{\"cookies\":[{\"name\":\"SESION\",\"value\":\"SECRETO-COOKIE-123\"}]}");
        Directory.CreateDirectory(Path.Combine(carpeta, "samples"));
        File.WriteAllText(Path.Combine(carpeta, "samples", "historico.html"), "<html>DATOS-PERSONALES-EN-CAPTURA</html>");
    }

    private static async Task<(HttpClient Cliente, string Id)> PerfilAsync(AppConPerfilesFactory app, string nombre, string? pin = null)
    {
        var c = app.Cliente();
        await CrearAsync(c, nombre, pin);
        var id = app.Gestor.Almacen.Listar().Single(p => p.Nombre == nombre).Id;
        return (c, id);
    }

    private static Task<(HttpResponseMessage, string)> BorrarTodoAsync(HttpClient c, string? confirmacion, string? pin = null) =>
        PostAsync(c, "/MisDatos", "/MisDatos/BorrarTodo", ("confirmacion", confirmacion ?? ""), ("pin", pin ?? ""));

    // ── Exportar ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LaExportacionTraeTodoLoTuyoEnUnJsonLegible()
    {
        using var app = new AppConDatosFactory();
        await app.InitializeAsync();
        using var c = app.CreateClient();
        await c.GetStringAsync("/Planificador");   // deja un escenario de planificación

        var r = await c.GetAsync("/MisDatos/Exportar");
        var texto = await r.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("application/json", r.Content.Headers.ContentType!.MediaType);
        Assert.Matches(@"^historial-academico-datos-\d{4}-\d{2}-\d{2}\.json$", (r.Content.Headers.ContentDisposition!.FileNameStar ?? r.Content.Headers.ContentDisposition.FileName)!.Trim('"'));

        using var doc = JsonDocument.Parse(texto);
        var raiz = doc.RootElement;
        Assert.Equal("historial-academico-exportacion", raiz.GetProperty("formato").GetString());
        Assert.Equal(1, raiz.GetProperty("version").GetInt32());
        Assert.True(DateTime.TryParse(raiz.GetProperty("exportado").GetString(), out _));

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HistorialContext>();
        Assert.Equal(await db.Periodos.CountAsync(), raiz.GetProperty("historico").GetArrayLength());
        Assert.Equal(await db.MateriasCursadas.CountAsync(), raiz.GetProperty("historico").EnumerateArray().Sum(p => p.GetProperty("materias").GetArrayLength()));
        Assert.Equal(await db.MateriasPensum.CountAsync(), raiz.GetProperty("pensum").GetArrayLength());
        Assert.Equal(await db.Equivalencias.CountAsync(), raiz.GetProperty("equivalencias").GetArrayLength());
        Assert.NotEqual(JsonValueKind.Null, raiz.GetProperty("alumno").ValueKind);
        Assert.True(raiz.GetProperty("planificador").GetProperty("escenarios").GetArrayLength() >= 1);
        Assert.Equal(JsonValueKind.Null, raiz.GetProperty("perfil").ValueKind);   // sin perfiles (base fija)
        Assert.Contains("\"calificacion\": \"", texto);            // camelCase y con sangría
        Assert.DoesNotContain("\\u00", texto);                       // las tildes van tal cual, no escapadas
        Assert.Contains("Análisis", texto);
    }

    [Fact]
    public async Task LaExportacionNoTraeElPinNiLaSesionNiLasCapturasDeBanner()
    {
        using var app = new AppConPerfilesFactory();
        var (c, id) = await PerfilAsync(app, "Ana María", "mi-pin-9876");
        SembrarSesion(app, id);
        // Con PIN el navegador nuevo tiene que entrar; este cliente ya lo hizo al crear el perfil.

        var r = await c.GetAsync("/MisDatos/Exportar");
        var texto = await r.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        using var doc = JsonDocument.Parse(texto);
        var perfil = doc.RootElement.GetProperty("perfil");
        Assert.Equal("Ana María", perfil.GetProperty("nombre").GetString());
        Assert.True(perfil.GetProperty("protegidoConPin").GetBoolean());
        foreach (var prohibido in new[] { "mi-pin-9876", "pbkdf2", "pinHash", "SECRETO-COOKIE-123", "cookies", "DATOS-PERSONALES-EN-CAPTURA", id })
            Assert.DoesNotContain(prohibido, texto);
        Assert.Matches(@"^historial-academico-ana-mar-a-\d{4}-\d{2}-\d{2}\.json$", (r.Content.Headers.ContentDisposition!.FileNameStar ?? r.Content.Headers.ContentDisposition.FileName)!.Trim('"'));
    }

    [Fact]
    public async Task LaExportacionIncluyeLasMateriasARmanoYSoloLasDeEsePerfil()
    {
        using var app = new AppConPerfilesFactory();
        var (ana, idAna) = await PerfilAsync(app, "Ana");
        var (beto, _) = await PerfilAsync(app, "Beto");
        string codigo;
        using (var scope = app.Abrir(idAna))
        {
            var db = scope.ServiceProvider.GetRequiredService<HistorialContext>();
            db.MateriasPensum.Add(new() { Codigo = "ISO200", Nombre = "Programación I", Creditos = 4, Cuatrimestre = 1 });
            db.MateriasManuales.Add(new() { Codigo = "ISO200", Periodo = "ENE-ABR 2026", Calificacion = "A", Creada = DateTime.UtcNow });
            await db.SaveChangesAsync();
            codigo = "ISO200";
        }

        using var deAna = JsonDocument.Parse(await ana.GetStringAsync("/MisDatos/Exportar"));
        using var deBeto = JsonDocument.Parse(await beto.GetStringAsync("/MisDatos/Exportar"));

        var manual = Assert.Single(deAna.RootElement.GetProperty("materiasManuales").EnumerateArray());
        Assert.Equal((codigo, "ENE-ABR 2026", "A"), (manual.GetProperty("codigo").GetString(), manual.GetProperty("periodo").GetString(), manual.GetProperty("calificacion").GetString()));
        Assert.Equal(0, deBeto.RootElement.GetProperty("materiasManuales").GetArrayLength());
        Assert.Equal("Beto", deBeto.RootElement.GetProperty("perfil").GetProperty("nombre").GetString());
    }

    // ── La pantalla ───────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LaPantallaDiceQueHayGuardadoYDondeYTieneSusBotones()
    {
        using var app = new AppConPerfilesFactory();
        var (c, id) = await PerfilAsync(app, "Ana", "clave-1234");
        SembrarSesion(app, id);

        var html = await PaginaAsync(c, "/MisDatos");

        Assert.Contains("Mis datos y privacidad", html);
        Assert.Contains(app.Gestor.Almacen.CarpetaDe(id), html);
        Assert.Contains("historial.db", html);
        Assert.Contains("guardada. Es como una llave", html);   // la sesión de Banner es delicada y se dice
        Assert.Contains("Capturas de las páginas de Banner", html);
        Assert.Contains("Tu PIN, guardado solo de forma cifrada", html);
        Assert.Contains("no se guardan en ningún lado", html);   // usuario y contraseña de Banner
        Assert.Contains("Descargar mis datos (JSON)", html);
        Assert.Contains("Borrar todos mis datos", html);
        Assert.Contains("Tu PIN</label>", html);                  // con PIN se pide para borrar
        Assert.Contains("Mis datos", html);                       // ítem del menú
    }

    [Fact]
    public async Task SinPinNoSePideElPinYSinSesionElBotonDeSesionEstaInactivo()
    {
        using var app = new AppConPerfilesFactory();
        var (c, _) = await PerfilAsync(app, "Ana");

        var html = await PaginaAsync(c, "/MisDatos");

        Assert.DoesNotContain("Tu PIN</label>", html);
        Assert.Contains("no hay ninguna guardada", html);
        Assert.Contains("No hay nada que borrar", html);
    }

    [Fact]
    public async Task ConUnaBaseFijaSeExportaPeroNoHayPerfilQueBorrar()
    {
        using var app = new AppFactory();
        await app.InitializeAsync();
        using var c = app.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var (estado, html) = await app.GetAsync("/MisDatos");

        Assert.Equal(HttpStatusCode.OK, estado);
        Assert.Contains("sin perfiles", html);
        Assert.DoesNotContain("Borrar todos mis datos", html);
        var token = Regex.Match(await c.GetStringAsync("/Estudiante/DatosPersonales"), "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        var r = await c.PostAsync("/MisDatos/BorrarTodo", new FormUrlEncodedContent(new Dictionary<string, string> { ["confirmacion"] = "x", ["__RequestVerificationToken"] = token }));
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);   // no hay nada que borrar: vuelve a la pantalla con el aviso
    }

    [Fact]
    public async Task LaVersionAnteriorSeMencionaPeroNuncaSeBorraDesdeAqui()
    {
        using var app = new AppConPerfilesFactory();
        Directory.CreateDirectory(app.CarpetaAnterior);
        File.WriteAllText(app.BaseAnterior, "base de la version anterior");
        var (c, _) = await PerfilAsync(app, "Ana");

        var html = await PaginaAsync(c, "/MisDatos");
        Assert.Contains(app.BaseAnterior, html);
        Assert.Contains("bórralo tú", html);

        await BorrarTodoAsync(c, "Ana");

        Assert.Equal("base de la version anterior", File.ReadAllText(app.BaseAnterior));
    }

    // ── Borrar la sesión de Banner ────────────────────────────────────────────────────────

    [Fact]
    public async Task BorrarLaSesionQuitaSesionYCapturasPeroDejaLosDatos()
    {
        using var app = new AppConPerfilesFactory();
        var (c, id) = await PerfilAsync(app, "Ana");
        SembrarSesion(app, id);
        var baseDeDatos = app.Gestor.Almacen.RutaBase(id);

        var (r, _) = await PostAsync(c, "/MisDatos", "/MisDatos/BorrarSesion");

        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.False(Directory.Exists(Path.Combine(app.Gestor.Almacen.CarpetaDe(id), ".auth")));
        Assert.False(Directory.Exists(Path.Combine(app.Gestor.Almacen.CarpetaDe(id), "samples")));
        Assert.True(File.Exists(baseDeDatos));
        Assert.True(new FileInfo(baseDeDatos).Length > 0);
        Assert.NotNull(app.Gestor.Almacen.Obtener(id));
        var html = await PaginaAsync(c, "/MisDatos");
        Assert.Contains("Borré la sesión de Banner", html);
        Assert.Contains("no hay ninguna guardada", html);
    }

    // ── Borrar todo ───────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task BorrarTodoQuitaBaseSesionYPerfilYNoTocaAOtroPerfil()
    {
        using var app = new AppConPerfilesFactory();
        var (ana, idAna) = await PerfilAsync(app, "Ana");
        var (_, idBeto) = await PerfilAsync(app, "Beto");
        SembrarSesion(app, idAna);
        SembrarSesion(app, idBeto);
        using (var scope = app.Abrir(idAna))
        {
            scope.ServiceProvider.GetRequiredService<HistorialContext>().MateriasManuales.Add(new() { Codigo = "ISO200", Periodo = "ENE-ABR 2026", Calificacion = "A", Creada = DateTime.UtcNow });
            await scope.ServiceProvider.GetRequiredService<HistorialContext>().SaveChangesAsync();
        }
        var carpetaAna = app.Gestor.Almacen.CarpetaDe(idAna);
        Assert.True(File.Exists(app.Gestor.Almacen.RutaBase(idAna)));

        var (r, _) = await BorrarTodoAsync(ana, "  ana ");   // el nombre, sin importar mayúsculas ni espacios de más

        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Equal("/Perfiles", r.Headers.Location!.OriginalString);
        Assert.False(Directory.Exists(carpetaAna), "Se borra la carpeta entera: base de datos, sesión y capturas.");
        Assert.Null(app.Gestor.Almacen.Obtener(idAna));
        Assert.Equal("Beto", Assert.Single(app.Gestor.Almacen.Listar()).Nombre);
        Assert.True(File.Exists(app.Gestor.Almacen.RutaBase(idBeto)));   // el otro perfil, intacto
        Assert.True(File.Exists(Sesion(app, idBeto)));

        // La cookie ya no vale: hay que elegir perfil de nuevo, y el mensaje lo confirma.
        var lista = await ana.GetAsync("/Perfiles");
        Assert.Contains("Borré todos los datos de «Ana»", WebUtility.HtmlDecode(await lista.Content.ReadAsStringAsync()));
        // (Beto es el único perfil que queda y no tiene PIN, así que un navegador sin cookie entra a él; el de Ana ya no entra a nada de Ana.)
        var portada = WebUtility.HtmlDecode(await (await ana.GetAsync("/")).Content.ReadAsStringAsync());
        Assert.Contains("Beto", portada);
    }

    [Fact]
    public async Task SiEraElUnicoPerfilSeVuelveAlPrimerUsoConElMensaje()
    {
        using var app = new AppConPerfilesFactory();
        var (c, id) = await PerfilAsync(app, "Ana");

        await BorrarTodoAsync(c, "Ana");

        Assert.Empty(app.Gestor.Almacen.Listar());
        Assert.False(Directory.Exists(app.Gestor.Almacen.CarpetaDe(id)));
        var desvio = await c.GetAsync("/");
        Assert.Equal("/Asistente/Bienvenida", desvio.Headers.Location!.OriginalString);
        var bienvenida = WebUtility.HtmlDecode(await c.GetStringAsync("/Asistente/Bienvenida"));
        Assert.Contains("Borré todos los datos de «Ana»", bienvenida);
        Assert.Contains("borrarlos todos", bienvenida);   // y la bienvenida ya promete lo que existe
    }

    [Theory]
    [InlineData("")]
    [InlineData("Beto")]
    [InlineData("An")]
    [InlineData("Ana Ana")]
    public async Task SinLaConfirmacionEscritaNoSeBorraNada(string confirmacion)
    {
        using var app = new AppConPerfilesFactory();
        var (c, id) = await PerfilAsync(app, "Ana");
        SembrarSesion(app, id);

        var (r, html) = await BorrarTodoAsync(c, confirmacion);

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Contains("No borré nada: para confirmar, escribe el nombre de tu perfil («Ana»)", html);
        Assert.True(File.Exists(app.Gestor.Almacen.RutaBase(id)));
        Assert.True(File.Exists(Sesion(app, id)));
        Assert.NotNull(app.Gestor.Almacen.Obtener(id));
    }

    [Fact]
    public async Task UnPerfilConPinPideElPinParaBorrar()
    {
        using var app = new AppConPerfilesFactory();
        var (c, id) = await PerfilAsync(app, "Ana", "clave-1234");

        var (sinPin, htmlSinPin) = await BorrarTodoAsync(c, "Ana");
        var (malPin, htmlMalPin) = await BorrarTodoAsync(c, "Ana", "otra");

        Assert.Equal(HttpStatusCode.OK, sinPin.StatusCode);
        Assert.Contains("El PIN no es correcto", htmlSinPin);
        Assert.Contains("El PIN no es correcto", htmlMalPin);
        Assert.NotNull(app.Gestor.Almacen.Obtener(id));
        Assert.Equal(2, app.Gestor.Almacen.Obtener(id)!.Fallos);   // cuenta como intento fallido, como en la entrada

        var (bien, _) = await BorrarTodoAsync(c, "Ana", "clave-1234");
        Assert.Equal(HttpStatusCode.Redirect, bien.StatusCode);
        Assert.Null(app.Gestor.Almacen.Obtener(id));
        Assert.False(Directory.Exists(app.Gestor.Almacen.CarpetaDe(id)));
    }

    [Fact]
    public async Task ConVariosPinIncorrectosSeguidosElBorradoEsperaComoLaEntrada()
    {
        using var app = new AppConPerfilesFactory();
        var (c, id) = await PerfilAsync(app, "Ana", "clave-1234");

        string html = "";
        for (var i = 0; i < 5; i++) (_, html) = await BorrarTodoAsync(c, "Ana", "mal");
        var (_, conElBueno) = await BorrarTodoAsync(c, "Ana", "clave-1234");

        Assert.Contains("El PIN no es correcto", html);
        Assert.Contains("Demasiados intentos con el PIN", conElBueno);
        Assert.NotNull(app.Gestor.Almacen.Obtener(id));
    }

    [Fact]
    public async Task SiUnArchivoEstaTomadoNoSeBorraNadaAMediasYElPerfilSigueParaReintentar()
    {
        using var app = new AppConPerfilesFactory();
        var (c, id) = await PerfilAsync(app, "Ana");
        SembrarSesion(app, id);
        var tomado = Path.Combine(app.Gestor.Almacen.CarpetaDe(id), "samples", "historico.html");

        HttpResponseMessage intento;
        using (new FileStream(tomado, FileMode.Open, FileAccess.Read, FileShare.None))   // como si otro programa lo tuviera abierto
            (intento, _) = await BorrarTodoAsync(c, "Ana");

        Assert.Equal("/Perfiles", intento.Headers.Location!.OriginalString);   // sale a una pantalla que no lee la base (pudo quedar a medias)
        var aviso = WebUtility.HtmlDecode(await c.GetStringAsync("/Perfiles"));
        Assert.Contains("No pude borrarlo todo", aviso);
        Assert.Contains("tu perfil sigue en la lista", aviso);
        Assert.NotNull(app.Gestor.Almacen.Obtener(id));
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/MisDatos")).StatusCode);   // y se puede volver a entrar: la base se vuelve a crear

        // Ya libre, se puede reintentar.
        var (r, _) = await BorrarTodoAsync(c, "Ana");
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Null(app.Gestor.Almacen.Obtener(id));
    }

    [Fact]
    public async Task BorrarSoloAfectaALaCarpetaDelPerfilNadaMasEnLaCarpetaDelUsuario()
    {
        using var app = new AppConPerfilesFactory();
        var (c, _) = await PerfilAsync(app, "Ana");
        var ajeno = Path.Combine(app.CarpetaUsuario, "otro-archivo-del-usuario.txt");
        File.WriteAllText(ajeno, "no es del perfil");

        await BorrarTodoAsync(c, "Ana");

        Assert.Equal("no es del perfil", File.ReadAllText(ajeno));
        Assert.True(Directory.Exists(app.CarpetaUsuario));
    }
}

/// <summary>Borrar un perfil desde el almacén: la carpeta primero, para no dejar datos sueltos.</summary>
public sealed class BorrarPerfilEnElAlmacenTests : IDisposable
{
    private readonly string _raiz = Path.Combine(Path.GetTempPath(), "almacen-borrar-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_raiz, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void SiLaCarpetaNoSePuedeBorrarElPerfilSigueEnLaLista()
    {
        var almacen = new AlmacenPerfiles(_raiz, iteracionesPin: 1000);
        var id = almacen.Crear("Ana", null).Perfil!.Id;
        var archivo = Path.Combine(almacen.CarpetaDe(id), "historial.db");
        File.WriteAllText(archivo, "datos");

        using (new FileStream(archivo, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.Throws<IOException>(() => almacen.Borrar(id));

        Assert.NotNull(almacen.Obtener(id));   // sigue en la lista: se puede reintentar
        Assert.True(almacen.Borrar(id));
        Assert.Null(almacen.Obtener(id));
        Assert.False(Directory.Exists(almacen.CarpetaDe(id)));
    }

    [Fact]
    public void BorrarUnPerfilSinCarpetaTambienLoQuita()
    {
        var almacen = new AlmacenPerfiles(_raiz, iteracionesPin: 1000);
        var id = almacen.Crear("Ana", null).Perfil!.Id;
        Directory.Delete(almacen.CarpetaDe(id));

        Assert.True(almacen.Borrar(id));
        Assert.Empty(almacen.Listar());
    }
}
