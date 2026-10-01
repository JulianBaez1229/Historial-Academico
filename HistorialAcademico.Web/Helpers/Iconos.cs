using Microsoft.AspNetCore.Html;

namespace HistorialAcademico.Web.Helpers;

/// <summary>
/// Íconos SVG de trazo simple, incluidos en la página (sin librerías ni CDN externos). Heredan el color del texto
/// (<c>currentColor</c>), así que cambian solos con el tema. Son decorativos: van con aria-hidden y el texto los acompaña.
/// </summary>
public static class Iconos
{
    private static readonly Dictionary<string, string> Trazos = new(StringComparer.OrdinalIgnoreCase)
    {
        ["casa"] = "M3 11l9-8 9 8M5 10v10h5v-6h4v6h5V10",
        ["usuario"] = "M12 12a4 4 0 100-8 4 4 0 000 8zM4 21c0-4 4-6 8-6s8 2 8 6",
        ["libro"] = "M4 4h11a3 3 0 013 3v13H7a3 3 0 01-3-3V4zM4 17a3 3 0 013-3h11",
        ["lista"] = "M9 6h11M9 12h11M9 18h11M4 6h.01M4 12h.01M4 18h.01",
        ["grafico"] = "M4 20V10M10 20V4M16 20v-7M22 20H2",
        ["mapa"] = "M9 4L3 6v14l6-2 6 2 6-2V4l-6 2-6-2zM9 4v14M15 6v14",
        ["inscribir"] = "M12 21a9 9 0 100-18 9 9 0 000 18zM8 12l3 3 5-6",
        ["calendario"] = "M4 6h16v14H4zM4 10h16M8 3v4M16 3v4",
        ["archivo"] = "M6 3h8l4 4v14H6zM14 3v4h4M9 13h6M9 17h6",
        ["intercambio"] = "M4 8h14l-3-3M20 16H6l3 3",
        ["refrescar"] = "M20 12a8 8 0 10-2.3 5.7M20 5v5h-5",
        ["menu"] = "M4 6h16M4 12h16M4 18h16",

        // Interruptor de tema.
        ["sol"] = "M12 16a4 4 0 100-8 4 4 0 000 8zM12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4",
        ["luna"] = "M21 12.8A9 9 0 1111.2 3a7 7 0 009.8 9.8z",
        ["monitor"] = "M3 5h18v11H3zM8 20h8M12 16v4",

        // Estados de las materias: cada uno con su propio ícono para no depender solo del color.
        ["check"] = "M5 12.5l4.5 4.5L19 7",
        ["escudo"] = "M12 3l8 3v6c0 5-3.5 8-8 9-4.5-1-8-4-8-9V6l8-3zM9 12l2 2 4-4",
        ["reloj"] = "M12 21a9 9 0 100-18 9 9 0 000 18zM12 7v5l3 2",
        ["disponible"] = "M12 21a9 9 0 100-18 9 9 0 000 18zM8.5 12h7M12.5 8.5L16 12l-3.5 3.5",
        ["candado"] = "M7 11V8a5 5 0 0110 0v3M5 11h14v10H5z",

        // Tarjetas del dashboard.
        ["meta"] = "M12 21a9 9 0 100-18 9 9 0 000 18zM12 16a4 4 0 100-8 4 4 0 000 8zM12 12h.01",
        ["birrete"] = "M2 9l10-5 10 5-10 5L2 9zM6 11.5V16c0 1.5 3 3 6 3s6-1.5 6-3v-4.5M22 9v6",
    };

    public static bool Existe(string nombre) => Trazos.ContainsKey(nombre);

    public static IHtmlContent Svg(string nombre, string clase = "icono")
    {
        if (!Trazos.TryGetValue(nombre, out var trazo)) throw new ArgumentException($"No existe el ícono «{nombre}».", nameof(nombre));
        return new HtmlString(
            $"<svg class=\"{clase}\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.8\" " +
            $"stroke-linecap=\"round\" stroke-linejoin=\"round\" aria-hidden=\"true\" focusable=\"false\"><path d=\"{trazo}\"/></svg>");
    }
}
