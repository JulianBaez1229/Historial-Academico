using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using HistorialAcademico.Core;
using HistorialAcademico.Core.Indice;
using HistorialAcademico.Core.Models;
using HistorialAcademico.Core.Universidad;

namespace HistorialAcademico.Banner;

/// <summary>
/// Convierte un Histórico Académico real de Banner en una muestra que se puede subir al repositorio: el nombre, la matrícula, la fecha de
/// nacimiento, el programa, la escuela y el campus pasan a ser ficticios; las fechas de los períodos se corren a otro año; y las calificaciones
/// se sortean de nuevo (con una semilla fija: el mismo archivo da siempre el mismo resultado). Los puntos de calidad y todos los totales se
/// recalculan, así que la muestra sigue siendo un histórico coherente que el parser acepta. Se conservan los códigos, títulos y créditos de las
/// materias: son del plan de estudios, que es público.
/// El resultado siempre se vuelve a leer con <see cref="HistoricoParser"/> y se valida con <see cref="ValidadorHistorico"/>; si no cuadra, no se entrega.
/// </summary>
public static partial class AnonimizadorHistorico
{
    public const int AnioInicialPorOmision = 2020, SemillaPorOmision = 20240501;

    private const string Falso = "PRUEBA";
    private static readonly EscalaCalificaciones Escala = EscalaCalificaciones.Unapec;

    // Reparto de las notas que aprueban: sobre todo B y C, algunas A y muy pocas D.
    private static readonly (string Letra, int Peso)[] Notas = { ("A", 28), ("B", 42), ("C", 26), ("D", 4) };

    [GeneratedRegex(@"^(?<termino>[A-Za-z0-9]+(?:-[A-Za-z0-9]+)*)\s+(?<anio>\d{4})$")]
    private static partial Regex NombreDePeriodo();

    public static string Anonimizar(string htmlReal, int anioInicial = AnioInicialPorOmision, int semilla = SemillaPorOmision)
    {
        var real = HistoricoParser.Parse(htmlReal);
        var rng = new Random(semilla);

        var primerAnio = real.Periodos.Concat<object>(real.EnProgreso).Select(NombreDe).Select(AnioDe).Where(a => a > 0).DefaultIfEmpty(anioInicial).Min();
        var desplazamiento = anioInicial - primerAnio;

        var sb = new StringBuilder();
        Cabecera(sb);

        // Períodos cerrados con notas nuevas y totales recalculados.
        var periodos = new List<(string Nombre, string Nivel, List<(MateriaHistorico M, string Nota, decimal Puntos)> Filas, TotalesIndice Periodo, TotalesIndice Acumulado)>();
        decimal aInt = 0, aApr = 0, aPga = 0, aPun = 0;
        foreach (var p in real.Periodos)
        {
            var filas = new List<(MateriaHistorico, string, decimal)>();
            foreach (var m in p.Materias)
            {
                // Solo se sortean las notas que cuentan para el índice; la exenta, las de 0 créditos y las letras raras se dejan como están.
                // Lo aprobado sigue aprobado y lo reprobado sigue reprobado: la muestra mantiene la misma historia (qué falta, qué se puede tomar).
                var nota = m.HorasCredito == 0 || !Escala.CuentaParaIndice(m.Calificacion) ? m.Calificacion : Sortear(rng, Escala.CuentaComoAprobada(m.Calificacion));
                filas.Add((m, nota, (Escala.PuntosPorLetra(nota) ?? 0) * m.HorasCredito));
            }
            var totales = CalculadoraIndice.Totales(filas.Select(f => new Core.Entities.MateriaCursada { Codigo = f.Item1.Codigo, Calificacion = f.Item2, HorasCredito = f.Item1.HorasCredito }), Escala);
            aInt += totales.HorasIntentadas; aApr += totales.HorasAprobadas; aPga += totales.HorasPga; aPun += totales.PuntosCalidad;
            var acumulado = new TotalesIndice(aInt, aApr, aPga, aPun, aPga == 0 ? 0 : Escala.Redondear(aPun / aPga));
            periodos.Add((Desplazar(p.Nombre, desplazamiento), string.IsNullOrEmpty(p.Nivel) ? "GRADO" : p.Nivel, filas, totales, acumulado));
        }

        var global = new TotalesIndice(aInt, aApr, aPga, aPun, aPga == 0 ? 0 : Escala.Redondear(aPun / aPga));

        sb.Append("<tr><th class=\"ddtitle\" colspan=\"12\"><a name=\"acadhist\"></a>INFORMACIÓN DEL ALUMNO</th></tr>\n");
        sb.Append("<tr><th class=\"ddlabel\" colspan=\"2\" scope=\"row\">Fecha de Nacimiento:</th><td class=\"dddefault\" colspan=\"10\">Mar 05, 2001</td></tr>\n");
        sb.Append("<tr><th class=\"ddlabel\" colspan=\"2\" scope=\"row\">Tipo de Alumno:</th><td class=\"dddefault\" colspan=\"10\">ANTIGUO</td></tr>\n");
        sb.Append("<tr><th class=\"ddtitle\" colspan=\"6\">Información de Currículum</th></tr>\n");
        sb.Append("<tr><th class=\"ddlabel\" colspan=\"6\" scope=\"row\">Programa Actual</th></tr>\n<tr><td class=\"dddefault\" colspan=\"6\">LICENCIADO</td></tr>\n");
        foreach (var (etiqueta, valor) in new[] { ("Programa:", $"ADMINISTRACION DE {Falso}"), ("Escuela:", $"ESCUELA DE {Falso}"), ("Campus:", $"CAMPUS - {Falso}"), ("Carrera:", $"ADMINISTRACION DE {Falso}") })
            sb.Append($"<tr><th class=\"ddlabel\" colspan=\"3\" scope=\"row\">{etiqueta}</th><td class=\"dddefault\" colspan=\"3\">{valor}</td></tr>\n");
        sb.Append("<tr><td class=\"ddseparator\" colspan=\"12\">&nbsp;</td></tr>\n");
        sb.Append("<tr><td class=\"ddseparator\" colspan=\"12\">***Tipo de Histórico Académico:UNAP HISTORICO UNAPEC No es Oficial ***</td></tr>\n");
        sb.Append("<tr><th class=\"ddtitle\" colspan=\"12\">GRADO A OBTENER</th></tr>\n");
        sb.Append("<tr><th class=\"ddlabel\" scope=\"row\">BUSCANDO GRADUARSE:</th><td class=\"dddefault\" colspan=\"3\">LICENCIADO</td><th class=\"ddlabel\" colspan=\"2\" scope=\"row\">Fecha Grado:</th><td class=\"dddefault\" colspan=\"6\"></td></tr>\n");
        sb.Append("<tr><th class=\"ddtitle\" colspan=\"6\">Información de Currículum</th></tr>\n");
        sb.Append("<tr><th class=\"ddlabel\" colspan=\"4\" scope=\"row\">Programa:</th><td class=\"dddefault\" colspan=\"8\">OTRO PROGRAMA GRADO</td></tr>\n");
        sb.Append(EncabezadoDeTotales(4, 3));
        sb.Append(FilaDeTotales("Institución:", 4, 3, global, "ddlabel"));
        sb.Append("<tr><td class=\"ddseparator\" colspan=\"12\">&nbsp;</td></tr>\n");
        sb.Append("<tr><th class=\"ddtitle\" colspan=\"12\">CRÉDITO DE INSTITUCIÓN <a href=\"#top\">-Arriba-</a></th></tr>\n\n");

        foreach (var (nombre, nivel, filas, periodo, acumulado) in periodos)
        {
            sb.Append($"<tr><th class=\"ddlabel\" colspan=\"12\" scope=\"row\">Periodo: {nombre} {nivel}</th></tr>\n");
            foreach (var (etiqueta, valor) in new[] { ("Escuela:", $"ESCUELA DE {Falso}"), ("Carrera:", $"ADMINISTRACION DE {Falso}"), ("Tipo de Alumno:", "ANTIGUO"), ("Estado académico:", "NORMAL") })
                sb.Append($"<tr><th class=\"ddlabel\" colspan=\"4\" scope=\"row\">{etiqueta}</th><td class=\"dddefault\" colspan=\"7\">{valor}</td></tr>\n");
            sb.Append("<tr><th class=\"ddheader\">Materia</th><th class=\"ddheader\">Curso</th><th class=\"ddheader\">Campus</th><th class=\"ddheader\">Nivel</th><th class=\"ddheader\" colspan=\"2\">Título</th><th class=\"ddheader\">Calificación</th><th class=\"ddheader\">Horas Crédito</th><th class=\"ddheader\">Puntos de Calidad</th><th class=\"ddheader\">Fechas Inicio y Fin</th><th class=\"ddheader\">R</th><th class=\"ddheader\">UEC Horas de Contacto</th></tr>\n");
            foreach (var (m, nota, puntos) in filas)
                sb.Append($"<tr><td class=\"dddefault\">{E(m.Materia)}</td><td class=\"dddefault\">{E(m.Curso)}</td><td class=\"dddefault\">CAMPUS - {Falso}</td><td class=\"dddefault\">{E(string.IsNullOrEmpty(m.Nivel) ? "GR" : m.Nivel)}</td><td class=\"dddefault\" colspan=\"2\">{E(m.Titulo)}</td><td class=\"dddefault\">{nota}</td><td class=\"dddefault\">{H(m.HorasCredito)}</td><td class=\"dddefault\">{P(puntos)}</td><td class=\"dddead\">&nbsp;</td><td class=\"dddefault\">&nbsp;</td><td class=\"dddead\">&nbsp;</td></tr>\n");
            sb.Append("<tr><th class=\"ddtitle\" colspan=\"12\">Totales Periodo (GRADO)</th></tr>\n");
            sb.Append(EncabezadoDeTotales(5, 2));
            sb.Append(FilaDeTotales("Periodo Actual", 5, 2, periodo, "ddlabel"));
            sb.Append(FilaDeTotales("Acumulativo:", 5, 2, acumulado, "ddlabel"));
            sb.Append("<tr><td class=\"ddseparator\" colspan=\"12\">&nbsp;</td></tr>\n");
            sb.Append("<tr><td class=\"ddseparator\" colspan=\"4\"><table class=\"infotexttable\"><tbody><tr><td class=\"indefault\"><span class=\"infotext\">Histórico Académico No Oficial</span></td></tr></tbody></table></td></tr>\n\n");
        }

        sb.Append("<tr><th class=\"ddtitle\" colspan=\"11\">TOTALES DE HISTÓRICO ACADÉMICO (GRADO) <a href=\"#top\">-Arriba-</a></th></tr>\n");
        sb.Append(EncabezadoDeTotales(4, 2));
        sb.Append(FilaDeTotales("Total Institución:", 4, 2, global, "ddlabel"));
        sb.Append(FilaDeTotales("Total Transferido:", 4, 2, new TotalesIndice(0, 0, 0, 0, 0), "ddlabel"));
        sb.Append(FilaDeTotales("Global:", 4, 2, global, "ddlabel"));
        sb.Append("<tr><td class=\"ddseparator\" colspan=\"11\">&nbsp;</td></tr>\n\n");

        if (real.EnProgreso.Count > 0)
        {
            sb.Append("<tr><th class=\"ddtitle\" colspan=\"11\">CURSOS EN PROGRESO <a href=\"#top\">-Arriba-</a></th></tr>\n");
            foreach (var pe in real.EnProgreso)
            {
                sb.Append($"<tr><th class=\"ddlabel\" colspan=\"11\" scope=\"row\">Periodo: {Desplazar(pe.Nombre, desplazamiento)} {(string.IsNullOrEmpty(pe.Nivel) ? "GRADO" : pe.Nivel)}</th></tr>\n");
                sb.Append($"<tr><th class=\"ddlabel\" colspan=\"4\" scope=\"row\">Escuela:</th><td class=\"dddefault\" colspan=\"7\">ESCUELA DE {Falso}</td></tr>\n");
                sb.Append("<tr><th class=\"ddheader\">Materia</th><th class=\"ddheader\">Curso</th><th class=\"ddheader\">Campus</th><th class=\"ddheader\">Nivel</th><th class=\"ddheader\" colspan=\"3\">Título</th><th class=\"ddheader\" colspan=\"2\">Horas Crédito</th><th class=\"ddheader\" colspan=\"2\">Fechas Inicio y Fin</th></tr>\n");
                foreach (var c in pe.Cursos)
                    sb.Append($"<tr><td class=\"dddefault\">{E(c.Materia)}</td><td class=\"dddefault\">{E(c.Curso)}</td><td class=\"dddefault\">CAMPUS - {Falso}</td><td class=\"dddefault\">{E(string.IsNullOrEmpty(c.Nivel) ? "GR" : c.Nivel)}</td><td class=\"dddefault\" colspan=\"3\">{E(c.Titulo)}</td><td class=\"dddefault\" colspan=\"2\">{H(c.HorasCredito)}</td><td class=\"dddead\" colspan=\"2\">&nbsp;</td></tr>\n");
                sb.Append("<tr><td class=\"ddseparator\" colspan=\"11\">&nbsp;</td></tr>\n");
            }
        }
        sb.Append("</tbody>\n</table>\n</div>\n</body></html>\n");

        var resultado = sb.ToString();
        Verificar(resultado, real);
        return resultado;
    }

    /// <summary>Comprueba que la muestra se lee, que sus totales cuadran y que no quedó nada del original (nombre, matrícula, programa, campus).</summary>
    private static void Verificar(string html, HistoricoBanner real)
    {
        var leido = HistoricoParser.Parse(html);
        var problemas = ValidadorHistorico.Validar(leido);
        if (problemas.Count > 0) throw new InvalidOperationException("La muestra anonimizada no cuadra: " + string.Join(" ", problemas.Take(3)));
        if (leido.Periodos.Count != real.Periodos.Count || leido.Periodos.Sum(p => p.Materias.Count) != real.Periodos.Sum(p => p.Materias.Count))
            throw new InvalidOperationException("La muestra anonimizada perdió períodos o materias respecto del original.");

        // Un dato personal que sobreviva es un error, no un aviso.
        var decodificado = WebUtility.HtmlDecode(html);
        foreach (var dato in new[] { real.Alumno.Nombre, real.Alumno.Programa, real.Alumno.Escuela, real.Alumno.Campus, real.Alumno.Carrera })
            if (!string.IsNullOrWhiteSpace(dato) && dato.Length > 3 && !dato.Contains(Falso, StringComparison.OrdinalIgnoreCase)
                && decodificado.Contains(dato, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"La muestra anonimizada todavía contiene «{dato}».");
    }

    private static void Cabecera(StringBuilder sb)
    {
        sb.Append("<!DOCTYPE html>\n<html lang=\"en\"><head><meta charset=\"UTF-8\"><title>Histórico Académico</title>\n");
        sb.Append("<script language=\"JavaScript\" type=\"text/javascript\">var userDetails = \"ESTUDIANTE DE PRUEBA\"; var userID = \"A00000000\";</script>\n</head>\n<body>\n");
        sb.Append("<!-- Datos FALSOS generados por AnonimizadorHistorico a partir de un histórico real: identidad, fechas y calificaciones ficticias. Reproduce la estructura de bwskotrn.P_ViewTran (Banner 8). -->\n");
        sb.Append("<div class=\"pagebodydiv\">\n<table class=\"datadisplaytable\" summary=\"Historia académica\">\n<tbody>\n");
    }

    private static string EncabezadoDeTotales(int vacias, int colspanPga) =>
        $"<tr><td class=\"dddead\" colspan=\"{vacias}\">&nbsp;</td><th class=\"ddheader\">Horas Intentadas</th><th class=\"ddheader\">Horas Aprobadas</th><th class=\"ddheader\">Horas Ganadas</th><th class=\"ddheader\">Horas PGA</th><th class=\"ddheader\">Puntos de Calidad</th><th class=\"ddheader\" colspan=\"{colspanPga}\">PGA</th></tr>\n";

    private static string FilaDeTotales(string etiqueta, int colspanEtiqueta, int colspanPga, TotalesIndice t, string clase) =>
        $"<tr><th class=\"{clase}\" colspan=\"{colspanEtiqueta}\" scope=\"row\">{etiqueta}</th><td class=\"dddefault\">{H(t.HorasIntentadas)}</td><td class=\"dddefault\">{H(t.HorasAprobadas)}</td><td class=\"dddefault\">{H(t.HorasAprobadas)}</td><td class=\"dddefault\">{H(t.HorasPga)}</td><td class=\"dddefault\">{P(t.PuntosCalidad)}</td><td class=\"dddefault\" colspan=\"{colspanPga}\">{P(t.Indice)}</td></tr>\n";

    private static string H(decimal v) => v.ToString("0.000", CultureInfo.InvariantCulture);
    private static string P(decimal v) => v.ToString("0.00", CultureInfo.InvariantCulture);
    private static string E(string s) => WebUtility.HtmlEncode(s);

    private static string Sortear(Random rng, bool aprobada)
    {
        if (!aprobada) return "F";
        var azar = rng.Next(Notas.Sum(n => n.Peso));
        foreach (var (letra, peso) in Notas)
        {
            if (azar < peso) return letra;
            azar -= peso;
        }
        return "B";
    }

    private static string NombreDe(object periodo) => periodo switch { PeriodoHistorico p => p.Nombre, PeriodoEnProgreso e => e.Nombre, _ => "" };

    private static int AnioDe(string nombre) => NombreDePeriodo().Match(nombre.Trim()) is { Success: true } m ? int.Parse(m.Groups["anio"].Value) : 0;

    private static string Desplazar(string nombre, int anios) =>
        NombreDePeriodo().Match(nombre.Trim()) is { Success: true } m ? $"{m.Groups["termino"].Value} {int.Parse(m.Groups["anio"].Value) + anios}" : nombre;
}
