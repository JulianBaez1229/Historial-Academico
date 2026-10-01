using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using HistorialAcademico.Core.Horarios;

namespace HistorialAcademico.Banner;

/// <summary>
/// Lee la respuesta JSON de <c>searchResults/searchResults</c> de Banner 9 (la que usa la pantalla "Consultar programación
/// académica") y la convierte en secciones. Se construyó sobre respuestas reales (ver samples/horarios/).
/// Del profesor solo se toma el nombre: el correo y la matrícula interna que trae el JSON se descartan.
/// </summary>
public static class SeccionesParser
{
    private static readonly Dictionary<string, int> Meses = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ene"] = 1, ["jan"] = 1, ["feb"] = 2, ["mar"] = 3, ["abr"] = 4, ["apr"] = 4, ["may"] = 5, ["jun"] = 6,
        ["jul"] = 7, ["ago"] = 8, ["aug"] = 8, ["sep"] = 9, ["oct"] = 10, ["nov"] = 11, ["dic"] = 12, ["dec"] = 12,
    };

    public static ResultadoBusqueda Parse(string json)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException)
        {
            // Si la sesión caducó, Banner responde con la página HTML de inicio de sesión en vez de JSON.
            if (json.TrimStart().StartsWith('<')) throw new BannerSesionExpiradaException();
            throw new BannerException("Banner devolvió una respuesta que no pude leer como secciones.");
        }

        using (doc)
        {
            var raiz = doc.RootElement;
            if (raiz.ValueKind != JsonValueKind.Object || !raiz.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                throw new BannerException("La respuesta de Banner no trae la lista de secciones (campo data).");
            if (raiz.TryGetProperty("success", out var ok) && ok.ValueKind == JsonValueKind.False)
                throw new BannerException("Banner indicó que la consulta de secciones no fue exitosa.");

            var secciones = data.EnumerateArray().Select(LeerSeccion).ToList();
            var total = raiz.TryGetProperty("totalCount", out var tc) && tc.TryGetInt32(out var n) ? n : secciones.Count;
            return new ResultadoBusqueda { Total = total, Secciones = secciones };
        }
    }

    private static SeccionBanner LeerSeccion(JsonElement s)
    {
        var materia = Texto(s, "subject");
        var curso = Texto(s, "courseNumber");
        return new SeccionBanner
        {
            Periodo = Texto(s, "term"),
            PeriodoDescripcion = Decodificar(Texto(s, "termDesc")),
            Nrc = Texto(s, "courseReferenceNumber"),
            Seccion = Texto(s, "sequenceNumber"),
            Materia = materia,
            Curso = curso,
            Codigo = Texto(s, "subjectCourse") is { Length: > 0 } sc ? sc : materia + curso,
            Titulo = Decodificar(Texto(s, "courseTitle")),
            // creditHours viene nulo en las secciones reales; los créditos están en creditHourLow.
            Creditos = Decimal(s, "creditHourLow") ?? Decimal(s, "creditHours") ?? 0,
            Campus = Decodificar(Texto(s, "campusDescription")),
            TipoHorario = Decodificar(Texto(s, "scheduleTypeDescription")),
            Metodo = Decodificar(Texto(s, "instructionalMethodDescription")),
            Profesores = LeerProfesores(s),
            CupoMaximo = Entero(s, "maximumEnrollment"),
            Inscritos = Entero(s, "enrollment"),
            CuposDisponibles = Entero(s, "seatsAvailable"),
            Abierta = s.TryGetProperty("openSection", out var ab) && ab.ValueKind == JsonValueKind.True,
            Bloques = LeerBloques(s),
        };
    }

    private static List<string> LeerProfesores(JsonElement s)
    {
        var nombres = new List<string>();
        if (s.TryGetProperty("faculty", out var f) && f.ValueKind == JsonValueKind.Array)
            foreach (var p in f.EnumerateArray())
            {
                var nombre = NombrePropio(Texto(p, "displayName"));
                if (nombre.Length > 0 && !nombres.Contains(nombre)) nombres.Add(nombre);
            }
        return nombres;
    }

    private static List<BloqueBanner> LeerBloques(JsonElement s)
    {
        var bloques = new List<BloqueBanner>();
        if (!s.TryGetProperty("meetingsFaculty", out var m) || m.ValueKind != JsonValueKind.Array) return bloques;

        foreach (var reunion in m.EnumerateArray())
        {
            if (!reunion.TryGetProperty("meetingTime", out var t) || t.ValueKind != JsonValueKind.Object) continue;
            bloques.Add(new BloqueBanner
            {
                Dias = Dias(t),
                Inicio = Hora(Texto(t, "beginTime"), esInicio: true),
                Fin = Hora(Texto(t, "endTime"), esInicio: false),
                Edificio = Decodificar(Texto(t, "buildingDescription")),
                Aula = Texto(t, "room"),
                TipoReunion = Decodificar(Texto(t, "meetingTypeDescription")),
                TipoHorario = Texto(t, "meetingScheduleType"),
                FechaInicio = Fecha(Texto(t, "startDate")),
                FechaFin = Fecha(Texto(t, "endDate")),
            });
        }
        return bloques;
    }

    private static DiasSemana Dias(JsonElement t)
    {
        var dias = DiasSemana.Ninguno;
        void Si(string propiedad, DiasSemana dia) { if (t.TryGetProperty(propiedad, out var v) && v.ValueKind == JsonValueKind.True) dias |= dia; }
        Si("monday", DiasSemana.Lunes); Si("tuesday", DiasSemana.Martes); Si("wednesday", DiasSemana.Miercoles);
        Si("thursday", DiasSemana.Jueves); Si("friday", DiasSemana.Viernes); Si("saturday", DiasSemana.Sabado); Si("sunday", DiasSemana.Domingo);
        return dias;
    }

    /// <summary>
    /// Banner escribe la hora de inicio como HH01 (0801 = 8:00, 1701 = 17:00, 2001 = 20:00) para que un bloque que empieza
    /// justo cuando otro termina no se solape con él. Se normaliza a la hora real; la de fin se deja tal cual (1000 = 10:00).
    /// </summary>
    public static TimeOnly? Hora(string? texto, bool esInicio)
    {
        if (texto is null || !Regex.IsMatch(texto, @"^\d{4}$")) return null;
        int h = int.Parse(texto[..2]), m = int.Parse(texto[2..]);
        if (esInicio && m % 5 == 1) m--;
        return h is < 24 && m is < 60 ? new TimeOnly(h, m) : null;
    }

    /// <summary>"13-Dic-2026" → 13 de diciembre de 2026 (acepta meses en español o en inglés).</summary>
    public static DateOnly? Fecha(string? texto)
    {
        var m = Regex.Match(texto ?? "", @"^(\d{1,2})-([A-Za-z]{3})\.?-(\d{4})$");
        if (!m.Success || !Meses.TryGetValue(m.Groups[2].Value, out var mes)) return null;
        try { return new DateOnly(int.Parse(m.Groups[3].Value), mes, int.Parse(m.Groups[1].Value)); }
        catch (ArgumentOutOfRangeException) { return null; }
    }

    /// <summary>"Andres Alexander, Almonte Urena" (nombres, apellidos) → "Andres Alexander Almonte Urena".</summary>
    public static string NombrePropio(string? displayName) =>
        Regex.Replace(Decodificar(displayName ?? "").Replace(',', ' '), @"\s+", " ").Trim();

    /// <summary>Banner escapa las tildes como entidades HTML: "DISE&amp;Ntilde;O" → "DISEÑO".</summary>
    private static string Decodificar(string texto) => WebUtility.HtmlDecode(texto).Trim();

    private static string Texto(JsonElement e, string propiedad) =>
        e.TryGetProperty(propiedad, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static int Entero(JsonElement e, string propiedad) =>
        e.TryGetProperty(propiedad, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : 0;

    private static decimal? Decimal(JsonElement e, string propiedad) =>
        e.TryGetProperty(propiedad, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var n) ? n : null;
}
