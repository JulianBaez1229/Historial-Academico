using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using HistorialAcademico.Core.Models;

namespace HistorialAcademico.Banner;

public class HistoricoParseException : Exception
{
    public HistoricoParseException(string mensaje) : base(mensaje) { }
}

/// <summary>
/// Convierte el HTML de bwskotrn.P_ViewTran (Histórico Académico de Banner 8) en un <see cref="HistoricoBanner"/>.
/// Todo el histórico vive en una única table.datadisplaytable sin ids: cada fila se reconoce por el tipo de
/// celda (th.ddtitle / th.ddlabel / th.ddheader / td.dddefault) y por su texto.
/// </summary>
public static class HistoricoParser
{
    private enum Seccion { Ninguna, Alumno, GradoAObtener, Periodos, TotalesPeriodo, TotalesGlobales, EnProgreso }

    private static readonly Regex PeriodoRx = new(@"^Periodo:\s*(?<nombre>.+?)\s+(?<nivel>\S+)$", RegexOptions.Compiled);
    private static readonly Regex FechaRx = new(@"^(?<mes>\p{L}{3})\.?\s+(?<dia>\d{1,2}),\s*(?<anio>\d{4})$", RegexOptions.Compiled);

    private static readonly Regex NombreRx = new(@"var\s+userDetails\s*=\s*""(?<n>[^""]*)""", RegexOptions.Compiled);

    private static readonly string[] Meses = { "ENE", "FEB", "MAR", "ABR", "MAY", "JUN", "JUL", "AGO", "SEP", "OCT", "NOV", "DIC" };

    public static HistoricoBanner Parse(string html)
    {
        var doc = new HtmlParser().ParseDocument(html);
        if (doc.QuerySelector("table.datadisplaytable") is not IHtmlTableElement tabla)
            throw new HistoricoParseException("No se encontró la tabla del histórico (table.datadisplaytable). ¿Cambió el formato de Banner?");

        var resultado = new HistoricoBanner();
        var seccion = Seccion.Ninguna;
        PeriodoHistorico? periodo = null;
        PeriodoEnProgreso? enProgreso = null;
        Dictionary<string, int>? columnas = null;
        var estadoUltimo = (string?)null;

        // Rows solo devuelve las filas propias de la tabla, no las de tablas anidadas ("Histórico Académico No Oficial").
        foreach (var fila in tabla.Rows)
        {
            var celdas = fila.Cells.ToList();
            if (celdas.Count == 0) continue;
            var primera = celdas[0];
            var textoPrimera = Texto(primera);

            // ── Título de sección ─────────────────────────────────────────────────────
            if (celdas.Count == 1 && primera.LocalName == "th" && primera.ClassList.Contains("ddtitle"))
            {
                var t = Normalizar(textoPrimera);
                if (t.StartsWith("INFORMACION DEL ALUMNO")) seccion = Seccion.Alumno;
                else if (t.StartsWith("GRADO A OBTENER")) seccion = Seccion.GradoAObtener;
                else if (t.StartsWith("CREDITO DE INSTITUCION")) seccion = Seccion.Periodos;
                else if (t.StartsWith("TOTALES PERIODO")) seccion = Seccion.TotalesPeriodo;
                else if (t.StartsWith("TOTALES DE HISTORICO")) seccion = Seccion.TotalesGlobales;
                else if (t.StartsWith("CURSOS EN PROGRESO")) seccion = Seccion.EnProgreso;
                // "Información de Currículum" y similares no cambian de sección.
                continue;
            }

            // ── Encabezado de período ─────────────────────────────────────────────────
            if (primera.ClassList.Contains("ddlabel") && textoPrimera.StartsWith("Periodo:", StringComparison.OrdinalIgnoreCase))
            {
                var m = PeriodoRx.Match(textoPrimera);
                if (!m.Success) throw new HistoricoParseException($"Encabezado de período no reconocido: \"{textoPrimera}\".");
                columnas = null;
                if (seccion == Seccion.EnProgreso)
                {
                    enProgreso = new PeriodoEnProgreso { Nombre = m.Groups["nombre"].Value, Nivel = m.Groups["nivel"].Value };
                    resultado.EnProgreso.Add(enProgreso);
                    periodo = null;
                }
                else
                {
                    seccion = Seccion.Periodos;
                    periodo = new PeriodoHistorico { Nombre = m.Groups["nombre"].Value, Nivel = m.Groups["nivel"].Value };
                    resultado.Periodos.Add(periodo);
                    enProgreso = null;
                }
                continue;
            }

            // ── Encabezado de columnas de materias ────────────────────────────────────
            // Se mapea por nombre de columna porque los cursos en progreso tienen otras columnas.
            if (primera.ClassList.Contains("ddheader") && Normalizar(textoPrimera) == "MATERIA")
            {
                columnas = new Dictionary<string, int>();
                for (var i = 0; i < celdas.Count; i++) columnas[Normalizar(Texto(celdas[i]))] = i;
                continue;
            }

            // ── Fila de totales: etiqueta + 6 cifras (Intentadas … PGA) ───────────────
            if (primera.LocalName == "th" && primera.ClassList.Contains("ddlabel") && celdas.Count == 7 && celdas.Skip(1).All(EsCeldaDato))
            {
                var totales = LeerTotales(celdas);
                switch (Normalizar(textoPrimera).TrimEnd(':'))
                {
                    case "INSTITUCION" when seccion == Seccion.GradoAObtener: resultado.ResumenSuperior = totales; break;
                    case "PERIODO ACTUAL" when periodo is not null: periodo.TotalesPeriodo = totales; break;
                    case "ACUMULATIVO" when periodo is not null: periodo.TotalesAcumulados = totales; break;
                    case "TOTAL INSTITUCION": resultado.TotalInstitucion = totales; break;
                    case "TOTAL TRANSFERIDO": resultado.TotalTransferido = totales; break;
                    case "GLOBAL": resultado.TotalGlobal = totales; break;
                }
                continue;
            }

            // ── Fila de datos de una materia ──────────────────────────────────────────
            if (columnas is not null && (seccion is Seccion.Periodos or Seccion.EnProgreso) &&
                primera.LocalName == "td" && celdas.Count == columnas.Count)
            {
                string Col(string nombre) => columnas.TryGetValue(nombre, out var i) ? Texto(celdas[i]) : "";

                if (enProgreso is not null)
                {
                    enProgreso.Cursos.Add(new CursoEnProgresoHistorico
                    {
                        Materia = Col("MATERIA"), Curso = Col("CURSO"), Campus = Col("CAMPUS"), Nivel = Col("NIVEL"),
                        Titulo = Col("TITULO"), HorasCredito = Numero(Col("HORAS CREDITO"), "Horas Crédito"),
                    });
                }
                else if (periodo is not null)
                {
                    periodo.Materias.Add(new MateriaHistorico
                    {
                        Materia = Col("MATERIA"), Curso = Col("CURSO"), Campus = Col("CAMPUS"), Nivel = Col("NIVEL"),
                        Titulo = Col("TITULO"), Calificacion = Col("CALIFICACION"),
                        HorasCredito = Numero(Col("HORAS CREDITO"), "Horas Crédito"),
                        PuntosCalidad = Numero(Col("PUNTOS DE CALIDAD"), "Puntos de Calidad"),
                    });
                }
                continue;
            }

            // ── Etiqueta + valor (datos del alumno y datos del período) ───────────────
            if (primera.LocalName == "th" && primera.ClassList.Contains("ddlabel") && celdas.Count >= 2 &&
                celdas[1].ClassList.Contains("dddefault"))
            {
                var etiqueta = Normalizar(textoPrimera).TrimEnd(':');
                var valor = Texto(celdas[1]);
                if (valor.Length == 0) continue;

                if (seccion == Seccion.Periodos && periodo is not null)
                {
                    switch (etiqueta)
                    {
                        case "ESCUELA": periodo.Escuela = valor; break;
                        case "CARRERA": periodo.Carrera = valor; break;
                        case "TIPO DE ALUMNO": periodo.TipoAlumno = valor; break;
                        case "ESTADO ACADEMICO": periodo.EstadoAcademico = valor; break;
                        case "ULTIMO ESTADO ACADEMICO": estadoUltimo = valor; break;
                    }
                }
                else if (seccion == Seccion.Alumno)
                {
                    // "Información de Currículum" (programa actual) va dentro de esta sección.
                    var a = resultado.Alumno;
                    switch (etiqueta)
                    {
                        case "FECHA DE NACIMIENTO": a.FechaNacimiento = Fecha(valor); break;
                        case "TIPO DE ALUMNO": a.TipoAlumno = valor; break;
                        case "PROGRAMA": a.Programa ??= valor; break;
                        case "ESCUELA": a.Escuela ??= valor; break;
                        case "CAMPUS": a.Campus ??= valor; break;
                        case "CARRERA": a.Carrera ??= valor; break;
                    }
                }
                else if (seccion == Seccion.GradoAObtener && etiqueta == "BUSCANDO GRADUARSE")
                {
                    resultado.Alumno.GradoAObtener = valor;
                }
            }
        }

        // El nombre no está en la tabla sino en la cabecera: var userDetails = "NOMBRE COMPLETO";
        var nombre = NombreRx.Match(html);
        if (nombre.Success && nombre.Groups["n"].Value.Trim().Length > 0)
            resultado.Alumno.Nombre = nombre.Groups["n"].Value.Trim();

        // Estado académico del alumno: el "último" que publica Banner, o el del último período cerrado.
        resultado.Alumno.EstadoAcademico = estadoUltimo ?? resultado.Periodos.LastOrDefault()?.EstadoAcademico;

        Validar(resultado);
        return resultado;
    }

    private static void Validar(HistoricoBanner h)
    {
        if (h.Periodos.Count == 0)
            throw new HistoricoParseException("El histórico no contiene ningún período cerrado.");
        if (h.TotalGlobal is null)
            throw new HistoricoParseException("No se encontró la fila \"Global\" de totales del histórico.");
        var sinTotales = h.Periodos.FirstOrDefault(p => p.TotalesPeriodo is null || p.TotalesAcumulados is null);
        if (sinTotales is not null)
            throw new HistoricoParseException($"Al período {sinTotales.Nombre} le faltan sus totales (Periodo Actual / Acumulativo).");
    }

    private static bool EsCeldaDato(IHtmlTableCellElement c) => c.LocalName == "td" && c.ClassList.Contains("dddefault");

    private static TotalesBanner LeerTotales(List<IHtmlTableCellElement> celdas)
    {
        decimal N(int i) => Numero(Texto(celdas[i]), "totales");
        return new TotalesBanner(N(1), N(2), N(3), N(4), N(5), N(6));
    }

    private static decimal Numero(string texto, string columna)
    {
        if (decimal.TryParse(texto, NumberStyles.Number, CultureInfo.InvariantCulture, out var n)) return n;
        throw new HistoricoParseException($"No pude leer \"{texto}\" como número en {columna}.");
    }

    private static DateOnly? Fecha(string texto)
    {
        var m = FechaRx.Match(texto);
        if (!m.Success) return null;
        var mes = Array.IndexOf(Meses, Normalizar(m.Groups["mes"].Value)) + 1;
        return mes == 0 ? null : new DateOnly(int.Parse(m.Groups["anio"].Value), mes, int.Parse(m.Groups["dia"].Value));
    }

    private static string Texto(IElement e) => Regex.Replace(e.TextContent.Replace(' ', ' '), @"\s+", " ").Trim();

    /// <summary>Mayúsculas y sin acentos, para comparar etiquetas sin depender de tildes.</summary>
    private static string Normalizar(string s)
    {
        var d = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var c in d)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString().Normalize(NormalizationForm.FormC).ToUpperInvariant().Trim();
    }
}
