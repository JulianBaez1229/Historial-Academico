using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using HistorialAcademico.Banner;
using HistorialAcademico.Core.Entities;
using HistorialAcademico.Core.Horarios;
using HistorialAcademico.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using Xunit;

namespace HistorialAcademico.Tests;

/// <summary>Servidor HTTP mínimo en localhost para reproducir páginas lentas o que se cuelgan.</summary>
public sealed class ServidorLento : IDisposable
{
    private readonly TcpListener _escucha = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _fin = new();
    private readonly bool _mudo;

    /// <param name="mudo">Si es true, nunca responde a nada. Si no, sirve una página que incluye una imagen que nunca termina de cargar.</param>
    public ServidorLento(bool mudo)
    {
        _mudo = mudo;
        _escucha.Start();
        _ = Task.Run(AceptarAsync);
    }

    public string Url => $"http://127.0.0.1:{((IPEndPoint)_escucha.LocalEndpoint).Port}/StudentSelfService/ssb/studentCommonDashboard";

    private async Task AceptarAsync()
    {
        try
        {
            while (!_fin.IsCancellationRequested)
            {
                var cliente = await _escucha.AcceptTcpClientAsync(_fin.Token);
                _ = Task.Run(() => AtenderAsync(cliente));
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
    }

    private async Task AtenderAsync(TcpClient cliente)
    {
        try
        {
            using var _ = cliente;
            var flujo = cliente.GetStream();
            var buffer = new byte[4096];
            var n = await flujo.ReadAsync(buffer, _fin.Token);
            var linea = Encoding.ASCII.GetString(buffer, 0, n).Split('\n')[0];

            // El servidor mudo y el recurso "colgado" dejan la conexión abierta sin contestar.
            if (_mudo || linea.Contains("/colgado")) { await Task.Delay(Timeout.Infinite, _fin.Token); return; }

            var cuerpo = "<html><body><h1>Panel</h1><img src=\"/colgado.png\"></body></html>";
            var cabecera = $"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {Encoding.UTF8.GetByteCount(cuerpo)}\r\nConnection: close\r\n\r\n";
            await flujo.WriteAsync(Encoding.UTF8.GetBytes(cabecera + cuerpo), _fin.Token);
        }
        catch (Exception) { /* el cliente cerró o el servidor se está apagando */ }
    }

    public void Dispose() { _fin.Cancel(); _escucha.Stop(); }
}

/// <summary>Usa el Chromium real de Playwright; el fallo original era un TimeoutException al esperar el evento "load".</summary>
public class BannerClientNavegacionTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "banner-test-" + Guid.NewGuid().ToString("N"));

    public BannerClientNavegacionTests()
    {
        Directory.CreateDirectory(Path.Combine(_dir, ".auth"));
        File.WriteAllText(Path.Combine(_dir, ".auth", "banner.json"), "{\"cookies\":[],\"origins\":[]}");
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private BannerClient Cliente(string url, int segundos) =>
        new(new BannerOptions { BaseUrl = url, CarpetaDatos = _dir, TiempoNavegacionSegundos = segundos });

    [Fact]
    public async Task UnaPaginaConUnRecursoQueNuncaTerminaNoHaceFallarPorElEventoLoad()
    {
        using var servidor = new ServidorLento(mudo: false);
        var reloj = Stopwatch.StartNew();

        // 127.0.0.1 no es un host de Banner, así que al abrir el panel se interpreta como sesión caducada.
        // Lo importante es que responde de inmediato: antes esperaba el "load" 30 s y lanzaba TimeoutException.
        await Assert.ThrowsAsync<BannerSesionExpiradaException>(() => Cliente(servidor.Url, segundos: 60).CapturarHistoricoAsync());

        // Incluye lanzar Chromium y hasta 15 s de espera de red tranquila; bajo carga (suite completa) puede pasar de 25 s, por eso la cota es holgada.
        Assert.True(reloj.Elapsed < TimeSpan.FromSeconds(45), $"Tardó {reloj.Elapsed.TotalSeconds:0} s: siguió esperando el evento load.");
    }

    [Fact]
    public async Task UnServidorQueNoResponderDaUnErrorLegibleEnVezDeUnaExcepcionDePlaywright()
    {
        using var servidor = new ServidorLento(mudo: true);

        var ex = await Assert.ThrowsAsync<BannerException>(() => Cliente(servidor.Url, segundos: 3).CapturarHistoricoAsync());

        Assert.IsNotType<BannerSesionExpiradaException>(ex);   // no es una sesión caducada: Banner no contestó
        Assert.Contains("no respondió a tiempo", ex.Message);
        Assert.Contains("3 segundos", ex.Message);
        Assert.DoesNotContain("Call log", ex.Message);         // sin la pila de Playwright
    }

    [Fact]
    public async Task SinSesionGuardadaPideLoginSinAbrirElNavegador()
    {
        var vacio = Path.Combine(Path.GetTempPath(), "banner-sin-sesion-" + Guid.NewGuid().ToString("N"));
        var cliente = new BannerClient(new BannerOptions { BaseUrl = "http://127.0.0.1:1/", CarpetaDatos = vacio });
        await Assert.ThrowsAsync<BannerSesionExpiradaException>(() => cliente.CapturarHistoricoAsync());
    }
}

/// <summary>Cliente de Banner falso para probar cómo reacciona la sincronización a cualquier fallo.</summary>
public class BannerFalso : BannerClient
{
    public Func<Task<string>> Captura { get; set; } = () => throw new BannerSesionExpiradaException();
    public Func<Task> Login { get; set; } = () => Task.CompletedTask;
    public int Capturas { get; private set; }
    public int Logins { get; private set; }

    /// <summary>Respuesta de ConsultarSeccionesAsync; por defecto no hay secciones publicadas.</summary>
    public Func<string, IReadOnlyList<ConsultaBanner>, Task<ResultadoBusqueda>> Secciones { get; set; } = (_, _) => Task.FromResult(new ResultadoBusqueda());
    public List<(string Periodo, string Consultas)> ConsultasSecciones { get; } = new();

    public BannerFalso() : base(new BannerOptions()) { }

    /// <summary>Cada llamada a ConsultarLoteAsync: el período y cuántas materias trajo.</summary>
    public List<(string Periodo, int Materias)> Lotes { get; } = new();

    /// <summary>Hace lo mismo que el cliente real: una materia que falla (BannerException) se anota y se sigue; la sesión caducada corta todo.</summary>
    public override async Task ConsultarLoteAsync(string periodo, IReadOnlyList<LoteConsulta> lote, Func<LoteConsulta, ResultadoLote, Task> alTerminar, CancellationToken ct = default)
    {
        Lotes.Add((periodo, lote.Count));
        foreach (var item in lote)
        {
            ct.ThrowIfCancellationRequested();
            ConsultasSecciones.Add((periodo, string.Join(",", item.Consultas.Select(c => c.Codigo))));
            ResultadoLote r;
            try { r = new ResultadoLote(await Secciones(periodo, item.Consultas), null); }
            catch (BannerSesionExpiradaException) { throw; }
            catch (BannerException ex) { r = new ResultadoLote(null, ex.Message); }
            await alTerminar(item, r);
        }
    }

    public override Task<ResultadoBusqueda> ConsultarSeccionesAsync(string periodo, IReadOnlyList<ConsultaBanner> consultas, CancellationToken ct = default)
    {
        ConsultasSecciones.Add((periodo, string.Join(",", consultas.Select(c => c.Codigo))));
        return Secciones(periodo, consultas);
    }

    public override bool TieneSesionGuardada => true;
    public override Task<string> CapturarHistoricoAsync(bool visible = false, CancellationToken ct = default) { Capturas++; return Captura(); }
    public override Task IniciarSesionAsync(CancellationToken ct = default) { Logins++; return Login(); }
}

public class SincronizacionRobustezTests
{
    private static (BdPrueba bd, BannerFalso banner, SincronizacionService sync) Preparar()
    {
        var bd = new BdPrueba();
        var banner = new BannerFalso();
        return (bd, banner, new SincronizacionService(bd.Db, banner));
    }

    [Theory]
    [InlineData(typeof(System.TimeoutException))]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(InvalidOperationException))]
    [InlineData(typeof(PlaywrightException))]
    public async Task CualquierExcepcionInesperadaSeRegistraYNoRompeLaPantalla(Type tipo)
    {
        var (bd, banner, sync) = Preparar();
        using var _ = bd;
        banner.Captura = () => throw (Exception)Activator.CreateInstance(tipo, "falló algo")!;

        var r = await sync.ActualizarAsync(permitirLogin: true);   // no debe lanzar

        Assert.False(r.Exito);
        Assert.False(r.RequiereLogin);
        Assert.Contains("Error inesperado", r.Mensaje);
        Assert.Contains("falló algo", r.Mensaje);
        var fila = await bd.Db.Sincronizaciones.SingleAsync();
        Assert.Equal(ResultadoSincronizacion.Error, fila.Resultado);
        Assert.Equal(r.Mensaje, fila.Mensaje);
    }

    [Fact]
    public async Task UnErrorDeBannerConservaSuMensajeLegible()
    {
        var (bd, banner, sync) = Preparar();
        using var _ = bd;
        banner.Captura = () => throw new BannerException("Banner no respondió a tiempo al abrir el panel de Banner (más de 60 segundos).");

        var r = await sync.ActualizarAsync(permitirLogin: true);

        Assert.False(r.Exito);
        Assert.Contains("no respondió a tiempo", r.Mensaje);
        Assert.Equal(0, banner.Logins);   // un tiempo agotado no es una sesión caducada: no se abre el login
    }

    [Fact]
    public async Task SiElLoginFallaSeRegistraUnSoloErrorConSuMotivo()
    {
        var (bd, banner, sync) = Preparar();
        using var _ = bd;
        banner.Login = () => throw new IOException("no se pudo abrir el navegador");

        var r = await sync.ActualizarAsync(permitirLogin: true);

        Assert.False(r.Exito);
        Assert.Contains("No se pudo iniciar sesión en Banner", r.Mensaje);
        Assert.Contains("no se pudo abrir el navegador", r.Mensaje);
        Assert.Single(await bd.Db.Sincronizaciones.ToListAsync());   // el intento con sesión caducada no suma un error más
        Assert.Equal(1, banner.Capturas);                            // y no se reintenta la captura
    }

    [Fact]
    public async Task SesionCaducadaMasLoginMasCapturaEsUnUnicoExito()
    {
        var (bd, banner, sync) = Preparar();
        using var _ = bd;
        var archivo = Path.Combine(Path.GetTempPath(), "historico-" + Guid.NewGuid().ToString("N") + ".html");
        await File.WriteAllTextAsync(archivo, Muestras.LeerSintetico());
        try
        {
            banner.Captura = () => banner.Capturas == 1 ? throw new BannerSesionExpiradaException() : Task.FromResult(archivo);

            var r = await sync.ActualizarAsync(permitirLogin: true);

            Assert.True(r.Exito, r.Mensaje);
            Assert.Equal((2, 1), (banner.Capturas, banner.Logins));
            var fila = await bd.Db.Sincronizaciones.SingleAsync();     // sin fila de error intermedia
            Assert.Equal(ResultadoSincronizacion.Exito, fila.Resultado);
            Assert.Equal(2, await bd.Db.Periodos.CountAsync());
        }
        finally { File.Delete(archivo); }
    }

    [Fact]
    public async Task SinPermitirLoginUnaSesionCaducadaPideLoginYSeRegistra()
    {
        var (bd, banner, sync) = Preparar();
        using var _ = bd;

        var r = await sync.ActualizarAsync(permitirLogin: false);

        Assert.True(r.RequiereLogin);
        Assert.Equal(0, banner.Logins);
        Assert.Equal(ResultadoSincronizacion.Error, (await bd.Db.Sincronizaciones.SingleAsync()).Resultado);
    }
}
