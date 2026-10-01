using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace HistorialAcademico.Web.Helpers;

/// <summary>
/// Lo que hace distinto al programa descargado de «dotnet run»: encuentra su carpeta aunque lo abran desde un acceso directo,
/// elige un puerto libre y abre el navegador en la dirección local. Todo está apagado salvo que <c>appsettings.Production.json</c>
/// (que solo viaja en el programa publicado) lo encienda, así que las pruebas y el desarrollo no lo notan.
/// </summary>
public static class Arranque
{
    public const int PuertoPorOmision = 5296;

    /// <summary>
    /// Si el proceso es el ejecutable publicado (HistorialAcademico.Web.exe y no «dotnet» ni el host de pruebas), la carpeta de trabajo pasa a ser
    /// la del programa: de ahí salen wwwroot y pensums aunque el acceso directo lo abra desde otra carpeta.
    /// </summary>
    /// <returns>True si cambió la carpeta.</returns>
    public static bool AjustarCarpetaDeTrabajo(string? rutaDelProceso, string carpetaDelPrograma, Action<string> cambiarCarpeta)
    {
        if (string.IsNullOrEmpty(rutaDelProceso)) return false;
        // Ojo: en macOS y Linux el ejecutable se llama «HistorialAcademico.Web» a secas; «.Web» no es una extensión que haya que quitar.
        var nombre = rutaDelProceso.Replace('\\', '/');
        nombre = nombre[(nombre.LastIndexOf('/') + 1)..];
        if (nombre.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) nombre = nombre[..^4];
        if (!string.Equals(nombre, "HistorialAcademico.Web", StringComparison.OrdinalIgnoreCase)) return false;
        cambiarCarpeta(carpetaDelPrograma);
        return true;
    }

    /// <summary>El primer puerto libre desde <paramref name="preferido"/> (el 5296 de siempre si se puede).</summary>
    public static int PuertoLibre(int preferido = PuertoPorOmision, int intentos = 20, Func<int, bool>? estaLibre = null)
    {
        estaLibre ??= PuertoDisponible;
        for (var puerto = preferido; puerto < preferido + intentos; puerto++)
            if (estaLibre(puerto)) return puerto;
        throw new InvalidOperationException($"No encontré un puerto libre entre el {preferido} y el {preferido + intentos - 1}. Cierra otros programas e inténtalo de nuevo.");
    }

    private static bool PuertoDisponible(int puerto)
    {
        try
        {
            using var escucha = new TcpListener(IPAddress.Loopback, puerto);
            escucha.Start();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    /// <summary>De las direcciones en las que escucha el servidor, la que hay que abrir en el navegador (siempre http y en localhost).</summary>
    public static string? UrlParaAbrir(IEnumerable<string> direcciones)
    {
        foreach (var d in direcciones.OrderBy(d => d.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ? 0 : 1))
        {
            if (!Uri.TryCreate(d.Replace("*", "localhost").Replace("+", "localhost"), UriKind.Absolute, out var uri)) continue;
            if (uri.Scheme is not ("http" or "https")) continue;
            var host = uri.Host is "0.0.0.0" or "::" or "[::]" ? "localhost" : uri.Host;
            return new UriBuilder(uri) { Host = host }.Uri.GetLeftPart(UriPartial.Path);
        }
        return null;
    }

    /// <summary>Abre la dirección en el navegador de la persona. Devuelve false si el sistema no pudo (no es grave: la dirección se muestra en la ventana).</summary>
    public static bool AbrirEnNavegador(string url, Action<ProcessStartInfo>? lanzar = null)
    {
        try
        {
            var inicio = new ProcessStartInfo(url) { UseShellExecute = true };
            if (lanzar is not null) lanzar(inicio);
            else Process.Start(inicio)?.Dispose();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
