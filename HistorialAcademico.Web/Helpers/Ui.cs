using System.Globalization;
using System.Net;
using HistorialAcademico.Core.Pensum;
using HistorialAcademico.Web.Services;
using Microsoft.AspNetCore.Html;

namespace HistorialAcademico.Web.Helpers;

/// <summary>Formato y presentación compartidos por las vistas (todo en español).</summary>
public static class Ui
{
    public static readonly CultureInfo Cultura = new("es-DO");

    private static readonly HashSet<string> Conectores = new(StringComparer.OrdinalIgnoreCase)
        { "de", "del", "la", "las", "el", "los", "y", "e", "o", "u", "a", "en", "con", "para", "por", "al" };
    private static readonly HashSet<string> Romanos = new(StringComparer.OrdinalIgnoreCase)
        { "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X" };

    /// <summary>
    /// Banner entrega títulos en MAYÚSCULAS, sin acentos y a veces recortados ("REDACCION DE TEXTOS DISCURSIVO").
    /// Los pasa a "Redaccion de Textos Discursivo". Los acentos no se pueden recuperar.
    /// </summary>
    public static string TituloBonito(string? titulo)
    {
        if (string.IsNullOrWhiteSpace(titulo)) return "";
        var palabras = titulo.Trim().ToLower(Cultura).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < palabras.Length; i++)
        {
            var p = palabras[i];
            palabras[i] = Romanos.Contains(p) ? p.ToUpperInvariant()
                : i > 0 && Conectores.Contains(p) ? p
                : char.ToUpper(p[0], Cultura) + p[1..];
        }
        return string.Join(' ', palabras);
    }

    /// <summary>Nombre del pénsum si la materia existe allí; si no, el título de Banner arreglado.</summary>
    public static string Nombre(EstadoAcademico e, string codigo, string? tituloBanner) =>
        e.BuscarPensum(codigo)?.Nombre ?? TituloBonito(tituloBanner);

    public static string FechaLarga(DateOnly? fecha) => fecha is null ? "—" : fecha.Value.ToString("d 'de' MMMM 'de' yyyy", Cultura);

    /// <summary>Las fechas se guardan en UTC; se muestran en hora local.</summary>
    public static string FechaHora(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), TimeZoneInfo.Local).ToString("dd/MM/yyyy h:mm tt", Cultura);

    /// <summary>
    /// Número que se anima contando desde 0 al cargar la página (ver site.js). El valor final ya está escrito en el HTML,
    /// así que sin JavaScript, o con "reducir movimiento", se ve igual sin animar. Los lectores de pantalla leen solo
    /// el valor final (el número que cuenta va oculto para ellos).
    /// </summary>
    public static IHtmlContent Contador(decimal valor, int decimales = 0, string sufijo = "")
    {
        var texto = valor.ToString("F" + decimales, Cultura);
        var dato = valor.ToString("F" + decimales, System.Globalization.CultureInfo.InvariantCulture);
        return new HtmlString(
            $"<span class=\"contador\" data-contar=\"{dato}\" data-decimales=\"{decimales}\" data-sufijo=\"{WebUtility.HtmlEncode(sufijo)}\" aria-hidden=\"true\">{texto}{WebUtility.HtmlEncode(sufijo)}</span>" +
            $"<span class=\"visually-hidden\">{texto}{WebUtility.HtmlEncode(sufijo)}</span>");
    }

    // ── Horarios de Banner ────────────────────────────────────────────────────────────────

    private static readonly (HistorialAcademico.Core.Horarios.DiasSemana Dia, string Abrev)[] DiasAbreviados =
    {
        (HistorialAcademico.Core.Horarios.DiasSemana.Lunes, "Lun"), (HistorialAcademico.Core.Horarios.DiasSemana.Martes, "Mar"),
        (HistorialAcademico.Core.Horarios.DiasSemana.Miercoles, "Mié"), (HistorialAcademico.Core.Horarios.DiasSemana.Jueves, "Jue"),
        (HistorialAcademico.Core.Horarios.DiasSemana.Viernes, "Vie"), (HistorialAcademico.Core.Horarios.DiasSemana.Sabado, "Sáb"),
        (HistorialAcademico.Core.Horarios.DiasSemana.Domingo, "Dom"),
    };

    /// <summary>"Mar y Jue" (o "Lun, Mié y Vie").</summary>
    public static string Dias(HistorialAcademico.Core.Horarios.DiasSemana dias)
    {
        var nombres = DiasAbreviados.Where(d => dias.HasFlag(d.Dia)).Select(d => d.Abrev).ToList();
        return nombres.Count <= 1 ? string.Join("", nombres) : string.Join(", ", nombres.Take(nombres.Count - 1)) + " y " + nombres[^1];
    }

    /// <summary>"8:00 – 10:00 a. m." en formato de 12 horas; vacío si Banner no publicó horas.</summary>
    public static string Horas(HistorialAcademico.Core.Horarios.BloqueBanner b)
    {
        if (b.Inicio is null || b.Fin is null) return "";
        string H(TimeOnly t) => t.ToString("h:mm", Cultura);
        var sufijoFin = b.Fin.Value.Hour >= 12 ? "p. m." : "a. m.";
        var sufijoIni = b.Inicio.Value.Hour >= 12 ? "p. m." : "a. m.";
        return sufijoIni == sufijoFin ? $"{H(b.Inicio.Value)} – {H(b.Fin.Value)} {sufijoFin}" : $"{H(b.Inicio.Value)} {sufijoIni} – {H(b.Fin.Value)} {sufijoFin}";
    }

    /// <summary>"9:00 a. m." / "5:30 p. m.".</summary>
    public static string Hora(TimeOnly t) => t.ToString("h:mm", Cultura) + (t.Hour >= 12 ? " p. m." : " a. m.");

    /// <summary>La hora que corresponde a minutos desde la medianoche (540 → "9:00 a. m.").</summary>
    public static string HoraMin(int minutos) => Hora(new TimeOnly(Math.Clamp(minutos / 60, 0, 23), minutos % 60));

    /// <summary>"ISO625-1 y ISO800-2 coinciden Jue de 9:00 a. m. a 10:00 a. m."</summary>
    public static string MotivoChoque(HistorialAcademico.Core.Horarios.Choque c) =>
        $"{c.A.Codigo}-{c.A.Seccion} y {c.B.Codigo}-{c.B.Seccion} coinciden {Dias(c.Dias)} de {Hora(c.Desde)} a {Hora(c.Hasta)}";

    /// <summary>Un decimal para usar dentro de CSS (siempre con punto).</summary>
    public static string Css(double valor) => valor.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>Una línea por bloque: días y horas, y el aula si la hay. Un bloque virtual sin día fijo se dice tal cual.</summary>
    public static string Bloque(HistorialAcademico.Core.Horarios.BloqueBanner b)
    {
        var lugar = string.Join(" ", new[] { b.Edificio, string.IsNullOrWhiteSpace(b.Aula) ? "" : "aula " + b.Aula }.Where(x => x.Length > 0));
        if (b.SinDiaFijo) return "Virtual, sin día fijo" + (lugar.Length > 0 ? " · " + lugar : "");
        var horas = Horas(b);
        return Dias(b.Dias) + (horas.Length > 0 ? " " + horas : "") + (lugar.Length > 0 ? " · " + lugar : "");
    }

    public static string Num(decimal n) => n.ToString("0.##", Cultura);
    public static string Indice(decimal n) => n.ToString("0.00", Cultura);
    public static string Pct(decimal n) => n.ToString("0.0", Cultura) + "%";

    public static string Texto(EstadoMateria s) => s switch
    {
        EstadoMateria.Aprobada => "Aprobada",
        EstadoMateria.Exenta => "Exenta",
        EstadoMateria.EnCurso => "En curso",
        EstadoMateria.Disponible => "Disponible",
        _ => "Bloqueada",
    };

    public static string Badge(EstadoMateria s) => s switch
    {
        EstadoMateria.Aprobada => "bg-success",
        EstadoMateria.Exenta => "bg-info",
        EstadoMateria.EnCurso => "bg-primary",
        EstadoMateria.Disponible => "bg-warning text-dark",
        _ => "bg-secondary",
    };

    /// <summary>Ícono de cada estado (ver <see cref="Iconos"/>): el estado nunca se comunica solo con el color.</summary>
    public static string IconoEstado(EstadoMateria s) => s switch
    {
        EstadoMateria.Aprobada => "check",
        EstadoMateria.Exenta => "escudo",
        EstadoMateria.EnCurso => "reloj",
        EstadoMateria.Disponible => "disponible",
        _ => "candado",
    };

    /// <summary>Clase CSS de la tarjeta del mapa del pénsum (definidas en site.css).</summary>
    public static string ClaseTarjeta(EstadoMateria s) => "estado-" + s.ToString().ToLowerInvariant();

    /// <summary>Prerrequisitos del CSV en lenguaje natural: "E077; 67% créditos aprobados" → "E077 + 67% de créditos".</summary>
    public static string PrerrequisitoTexto(string? prerrequisitos)
    {
        if (string.IsNullOrWhiteSpace(prerrequisitos)) return "—";
        var partes = PrerrequisitoParser.TryParse(prerrequisitos, out var reqs, out _)
            ? reqs.Select(r => r.Materia ?? $"{r.Porcentaje}% de créditos")
            : new[] { prerrequisitos };
        return string.Join(" + ", partes);
    }
}
