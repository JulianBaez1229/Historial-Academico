using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Playwright;
using Xunit;

namespace HistorialAcademico.Tests;

/// <summary>Sirve los archivos estáticos reales (Bootstrap, tema.css, site.css) y una página de prueba con los componentes.</summary>
public sealed class ServidorEstatico : IDisposable
{
    private const string Pagina = """
        <!doctype html><html lang="es"><head><meta charset="utf-8">
        <link rel="stylesheet" href="/lib/bootstrap/dist/css/bootstrap.min.css">
        <link rel="stylesheet" href="/css/tema.css"><link rel="stylesheet" href="/css/site.css"></head>
        <body><main class="contenido">
          <a id="enlace" href="#">Un enlace</a>
          <button id="primario" class="btn btn-primary">Primario</button>
          <button id="contorno" class="btn btn-outline-primary">Contorno</button>
          <button id="peligro" class="btn btn-outline-danger">Peligro</button>
          <ul class="nav nav-pills"><li class="nav-item"><a id="pildora" class="nav-link active" href="#">Activa</a></li></ul>
          <span id="info" class="badge bg-info">Exenta</span>
          <span id="alerta" class="badge bg-warning text-dark">Disponible</span>
          <span id="exito" class="badge bg-success">Aprobada</span>
          <span id="avisoTexto" class="text-warning">Aviso</span>
          <span id="peligroTexto" class="text-danger">Error</span>
          <a id="tarjeta" class="card" href="#"><div class="card-body">Tarjeta</div></a>
          <table class="table table-hover"><tbody><tr id="fila"><td>Fila</td></tr></tbody></table>
          <div id="mapa" class="mapa-card estado-aprobada">ISO100</div>
          <div class="menu-lateral" style="position:static"><a id="menuActivo" class="menu-item activo" href="#">Activo</a><a id="menuNormal" class="menu-item" href="#">Normal</a></div>
        </main></body></html>
        """;

    private readonly TcpListener _escucha = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _fin = new();
    private readonly IReadOnlyDictionary<string, string> _paginas;

    /// <param name="paginas">Páginas extra que se sirven tal cual (ruta → HTML), por ejemplo el HTML real de la aplicación.</param>
    public ServidorEstatico(IReadOnlyDictionary<string, string>? paginas = null)
    {
        _paginas = paginas ?? new Dictionary<string, string>();
        _escucha.Start();
        _ = Task.Run(AceptarAsync);
    }

    public string Url => Direccion("/prueba.html");

    public string Direccion(string ruta) => $"http://127.0.0.1:{((IPEndPoint)_escucha.LocalEndpoint).Port}{ruta}";

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
            var buffer = new byte[8192];
            var n = await flujo.ReadAsync(buffer, _fin.Token);
            var ruta = Encoding.ASCII.GetString(buffer, 0, n).Split('\n')[0].Split(' ')[1].Split('?')[0];

            byte[] cuerpo;
            var tipo = "text/css; charset=utf-8";
            var estado = "200 OK";
            if (ruta == "/prueba.html") { cuerpo = Encoding.UTF8.GetBytes(Pagina); tipo = "text/html; charset=utf-8"; }
            else if (_paginas.TryGetValue(ruta, out var html)) { cuerpo = Encoding.UTF8.GetBytes(html); tipo = "text/html; charset=utf-8"; }
            else
            {
                var archivo = Path.Combine(Estaticos.Raiz, ruta.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(archivo)) cuerpo = await File.ReadAllBytesAsync(archivo, _fin.Token);
                else { cuerpo = Array.Empty<byte>(); estado = "404 Not Found"; }
            }

            var cabecera = $"HTTP/1.1 {estado}\r\nContent-Type: {tipo}\r\nContent-Length: {cuerpo.Length}\r\nConnection: close\r\n\r\n";
            await flujo.WriteAsync(Encoding.ASCII.GetBytes(cabecera), _fin.Token);
            await flujo.WriteAsync(cuerpo, _fin.Token);
        }
        catch (Exception) { /* el navegador cerró la conexión */ }
    }

    public void Dispose() { _fin.Cancel(); _escucha.Stop(); }
}

/// <summary>
/// Comprueba en un Chromium real, con el Bootstrap que trae el proyecto, que el tema se aplica de verdad:
/// los colores de la marca, y las animaciones de hover con y sin "reducir movimiento".
/// </summary>
public class TemaNavegadorTests : IAsyncLifetime
{
    private ServidorEstatico _servidor = null!;
    private IPlaywright _pw = null!;
    private IBrowser _navegador = null!;

    public async Task InitializeAsync()
    {
        _servidor = new ServidorEstatico();
        _pw = await Playwright.CreateAsync();
        _navegador = await _pw.Chromium.LaunchAsync(new() { Headless = true });
    }

    public async Task DisposeAsync()
    {
        await _navegador.DisposeAsync();
        _pw.Dispose();
        _servidor.Dispose();
    }

    private async Task<IPage> AbrirAsync(ReducedMotion movimiento)
    {
        var contexto = await _navegador.NewContextAsync(new() { ReducedMotion = movimiento, ViewportSize = new() { Width = 1200, Height = 800 } });
        var pagina = await contexto.NewPageAsync();
        await pagina.GotoAsync(_servidor.Url);
        return pagina;
    }

    private static Task<string> Estilo(IPage p, string selector, string propiedad) =>
        p.EvaluateAsync<string>("([s, prop]) => getComputedStyle(document.querySelector(s))[prop]", new object[] { selector, propiedad });

    [Fact]
    public async Task LosColoresDeLaMarcaSeAplicanSobreBootstrap()
    {
        var p = await AbrirAsync(ReducedMotion.NoPreference);

        Assert.Equal("rgb(27, 42, 126)", await Estilo(p, "#primario", "backgroundColor"));     // azul marino, no el azul de Bootstrap
        Assert.Equal("rgb(255, 255, 255)", await Estilo(p, "#primario", "color"));
        Assert.Equal("rgb(27, 42, 126)", await Estilo(p, "#contorno", "color"));
        Assert.Equal("rgb(208, 32, 46)", await Estilo(p, "#peligro", "color"));               // rojo de la paleta
        Assert.Equal("rgb(27, 42, 126)", await Estilo(p, "#enlace", "color"));
        Assert.Equal("rgb(27, 42, 126)", await Estilo(p, "#pildora", "backgroundColor"));
        Assert.Equal("rgb(58, 110, 165)", await Estilo(p, "#info", "backgroundColor"));       // azul acero
        Assert.Equal("rgb(255, 255, 255)", await Estilo(p, "#info", "color"));
        Assert.Equal("rgb(196, 162, 58)", await Estilo(p, "#alerta", "backgroundColor"));      // dorado
        Assert.Equal("rgb(27, 42, 126)", await Estilo(p, "#alerta", "color"));                 // texto azul marino sobre dorado
        Assert.Equal("rgb(20, 108, 67)", await Estilo(p, "#exito", "backgroundColor"));
        Assert.Equal("rgb(122, 95, 12)", await Estilo(p, "#avisoTexto", "color"));             // dorado oscuro, legible como texto
        Assert.Equal("rgb(208, 32, 46)", await Estilo(p, "#peligroTexto", "color"));
    }

    [Fact]
    public async Task ElMenuResaltaLaSeccionActivaEnDorado()
    {
        var p = await AbrirAsync(ReducedMotion.NoPreference);

        Assert.Equal("rgb(196, 162, 58)", await Estilo(p, "#menuActivo", "backgroundColor"));
        Assert.Equal("rgb(27, 42, 126)", await Estilo(p, "#menuActivo", "color"));
        Assert.Equal("rgba(0, 0, 0, 0)", await Estilo(p, "#menuNormal", "backgroundColor"));
        Assert.Equal("rgb(27, 42, 126)", await Estilo(p, ".menu-lateral", "backgroundColor"));
    }

    [Fact]
    public async Task ElBotonCambiaDeColorYCreceUnPocoAlPasarElCursor()
    {
        var p = await AbrirAsync(ReducedMotion.NoPreference);

        Assert.Equal("0.2s", await Estilo(p, "#primario", "transitionDuration").ContinueWith(t => t.Result.Split(',')[0].Trim()));
        await p.HoverAsync("#primario");
        await p.WaitForTimeoutAsync(350);   // la transición dura 200 ms

        Assert.Equal("rgb(19, 30, 92)", await Estilo(p, "#primario", "backgroundColor"));   // azul marino más oscuro
        Assert.Equal("matrix(1.03, 0, 0, 1.03, 0, 0)", await Estilo(p, "#primario", "transform"));
    }

    [Fact]
    public async Task LaTarjetaYLaFilaSeElevanAlPasarElCursor()
    {
        var p = await AbrirAsync(ReducedMotion.NoPreference);

        var antes = await Estilo(p, "#tarjeta", "boxShadow");
        await p.HoverAsync("#tarjeta");
        await p.WaitForTimeoutAsync(350);
        Assert.Equal("matrix(1, 0, 0, 1, 0, -2)", await Estilo(p, "#tarjeta", "transform"));    // sube 2 px
        Assert.NotEqual(antes, await Estilo(p, "#tarjeta", "boxShadow"));                       // y cambia la sombra

        await p.HoverAsync("#fila");
        await p.WaitForTimeoutAsync(350);
        Assert.Contains("196, 162, 58", await Estilo(p, "#fila", "boxShadow"));                 // indicador dorado a la izquierda
    }

    [Fact]
    public async Task ConReducirMovimientoNoHayTransicionesNiEscalas()
    {
        var p = await AbrirAsync(ReducedMotion.Reduce);

        Assert.Equal("0s", await Estilo(p, "#primario", "transitionDuration"));
        Assert.Equal("0s", await Estilo(p, "#tarjeta", "transitionDuration"));
        await p.HoverAsync("#primario");
        await p.WaitForTimeoutAsync(300);
        Assert.Equal("none", await Estilo(p, "#primario", "transform"));    // sin escala
        await p.HoverAsync("#tarjeta");
        await p.WaitForTimeoutAsync(300);
        Assert.Equal("none", await Estilo(p, "#tarjeta", "transform"));     // sin elevación
    }
}
