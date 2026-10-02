using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using HistorialAcademico.Banner;
using HistorialAcademico.Web.Helpers;

namespace HistorialAcademico.Tests;

/// <summary>El programa descargable: el primer arranque (navegador, puerto, apertura) y los flujos de GitHub que lo arman.</summary>
public class DistribucionTests
{
    private static string Ruta(params string[] partes) => Path.Combine(new[] { PensumEjemplo.Raiz }.Concat(partes).ToArray());

    // ── El navegador de Playwright ────────────────────────────────────────────────────────

    [Fact]
    public void SiChromiumYaEstaNoSeInstalaNiSeEscribeNada()
    {
        var salida = new StringWriter();
        var instalaciones = 0;

        var r = new InstaladorNavegador(() => true, _ => { instalaciones++; return 0; }).AsegurarChromium(salida);

        Assert.Equal(ResultadoInstalacion.YaEstaba, r);
        Assert.Equal(0, instalaciones);
        Assert.Equal("", salida.ToString());
    }

    [Fact]
    public void SiFaltaSeInstalaChromiumYSeAvisaDeLaPrimeraVezYDelTamano()
    {
        var salida = new StringWriter();
        string[]? recibidos = null;
        var instalado = false;

        var r = new InstaladorNavegador(() => instalado, args => { recibidos = args; instalado = true; return 0; }).AsegurarChromium(salida);

        Assert.Equal(ResultadoInstalacion.Instalado, r);
        Assert.Equal(new[] { "install", "chromium" }, recibidos);
        var texto = salida.ToString();
        Assert.Contains("Primera vez", texto);
        Assert.Contains("150 MB", texto);
        Assert.Contains("No cierres esta ventana", texto);
        Assert.Contains("Navegador instalado.", texto);
    }

    [Fact]
    public void SiElInstaladorFallaSeCuentaElProblemaYLaAplicacionArrancaIgual()
    {
        var salida = new StringWriter();

        var r = new InstaladorNavegador(() => false, _ => 1).AsegurarChromium(salida);

        Assert.Equal(ResultadoInstalacion.Fallo, r);
        Assert.Contains("No se pudo instalar el navegador (código 1)", salida.ToString());
        Assert.Contains("vuelve a abrir el programa", salida.ToString());
    }

    [Fact]
    public void SiElInstaladorLanzaUnaExcepcionNoSePropagaYDiceElMotivoSinLaPila()
    {
        var salida = new StringWriter();

        var r = new InstaladorNavegador(() => false, _ => throw new IOException("Sin conexión con el servidor\n   en Microsoft.Playwright...")).AsegurarChromium(salida);

        Assert.Equal(ResultadoInstalacion.Fallo, r);
        Assert.Contains("Sin conexión con el servidor", salida.ToString());
        Assert.DoesNotContain("Microsoft.Playwright", salida.ToString());
    }

    [Fact]
    public void ElCodigoDeSalidaCeroSinChromiumInstaladoTambienEsUnFallo()
    {
        var r = new InstaladorNavegador(() => false, _ => 0).AsegurarChromium(new StringWriter());

        Assert.Equal(ResultadoInstalacion.Fallo, r);
    }

    [Fact]
    public void ElMensajeDeFaltaEmpiezaIgualParaQuienCompilaYParaQuienDescargo()
    {
        Assert.StartsWith("Falta el navegador de Playwright. Instálalo con:", InstaladorNavegador.MensajeFalta);
        Assert.Contains("playwright.ps1 install chromium", InstaladorNavegador.MensajeFalta);
        Assert.Contains("ábrelo de nuevo", InstaladorNavegador.MensajeFalta);
    }

    // ── La carpeta de trabajo ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(@"C:\Programas\HistorialAcademico\HistorialAcademico.Web.exe", true)]
    [InlineData("/opt/ha/HistorialAcademico.Web", true)]
    [InlineData(@"C:\Program Files\dotnet\dotnet.exe", false)]           // dotnet run
    [InlineData(@"D:\herramientas\testhost.exe", false)]                 // las pruebas
    [InlineData("", false)]
    [InlineData(null, false)]
    public void SoloElEjecutablePublicadoVuelveASuCarpeta(string? proceso, bool cambia)
    {
        string? nueva = null;

        var cambio = Arranque.AjustarCarpetaDeTrabajo(proceso, "/programa", c => nueva = c);

        Assert.Equal(cambia, cambio);
        Assert.Equal(cambia ? "/programa" : null, nueva);
    }

    // ── El puerto ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ElPuertoPreferidoSeUsaSiEstaLibre() => Assert.Equal(5296, Arranque.PuertoLibre(estaLibre: _ => true));

    [Fact]
    public void SiElPuertoEstaOcupadoSeElijeElSiguienteLibre()
    {
        var ocupados = new HashSet<int> { 5296, 5297 };

        Assert.Equal(5298, Arranque.PuertoLibre(estaLibre: p => !ocupados.Contains(p)));
    }

    [Fact]
    public void ConUnPuertoOcupadoDeVerdadNoSeDevuelveEse()
    {
        using var ocupado = new TcpListener(IPAddress.Loopback, 0);
        ocupado.Start();
        var puerto = ((IPEndPoint)ocupado.LocalEndpoint).Port;

        var elegido = Arranque.PuertoLibre(puerto);

        Assert.NotEqual(puerto, elegido);
    }

    [Fact]
    public void SiNoHayNingunPuertoLibreElMensajeEstaEnEspanol()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Arranque.PuertoLibre(5296, 3, _ => false));

        Assert.Contains("No encontré un puerto libre entre el 5296 y el 5298", ex.Message);
    }

    // ── La dirección que se abre ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("http://localhost:5296", "http://localhost:5296/")]
    [InlineData("http://0.0.0.0:5000", "http://localhost:5000/")]
    [InlineData("http://[::]:5000", "http://localhost:5000/")]
    [InlineData("http://*:5000", "http://localhost:5000/")]
    [InlineData("http://+:5000", "http://localhost:5000/")]
    [InlineData("http://127.0.0.1:5297", "http://127.0.0.1:5297/")]
    public void LaDireccionQueSeAbreEsLocal(string escucha, string esperada) =>
        Assert.Equal(esperada, Arranque.UrlParaAbrir(new[] { escucha }));

    [Fact]
    public void ConHttpsYHttpSeAbreHttp() =>
        Assert.Equal("http://localhost:5296/", Arranque.UrlParaAbrir(new[] { "https://localhost:7282", "http://localhost:5296" }));

    [Fact]
    public void SinDireccionesValidasNoHayNadaQueAbrir()
    {
        Assert.Null(Arranque.UrlParaAbrir(Array.Empty<string>()));
        Assert.Null(Arranque.UrlParaAbrir(new[] { "ftp://localhost:21", "no es una dirección" }));
    }

    [Fact]
    public void AbrirElNavegadorUsaLaShellConLaDireccionYDiceSiPudo()
    {
        System.Diagnostics.ProcessStartInfo? recibido = null;

        var ok = Arranque.AbrirEnNavegador("http://localhost:5296/", info => recibido = info);

        Assert.True(ok);
        Assert.Equal("http://localhost:5296/", recibido!.FileName);
        Assert.True(recibido.UseShellExecute);
        Assert.False(Arranque.AbrirEnNavegador("http://localhost:5296/", _ => throw new InvalidOperationException("sin navegador")));
    }

    // ── La configuración del programa publicado ───────────────────────────────────────────

    private static JsonElement Configuracion(string archivo) =>
        JsonDocument.Parse(File.ReadAllText(Ruta("HistorialAcademico.Web", archivo))).RootElement;

    [Fact]
    public void SoloElProgramaPublicadoEnciendeLasTresCosasDelPrimerArranque()
    {
        var produccion = Configuracion("appsettings.Production.json").GetProperty("Inicio");

        Assert.True(produccion.GetProperty("PuertoLibre").GetBoolean());
        Assert.True(produccion.GetProperty("InstalarNavegador").GetBoolean());
        Assert.True(produccion.GetProperty("AbrirNavegador").GetBoolean());
        Assert.Equal(5296, produccion.GetProperty("Puerto").GetInt32());
        Assert.False(Configuracion("appsettings.json").TryGetProperty("Inicio", out _));   // desarrollo y pruebas: apagado
    }

    [Fact]
    public void ElProgramaPublicadoLlevaLosPensumsLaLicenciaYElLeeme()
    {
        var proyecto = File.ReadAllText(Ruta("HistorialAcademico.Web", "HistorialAcademico.Web.csproj"));

        Assert.Contains(@"..\pensums\**\*.json", proyecto);
        Assert.Contains(@"personal-*.json", proyecto);                 // los pénsums personales de quien publica no viajan
        Assert.Contains(@"..\LICENSE;..\LEEME.txt", proyecto);
        Assert.True(File.Exists(Ruta("LICENSE")));
        Assert.True(File.Exists(Ruta("LEEME.txt")));
        var leeme = File.ReadAllText(Ruta("LEEME.txt"));
        Assert.Contains("no esta afiliado ni avalado por ninguna universidad", leeme);
        Assert.Contains("5296", leeme);
    }

    // ── Los flujos de GitHub ──────────────────────────────────────────────────────────────

    private static string Flujo(string nombre) => File.ReadAllText(Ruta(".github", "workflows", nombre)).Replace("\r\n", "\n");

    [Fact]
    public void ElFlujoDeIntegracionCorreEnCadaPullRequestAMainYSoloLee()
    {
        var ci = Flujo("ci.yml");

        Assert.Contains("on:\n  pull_request:\n    branches: [main]", ci);
        Assert.Contains("permissions:\n  contents: read", ci);
        Assert.Contains("dotnet build --configuration Release -warnaserror", ci);
        Assert.Contains("dotnet test --configuration Release --no-build", ci);
        Assert.Contains("-- validar pensums", ci);
        Assert.Contains("playwright.ps1 install chromium", ci);
        Assert.DoesNotContain("pull_request_target", ci);
        Assert.DoesNotContain("secrets.", ci);
    }

    [Fact]
    public void ElFlujoDeReleaseSoloCorreConEtiquetasVYSoloPublicaConElPermisoJusto()
    {
        var release = Flujo("release.yml");

        Assert.Contains("on:\n  push:\n    tags:\n      - 'v*'", release);
        Assert.DoesNotContain("pull_request", release);                 // el código de un fork nunca publica
        Assert.DoesNotContain("secrets.", release);
        Assert.Equal(1, release.Split("contents: write").Length - 1);   // un solo trabajo puede escribir: el que crea el Release
        Assert.StartsWith("name: Publicar versión\n", release);
        Assert.Contains("permissions:\n  contents: read", release);
        Assert.Contains("needs: probar", release);                       // no se arma nada sin pasar las pruebas
        Assert.Contains("needs: [armar, instalador]", release);          // el Release espera al programa de cada sistema y al instalador
        Assert.Contains("GH_TOKEN: ${{ github.token }}", release);
        Assert.Contains("--self-contained true", release);
        Assert.Contains("-p:Version=", release);
        Assert.Contains("SHA256SUMS", release);
        foreach (var rid in new[] { "win-x64", "linux-x64", "osx-arm64", "osx-x64" }) Assert.Contains(rid, release);
        Assert.Contains("--prerelease", release);                        // v1.2.3-beta.1 no es la versión estable
    }

    [Fact]
    public void ElFlujoDeReleaseTambienSePuedeLanzarAManoYAgregaArchivosAUnReleaseQueYaExiste()
    {
        var release = Flujo("release.yml");

        Assert.Contains("workflow_dispatch:\n    inputs:\n      etiqueta:", release);
        Assert.Contains("ETIQUETA: ${{ github.event_name == 'workflow_dispatch' && inputs.etiqueta || github.ref_name }}", release);
        Assert.DoesNotContain("$GITHUB_REF_NAME", release);              // todo usa la etiqueta elegida, no el nombre de la rama de un lanzamiento manual
        Assert.Contains("gh release view \"$ETIQUETA\"", release);
        Assert.Contains("gh release upload \"$ETIQUETA\" entrega/* --clobber", release);   // un Release creado a mano en la web no se queda sin archivos
        Assert.Contains("gh release create \"$ETIQUETA\"", release);
        Assert.Matches(@"\^v\[0-9\]\+", release);                        // la etiqueta escrita a mano también se valida
    }

    [Fact]
    public void ElFlujoDeReleaseArmaElInstaladorDeWindowsConInnoSetup()
    {
        var release = Flujo("release.yml");

        Assert.Contains("instalador:\n    name: Armar el instalador de Windows\n    needs: probar\n    runs-on: windows-latest", release);
        Assert.Contains("choco install innosetup", release);
        Assert.Contains("installer\\HistorialAcademico.iss", release);
        Assert.Contains("/DAppVersion=", release);
        Assert.Contains("/DOrigenPrograma=", release);
        Assert.Contains("programa-instalador-win-x64", release);          // lo recoge el patrón programa-* del último trabajo
        Assert.Contains("HistorialAcademico-Instalador-*.exe", release);
        Assert.True(File.Exists(Ruta("installer", "HistorialAcademico.iss")));
    }

    [Fact]
    public void ElScriptDelInstaladorInstalaSinAdministradorSinTocarLosDatosYEnEspanol()
    {
        var iss = File.ReadAllText(Ruta("installer", "HistorialAcademico.iss"));

        Assert.Contains("PrivilegesRequired=lowest", iss);                         // sin pedir contraseña de administrador
        Assert.Contains(@"DefaultDirName={localappdata}\Programs\HistorialAcademico", iss);
        Assert.Contains("AppId={{", iss);                                          // siempre el mismo: una versión nueva se instala encima
        Assert.Contains("OutputBaseFilename=HistorialAcademico-Instalador-v{#AppVersion}", iss);
        Assert.Contains("compiler:Languages\\Spanish.isl", iss);
        Assert.Contains(@"{#Ejecutable}", iss);
        Assert.Contains("#define Ejecutable \"HistorialAcademico.Web.exe\"", iss);
        Assert.Contains("recursesubdirs", iss);                                    // lleva wwwroot, pensums y el resto de la carpeta publicada
        Assert.Contains("postinstall", iss);                                       // ofrece abrir la aplicación al terminar
        Assert.Contains("Tasks: escritorio", iss);
        Assert.Contains("CloseApplications=yes", iss);                             // una actualización cierra la aplicación si está abierta
        Assert.DoesNotContain("[UninstallDelete]", iss);                           // desinstalar no borra los datos de la persona
        Assert.DoesNotContain("LOCALAPPDATA%\\HistorialAcademico\"", iss);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, File.ReadAllBytes(Ruta("installer", "HistorialAcademico.iss")).Take(3).ToArray());   // Inno Setup lee UTF-8 solo con BOM
    }

    [Fact]
    public void ElNombreDelEjecutableDelInstaladorCoincideConElDelProyecto()
    {
        // Si se renombra el proyecto, el instalador (acceso directo y «Abrir ahora») dejaría de funcionar.
        Assert.Equal("HistorialAcademico.Web", typeof(HistorialAcademico.Web.Helpers.Arranque).Assembly.GetName().Name);
        Assert.Contains("HistorialAcademico.Web.exe", File.ReadAllText(Ruta("installer", "HistorialAcademico.iss")));
    }

    [Theory]
    [InlineData("ci.yml")]
    [InlineData("release.yml")]
    public void LosFlujosSonYamlSinTabuladoresYSoloUsanAccionesOficiales(string archivo)
    {
        var flujo = Flujo(archivo);

        Assert.DoesNotContain('\t', flujo);
        foreach (var linea in flujo.Split('\n').Where(l => l.Contains("uses:")))
            Assert.Matches(@"uses: actions/[a-z-]+@v\d+", linea);         // nada de terceros: menos superficie de ataque
    }

    [Fact]
    public void TodoLoQueLosFlujosNombranExiste()
    {
        foreach (var proyecto in new[] { "HistorialAcademico.Validador", "HistorialAcademico.Web", "HistorialAcademico.Banner" })
            Assert.True(Directory.Exists(Ruta(proyecto)), proyecto);
        Assert.True(Directory.Exists(Ruta("pensums")));
    }

    // ── El nombre del ejecutable y la versión ─────────────────────────────────────────────

    [Fact]
    public void ElEjecutableSeLlamaComoLoDicenElLeemeYElAjusteDeCarpeta()
    {
        // El ajuste de carpeta y el LEEME dependen de este nombre: si cambia el proyecto, hay que cambiarlos.
        Assert.Equal("HistorialAcademico.Web", typeof(Arranque).Assembly.GetName().Name);
        Assert.Contains("HistorialAcademico.Web.exe", File.ReadAllText(Ruta("LEEME.txt")));
        Assert.Contains("HistorialAcademico.Web.exe", File.ReadAllText(Ruta("README.md")));
    }
}
