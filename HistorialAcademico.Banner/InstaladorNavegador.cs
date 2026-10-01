using Microsoft.Playwright;

namespace HistorialAcademico.Banner;

public enum ResultadoInstalacion { YaEstaba, Instalado, Fallo }

/// <summary>
/// El navegador (Chromium de Playwright) que la aplicación usa para que la persona inicie sesión en Banner y para exportar el plan a PDF o imagen.
/// El programa descargable no lo trae: la primera vez que arranca lo instala (unos 150 MB) y va mostrando el avance en la ventana.
/// </summary>
public sealed class InstaladorNavegador
{
    /// <summary>Cómo instalarlo a mano si el arranque no pudo hacerlo.</summary>
    public const string InstalarAMano = "pwsh HistorialAcademico.Banner/bin/Debug/net8.0/playwright.ps1 install chromium";

    /// <summary>El mensaje que se muestra cuando falta el navegador. Empieza igual para quien compiló el código y para quien descargó el programa.</summary>
    public const string MensajeFalta =
        $"Falta el navegador de Playwright. Instálalo con: {InstalarAMano} (si usas el programa descargado, ciérralo y ábrelo de nuevo: lo instala solo).";

    private readonly Func<bool> _estaInstalado;
    private readonly Func<string[], int> _instalar;

    /// <param name="estaInstalado">Solo para pruebas; por omisión se pregunta a Playwright dónde debería estar Chromium.</param>
    /// <param name="instalar">Solo para pruebas; por omisión se usa el instalador de Playwright, que dibuja su propia barra de avance.</param>
    public InstaladorNavegador(Func<bool>? estaInstalado = null, Func<string[], int>? instalar = null)
    {
        _estaInstalado = estaInstalado ?? ChromiumInstalado;
        _instalar = instalar ?? Microsoft.Playwright.Program.Main;
    }

    public static bool ChromiumInstalado()
    {
        try
        {
            using var playwright = Playwright.CreateAsync().GetAwaiter().GetResult();
            var ruta = playwright.Chromium.ExecutablePath;
            return !string.IsNullOrEmpty(ruta) && File.Exists(ruta);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Si Chromium ya está, no hace nada; si falta, lo instala escribiendo en <paramref name="salida"/> qué pasa. Nunca lanza: un fallo se cuenta y la aplicación arranca igual.</summary>
    public ResultadoInstalacion AsegurarChromium(TextWriter salida)
    {
        if (_estaInstalado()) return ResultadoInstalacion.YaEstaba;

        salida.WriteLine("Primera vez: falta el navegador que usa la aplicación (Chromium, unos 150 MB).");
        salida.WriteLine("Lo estoy descargando; puede tardar unos minutos según tu conexión. No cierres esta ventana.");
        salida.Flush();
        try
        {
            var codigo = _instalar(new[] { "install", "chromium" });
            if (codigo == 0 && _estaInstalado())
            {
                salida.WriteLine("Navegador instalado.");
                return ResultadoInstalacion.Instalado;
            }
            salida.WriteLine($"No se pudo instalar el navegador (código {codigo}). La aplicación funciona, pero conectar Banner y exportar el plan no hasta que se instale.");
        }
        catch (Exception ex)
        {
            salida.WriteLine($"No se pudo instalar el navegador: {ex.Message.Split('\n')[0].Trim()}. La aplicación funciona, pero conectar Banner y exportar el plan no hasta que se instale.");
        }
        salida.WriteLine("Revisa tu conexión y vuelve a abrir el programa: lo intentará de nuevo.");
        return ResultadoInstalacion.Fallo;
    }
}
