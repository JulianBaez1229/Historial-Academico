using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Html;

namespace HistorialAcademico.Web.Helpers;

public record SegmentoDona(string Clase, decimal Valor, string Etiqueta);

public record SerieLinea(string Clase, string Nombre, IReadOnlyList<decimal?> Valores, bool Cuadrados = false, bool MostrarValores = false);

/// <summary>
/// Gráficos SVG generados en el servidor: sin librerías, sin CDN y sin JavaScript. Los colores salen de clases CSS
/// (definidas con los tokens de tema.css), no del código. Cada gráfico es un <c>role="img"</c> con descripción; la vista
/// añade además una tabla con los mismos datos para lectores de pantalla.
/// </summary>
public static class Graficos
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private static string N(double v) => v.ToString("0.##", Inv);
    private static string T(string s) => WebUtility.HtmlEncode(s);

    // ── Dona ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Dona de progreso: cada segmento ocupa Valor/Total de la circunferencia. El texto central lo pone la vista con
    /// <paramref name="centro"/> (puede llevar data-contar para la animación de conteo).
    /// </summary>
    public static IHtmlContent Dona(IReadOnlyList<SegmentoDona> segmentos, decimal total, string descripcion, string centro = "")
    {
        const double radio = 48, ancho = 14, cx = 60;
        var circunferencia = 2 * Math.PI * radio;
        var sb = new StringBuilder();
        sb.Append($"<svg class=\"dona\" viewBox=\"0 0 120 120\" role=\"img\" aria-label=\"{T(descripcion)}\">");
        sb.Append($"<title>{T(descripcion)}</title>");
        sb.Append($"<g transform=\"rotate(-90 {cx} {cx})\" fill=\"none\" stroke-width=\"{ancho}\">");
        sb.Append($"<circle class=\"dona-pista\" cx=\"{cx}\" cy=\"{cx}\" r=\"{radio}\"/>");

        double acumulado = 0;
        foreach (var s in segmentos.Where(s => s.Valor > 0 && total > 0))
        {
            var largo = Math.Min((double)(s.Valor / total), 1) * circunferencia;
            sb.Append($"<circle class=\"dona-segmento {T(s.Clase)}\" cx=\"{cx}\" cy=\"{cx}\" r=\"{radio}\" " +
                      $"stroke-dasharray=\"{N(largo)} {N(circunferencia - largo)}\" stroke-dashoffset=\"{N(-acumulado)}\">" +
                      $"<title>{T(s.Etiqueta)}</title></circle>");
            acumulado += largo;
        }
        sb.Append("</g>");
        if (centro.Length > 0) sb.Append($"<text class=\"dona-centro\" x=\"{cx}\" y=\"{cx}\" text-anchor=\"middle\" dominant-baseline=\"central\">{centro}</text>");
        sb.Append("</svg>");
        return new HtmlString(sb.ToString());
    }

    // ── Líneas ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Gráfico de líneas con eje Y fijo de 0 a <paramref name="maximoY"/> y una etiqueta por punto del eje X.
    /// Las series se distinguen por color, por tipo de línea (continua o discontinua) y por marcador (círculo o cuadrado).
    /// </summary>
    public static IHtmlContent Lineas(IReadOnlyList<string> etiquetasX, IReadOnlyList<SerieLinea> series, decimal maximoY, string descripcion)
    {
        const double ancho = 520, alto = 250, izq = 34, der = 16, arriba = 16, abajo = 46;
        var w = ancho - izq - der;
        var h = alto - arriba - abajo;
        var n = etiquetasX.Count;
        var max = (double)maximoY;

        // Los puntos de los extremos quedan a 24 px del eje para que sus etiquetas no toquen los números del eje Y.
        const double margenX = 24;
        double X(int i) => n <= 1 ? izq + w / 2 : izq + margenX + (w - 2 * margenX) * i / (n - 1);
        double Y(decimal v) => arriba + h * (1 - Math.Clamp((double)v / max, 0, 1));

        var sb = new StringBuilder();
        sb.Append($"<svg class=\"lineas\" viewBox=\"0 0 {N(ancho)} {N(alto)}\" role=\"img\" aria-label=\"{T(descripcion)}\">");
        sb.Append($"<title>{T(descripcion)}</title>");

        // Cuadrícula y eje Y (0, 1, 2, 3, 4…).
        for (var v = 0; v <= (int)maximoY; v++)
        {
            var y = Y(v);
            sb.Append($"<line class=\"lineas-rejilla\" x1=\"{N(izq)}\" y1=\"{N(y)}\" x2=\"{N(ancho - der)}\" y2=\"{N(y)}\"/>");
            sb.Append($"<text class=\"lineas-eje\" x=\"{N(izq - 6)}\" y=\"{N(y)}\" text-anchor=\"end\" dominant-baseline=\"central\">{v}</text>");
        }

        // Eje X: nombre del período en dos líneas ("MAY-AGO" / "2024").
        for (var i = 0; i < n; i++)
        {
            var partes = etiquetasX[i].Split(' ', 2);
            sb.Append($"<text class=\"lineas-eje\" x=\"{N(X(i))}\" y=\"{N(alto - abajo + 16)}\" text-anchor=\"middle\">" +
                      $"<tspan x=\"{N(X(i))}\">{T(partes[0])}</tspan>" +
                      (partes.Length > 1 ? $"<tspan x=\"{N(X(i))}\" dy=\"12\">{T(partes[1])}</tspan>" : "") + "</text>");
        }

        foreach (var serie in series)
        {
            var puntos = serie.Valores.Select((v, i) => (v, i)).Where(p => p.v is not null).ToList();
            if (puntos.Count == 0) continue;

            if (puntos.Count > 1)
                sb.Append($"<polyline class=\"linea {T(serie.Clase)}\" fill=\"none\" points=\"" +
                          string.Join(" ", puntos.Select(p => $"{N(X(p.i))},{N(Y(p.v!.Value))}")) + "\"/>");

            foreach (var (v, i) in puntos)
            {
                var x = X(i);
                var y = Y(v!.Value);
                var tip = $"<title>{T(etiquetasX[i])}: {T(serie.Nombre)} {v.Value.ToString("0.00", Inv)}</title>";
                sb.Append(serie.Cuadrados
                    ? $"<rect class=\"marcador {T(serie.Clase)}\" x=\"{N(x - 4)}\" y=\"{N(y - 4)}\" width=\"8\" height=\"8\">{tip}</rect>"
                    : $"<circle class=\"marcador {T(serie.Clase)}\" cx=\"{N(x)}\" cy=\"{N(y)}\" r=\"4.5\">{tip}</circle>");
                if (serie.MostrarValores)
                    sb.Append($"<text class=\"lineas-valor\" x=\"{N(x)}\" y=\"{N(y - 9)}\" text-anchor=\"middle\">{v.Value.ToString("0.00", Inv)}</text>");
            }
        }
        sb.Append("</svg>");
        return new HtmlString(sb.ToString());
    }
}
