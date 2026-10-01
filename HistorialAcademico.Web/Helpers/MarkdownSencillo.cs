using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace HistorialAcademico.Web.Helpers;

/// <summary>
/// Convierte a HTML el Markdown sencillo de las guías del proyecto: títulos, párrafos, listas con viñetas o numeradas (con un nivel de
/// viñetas anidadas y bloques de código dentro de un elemento), bloques de código, tablas, y en el texto **negrita**, `código` y
/// [enlaces](https://…). Todo el texto se codifica antes de generar el HTML, así que un archivo .md no puede meter etiquetas ni
/// scripts; los enlaces solo pueden ser http(s), internos (#…) o relativos.
/// </summary>
public static class MarkdownSencillo
{
    private static readonly Regex Titulo = new(@"^(#{1,4})\s+(.*)$", RegexOptions.Compiled);
    private static readonly Regex Vineta = new(@"^\s*[-*]\s+(.*)$", RegexOptions.Compiled);
    private static readonly Regex Numerada = new(@"^\s*\d+[.)]\s+(.*)$", RegexOptions.Compiled);
    private static readonly Regex Separador = new(@"^\s*\|?\s*:?-{3,}:?\s*(\|\s*:?-{3,}:?\s*)*\|?\s*$", RegexOptions.Compiled);
    private static readonly Regex Codigo = new("`([^`]+)`", RegexOptions.Compiled);
    private static readonly Regex Negrita = new(@"\*\*(.+?)\*\*", RegexOptions.Compiled);
    private static readonly Regex Enlace = new(@"\[([^\]]+)\]\(([^)\s]+)\)", RegexOptions.Compiled);

    /// <summary>Los títulos bajan un nivel (# → h2) porque la página que muestra la guía ya tiene su propio h1.</summary>
    public static string AHtml(string? markdown)
    {
        var html = new StringBuilder();
        var lineas = (markdown ?? "").Replace("\r\n", "\n").Split('\n');
        var i = 0;

        while (i < lineas.Length)
        {
            var linea = lineas[i];

            if (linea.TrimStart().StartsWith("```")) { html.Append(BloqueDeCodigo(lineas, ref i, 0)).AppendLine(); }
            else if (string.IsNullOrWhiteSpace(linea)) i++;
            else if (Titulo.Match(linea) is { Success: true } t)
            {
                var nivel = Math.Min(t.Groups[1].Length + 1, 6);
                html.Append($"<h{nivel}>").Append(Inline(t.Groups[2].Value.Trim())).AppendLine($"</h{nivel}>");
                i++;
            }
            else if (EsTabla(lineas, i)) i = Tabla(lineas, i, html);
            else if (Vineta.IsMatch(linea)) i = Lista(lineas, i, html, "ul", Vineta);
            else if (Numerada.IsMatch(linea)) i = Lista(lineas, i, html, "ol", Numerada);
            else
            {
                var parrafo = new List<string>();
                while (i < lineas.Length && !string.IsNullOrWhiteSpace(lineas[i]) && !EsInicioDeBloque(lineas, i)) parrafo.Add(lineas[i++].Trim());
                html.Append("<p>").Append(Inline(string.Join(" ", parrafo))).AppendLine("</p>");
            }
        }
        return html.ToString();
    }

    /// <summary>
    /// Escapa lo que el navegador interpretaría (& &lt; &gt; " '). Las letras con tilde y la ñ se dejan tal cual: el HTML se sirve en UTF-8,
    /// y así no se convierten en entidades numéricas ilegibles. Vale para texto y para atributos entre comillas dobles.
    /// </summary>
    private static string Escapar(string texto) =>
        texto.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&#39;");

    private static int Sangria(string linea) => linea.Length - linea.TrimStart().Length;

    private static bool EsTabla(string[] lineas, int i) =>
        lineas[i].TrimStart().StartsWith('|') && i + 1 < lineas.Length && Separador.IsMatch(lineas[i + 1]);

    private static bool EsInicioDeBloque(string[] lineas, int i) =>
        lineas[i].TrimStart().StartsWith("```") || Titulo.IsMatch(lineas[i]) || Vineta.IsMatch(lineas[i]) || Numerada.IsMatch(lineas[i]) || EsTabla(lineas, i);

    /// <summary>Un bloque entre ``` y ```; las líneas pierden hasta <paramref name="sangria"/> espacios de sangría (los de su lista).</summary>
    private static string BloqueDeCodigo(string[] lineas, ref int i, int sangria)
    {
        var codigo = new List<string>();
        i++;   // la línea de apertura
        while (i < lineas.Length && !lineas[i].TrimStart().StartsWith("```"))
        {
            var l = lineas[i++];
            codigo.Add(l[Math.Min(sangria, Sangria(l))..]);
        }
        i++;   // el cierre
        return "<pre class=\"guia-codigo\"><code>" + Escapar(string.Join("\n", codigo)) + "</code></pre>";
    }

    private static int Lista(string[] lineas, int i, StringBuilder html, string etiqueta, Regex patron)
    {
        html.AppendLine($"<{etiqueta}>");
        while (i < lineas.Length && Sangria(lineas[i]) < 2 && patron.Match(lineas[i]) is { Success: true } m)
        {
            var texto = m.Groups[1].Value.Trim();
            var extra = new StringBuilder();
            var anidados = new List<string>();
            i++;

            // Lo que sigue con sangría (o separado por líneas en blanco de algo con sangría) pertenece a este elemento.
            while (i < lineas.Length && (Sangria(lineas[i]) >= 2 || (string.IsNullOrWhiteSpace(lineas[i]) && i + 1 < lineas.Length && Sangria(lineas[i + 1]) >= 2 && !string.IsNullOrWhiteSpace(lineas[i + 1]))))
            {
                var l = lineas[i];
                if (string.IsNullOrWhiteSpace(l)) i++;
                else if (l.TrimStart().StartsWith("```")) extra.Append(BloqueDeCodigo(lineas, ref i, Sangria(l)));
                else if (Vineta.Match(l) is { Success: true } sub) { anidados.Add(sub.Groups[1].Value.Trim()); i++; }
                else { texto += " " + l.Trim(); i++; }
            }

            html.Append("<li>").Append(Inline(texto));
            if (anidados.Count > 0) html.Append("<ul>").Append(string.Concat(anidados.Select(a => "<li>" + Inline(a) + "</li>"))).Append("</ul>");
            html.Append(extra).AppendLine("</li>");

            // Una línea en blanco entre elementos de la misma lista no la corta.
            var j = i;
            while (j < lineas.Length && string.IsNullOrWhiteSpace(lineas[j])) j++;
            if (j > i && j < lineas.Length && Sangria(lineas[j]) < 2 && patron.IsMatch(lineas[j])) i = j;
        }
        html.AppendLine($"</{etiqueta}>");
        return i;
    }

    private static int Tabla(string[] lineas, int i, StringBuilder html)
    {
        html.AppendLine("<div class=\"table-responsive\"><table class=\"table table-sm guia-tabla\">");
        html.Append("<thead><tr>");
        foreach (var c in Celdas(lineas[i])) html.Append("<th>").Append(Inline(c)).Append("</th>");
        html.AppendLine("</tr></thead><tbody>");
        i += 2;
        while (i < lineas.Length && lineas[i].TrimStart().StartsWith('|'))
        {
            html.Append("<tr>");
            foreach (var c in Celdas(lineas[i])) html.Append("<td>").Append(Inline(c)).Append("</td>");
            html.AppendLine("</tr>");
            i++;
        }
        html.AppendLine("</tbody></table></div>");
        return i;
    }

    /// <summary>Las celdas de una fila «| a | b |»; un `|` dentro de un código entre comillas invertidas no separa.</summary>
    private static List<string> Celdas(string fila)
    {
        var celdas = new List<string>();
        var actual = new StringBuilder();
        var enCodigo = false;
        foreach (var ch in fila.Trim())
        {
            if (ch == '`') enCodigo = !enCodigo;
            if (ch == '|' && !enCodigo) { celdas.Add(actual.ToString().Trim()); actual.Clear(); }
            else actual.Append(ch);
        }
        celdas.Add(actual.ToString().Trim());
        if (celdas.Count > 0 && celdas[0].Length == 0) celdas.RemoveAt(0);
        if (celdas.Count > 0 && celdas[^1].Length == 0) celdas.RemoveAt(celdas.Count - 1);
        return celdas;
    }

    /// <summary>Negrita, código y enlaces dentro de una línea. El código se aparta primero para que no se procese lo que lleva dentro.</summary>
    private static string Inline(string texto)
    {
        var resultado = new StringBuilder();
        var pos = 0;
        foreach (Match m in Codigo.Matches(texto))
        {
            resultado.Append(FueraDeCodigo(texto[pos..m.Index]));
            resultado.Append("<code>").Append(Escapar(m.Groups[1].Value)).Append("</code>");
            pos = m.Index + m.Length;
        }
        resultado.Append(FueraDeCodigo(texto[pos..]));
        return resultado.ToString();
    }

    private static string FueraDeCodigo(string texto)
    {
        var seguro = Escapar(texto);
        seguro = Enlace.Replace(seguro, m => EnlaceSeguro(m.Groups[2].Value)
            ? $"<a href=\"{m.Groups[2].Value}\"{(m.Groups[2].Value.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? " target=\"_blank\" rel=\"noopener noreferrer\"" : "")}>{m.Groups[1].Value}</a>"
            : m.Groups[1].Value);
        return Negrita.Replace(seguro, "<strong>$1</strong>");
    }

    /// <summary>Solo enlaces http(s), internos (#…) o relativos (sin esquema como javascript:).</summary>
    private static bool EnlaceSeguro(string url)
    {
        var u = WebUtility.HtmlDecode(url).Trim();
        if (u.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || u.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return true;
        if (u.StartsWith('#')) return true;
        return !u.Contains(':') && !u.StartsWith("//");
    }
}
