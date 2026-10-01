using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using HistorialAcademico.Core.Actualizaciones;
using HistorialAcademico.Core.Perfiles;

namespace HistorialAcademico.Web.Services;

/// <summary>Una versión publicada más nueva que la que se está usando.</summary>
public sealed record AvisoActualizacion(string Version, string Actual, string Url);

public enum EstadoConsulta { NoConsultada, Consultando, AlDia, Nueva, Fallo }

/// <summary>
/// Avisa, con discreción, si en GitHub hay una versión más nueva del programa. Es la única conexión que la aplicación hace por su cuenta, y por eso
/// está pensada para molestar poco y poder apagarse:
/// <list type="bullet">
/// <item>Solo consulta si la persona no la desactivó (preferencia del equipo), si el programa sabe de qué repositorio salió y si es un programa publicado.</item>
/// <item>Una sola petición GET a la API pública de Releases (sin credenciales ni datos de la persona: solo la dirección IP, como cualquier página web, y el
/// nombre y la versión del programa) al abrirse, y otra si la persona pulsa «Buscar ahora». Nunca en bucle ni en segundo plano.</item>
/// <item>Espera como máximo 5 segundos, lee como máximo 256 KB y, si algo sale mal, no muestra nada.</item>
/// <item>Lo que responde GitHub se trata como dato: la dirección del aviso solo se usa si apunta a los Releases de ese mismo repositorio.</item>
/// </list>
/// </summary>
public sealed partial class ActualizacionesService : IDisposable
{
    public const string ApiPorOmision = "https://api.github.com";
    private const int MaxBytes = 256 * 1024;
    private static readonly TimeSpan EsperaMaxima = TimeSpan.FromSeconds(5);

    [GeneratedRegex(@"^[A-Za-z0-9](?:[A-Za-z0-9_.-]{0,98}[A-Za-z0-9_])?/[A-Za-z0-9_.-]{1,100}$")]
    private static partial Regex FormatoRepositorio();

    private readonly AlmacenPreferencias _preferencias;
    private readonly HttpClient _http;
    private readonly string _api;
    private readonly bool _activo;
    private readonly SemaphoreSlim _uno = new(1, 1);
    private volatile AvisoActualizacion? _aviso;
    private volatile EstadoConsulta _estado = EstadoConsulta.NoConsultada;
    private volatile string? _error;

    /// <param name="configuracion">«Actualizaciones:Activas» (solo el programa publicado la enciende) y «Actualizaciones:Repositorio» (usuario/repositorio; si falta se usa el que trae el programa).</param>
    /// <param name="manejador">Solo para pruebas: así no se sale a internet.</param>
    /// <param name="versionActual">Solo para pruebas; por omisión la versión del programa.</param>
    /// <param name="repositorioIncorporado">Solo para pruebas; por omisión el que le puso el flujo de Release al armar el programa.</param>
    public ActualizacionesService(IConfiguration configuracion, AlmacenPreferencias preferencias, HttpMessageHandler? manejador = null,
        string? versionActual = null, string? repositorioIncorporado = null, string api = ApiPorOmision)
    {
        _preferencias = preferencias;
        _api = api.TrimEnd('/');
        Actual = versionActual ?? VersionDelPrograma();
        var repositorio = configuracion["Actualizaciones:Repositorio"] is { Length: > 0 } r ? r : repositorioIncorporado ?? RepositorioIncorporado();
        Repositorio = repositorio is not null && FormatoRepositorio().IsMatch(repositorio.Trim()) ? repositorio.Trim() : null;
        _activo = configuracion.GetValue<bool>("Actualizaciones:Activas");
        _http = new HttpClient(manejador ?? new SocketsHttpHandler { AllowAutoRedirect = false }, disposeHandler: manejador is null) { Timeout = EsperaMaxima };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"HistorialAcademico/{Regex.Replace(Actual, @"[^A-Za-z0-9._+-]", "-")}");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    /// <summary>La versión que está corriendo (la de la etiqueta del Release, o 0.0.0-desarrollo si se compiló desde el código).</summary>
    public string Actual { get; }

    /// <summary>usuario/repositorio de donde salió el programa; null si no se sabe (compilado desde el código sin indicarlo).</summary>
    public string? Repositorio { get; }

    /// <summary>¿Este programa puede consultar? Hace falta que sea uno publicado y que se sepa el repositorio.</summary>
    public bool Disponible => _activo && Repositorio is not null && VersionApp.TryParse(Actual, out var v) && !v.EsDeDesarrollo;

    /// <summary>¿Consulta al abrirse? Es la preferencia de la persona, sobre lo anterior.</summary>
    public bool Habilitada => Disponible && _preferencias.Cargar().AvisarActualizaciones;

    public EstadoConsulta Estado => _estado;
    public string? UltimoError => _error;

    /// <summary>El aviso que hay que mostrar ahora: una versión más nueva que no se haya ignorado, y solo si los avisos siguen encendidos.</summary>
    public AvisoActualizacion? Aviso
    {
        get
        {
            var aviso = _aviso;
            if (aviso is null || !Habilitada) return null;
            return string.Equals(_preferencias.Cargar().VersionIgnorada, aviso.Version, StringComparison.OrdinalIgnoreCase) ? null : aviso;
        }
    }

    /// <summary>Consulta una vez, si corresponde. No lanza nunca: cualquier problema deja «Fallo» y ningún aviso.</summary>
    public async Task ConsultarAsync(bool forzar = false, CancellationToken ct = default)
    {
        if (!Habilitada) return;
        if (!await _uno.WaitAsync(0, ct)) return;   // ya hay una consulta en marcha
        try
        {
            if (_estado is not EstadoConsulta.NoConsultada and not EstadoConsulta.Fallo && !forzar) return;
            _estado = EstadoConsulta.Consultando;
            _error = null;
            using var pedido = new HttpRequestMessage(HttpMethod.Get, $"{_api}/repos/{Repositorio}/releases/latest");
            using var respuesta = await _http.SendAsync(pedido, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!respuesta.IsSuccessStatusCode)
            {
                Fallar(respuesta.StatusCode == System.Net.HttpStatusCode.NotFound
                    ? "GitHub no tiene ninguna versión publicada de este programa todavía."
                    : $"GitHub respondió {(int)respuesta.StatusCode}.");
                return;
            }
            var cuerpo = await LeerLimitadoAsync(respuesta, ct);
            using var json = JsonDocument.Parse(cuerpo);
            var raiz = json.RootElement;
            var etiqueta = raiz.TryGetProperty("tag_name", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
            var borrador = raiz.TryGetProperty("draft", out var d) && d.ValueKind == JsonValueKind.True;
            var prueba = raiz.TryGetProperty("prerelease", out var p) && p.ValueKind == JsonValueKind.True;
            if (borrador || prueba || !VersionApp.TryParse(etiqueta, out var publicada)) { Ahora(EstadoConsulta.AlDia, null); return; }

            if (!VersionApp.HayNueva(Actual, publicada.ToString())) { Ahora(EstadoConsulta.AlDia, null); return; }
            var url = raiz.TryGetProperty("html_url", out var h) && h.ValueKind == JsonValueKind.String ? h.GetString() : null;
            var seguro = $"https://github.com/{Repositorio}/releases";
            var destino = url is not null && url.StartsWith(seguro + "/", StringComparison.OrdinalIgnoreCase) && Uri.IsWellFormedUriString(url, UriKind.Absolute)
                ? url : $"{seguro}/latest";
            Ahora(EstadoConsulta.Nueva, new AvisoActualizacion(publicada.ToString(), Actual, destino));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException or JsonException or InvalidOperationException or IOException)
        {
            Fallar(ex is TaskCanceledException ? "GitHub no respondió a tiempo." : "No pude consultar GitHub (¿sin internet?).");
        }
        finally { _uno.Release(); }
    }

    private void Ahora(EstadoConsulta estado, AvisoActualizacion? aviso) { _aviso = aviso; _estado = estado; }
    private void Fallar(string motivo) { _aviso = null; _error = motivo; _estado = EstadoConsulta.Fallo; }

    private static async Task<string> LeerLimitadoAsync(HttpResponseMessage respuesta, CancellationToken ct)
    {
        if (respuesta.Content.Headers.ContentLength is > MaxBytes) throw new InvalidOperationException("Respuesta demasiado grande.");
        await using var flujo = await respuesta.Content.ReadAsStreamAsync(ct);
        using var memoria = new MemoryStream();
        var buffer = new byte[8192];
        int leidos;
        while ((leidos = await flujo.ReadAsync(buffer, ct)) > 0)
        {
            memoria.Write(buffer, 0, leidos);
            if (memoria.Length > MaxBytes) throw new InvalidOperationException("Respuesta demasiado grande.");
        }
        return System.Text.Encoding.UTF8.GetString(memoria.ToArray());
    }

    // ── Preferencias ──────────────────────────────────────────────────────────────────────

    /// <summary>Enciende o apaga los avisos. Al apagarlos se olvida lo que ya se sabía: no queda ningún aviso ni se vuelve a consultar.</summary>
    public bool Avisar(bool si)
    {
        var guardado = _preferencias.Cambiar(p => p.AvisarActualizaciones = si);
        if (guardado && !si) { _aviso = null; _estado = EstadoConsulta.NoConsultada; _error = null; }
        return guardado;
    }

    public bool Ignorar(string version) => _preferencias.Cambiar(p => p.VersionIgnorada = version);

    // ── Versión del programa ──────────────────────────────────────────────────────────────

    public static string VersionDelPrograma()
    {
        var informativa = typeof(ActualizacionesService).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
        var mas = informativa.IndexOf('+');
        return mas >= 0 ? informativa[..mas] : informativa;
    }

    /// <summary>El repositorio que el flujo de Release escribió en el programa al armarlo (-p:RepositorioGitHub=usuario/repositorio).</summary>
    public static string? RepositorioIncorporado() =>
        typeof(ActualizacionesService).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "RepositorioGitHub")?.Value is { Length: > 0 } valor ? valor : null;

    public void Dispose() { _http.Dispose(); _uno.Dispose(); }
}
