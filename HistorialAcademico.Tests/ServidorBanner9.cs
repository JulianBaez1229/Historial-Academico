using System.Net;
using System.Net.Sockets;
using System.Text;

namespace HistorialAcademico.Tests;

/// <summary>
/// Servidor local que imita los servicios de Banner 9 "Registration" (períodos, materias, fijar período y secciones),
/// con las mismas rutas y forma de JSON que se capturaron del Banner real. Registra las peticiones para poder comprobar
/// cómo las hace el cliente (token de sincronización, sesión única…).
/// </summary>
public sealed class ServidorBanner9 : IDisposable
{
    public const string Token = "TOKEN-DE-PRUEBA-0123456789-abcdefgh";

    public record Peticion(string Metodo, string Ruta, string Consulta, Dictionary<string, string> Cabeceras, string Cuerpo);

    private readonly TcpListener _escucha = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _fin = new();
    private readonly List<Peticion> _peticiones = new();

    /// <summary>Períodos que devuelve getTerms.</summary>
    public string PeriodosJson { get; set; } = """
        [
          { "code": "202710", "description": "ENE-ABR 2027 GRADO (S&oacute;lo ver)" },
          { "code": "202635", "description": "SEP-DIC 2026 POSGRADO" },
          { "code": "202630", "description": "SEP-DIC 2026 GRADO" },
          { "code": "202620", "description": "MAY-AGO 2026 GRADO (S&oacute;lo ver)" }
        ]
        """;

    /// <summary>Períodos sin materias publicadas (el caso real de un cuatrimestre futuro).</summary>
    public HashSet<string> PeriodosVacios { get; } = new() { "202710" };

    /// <summary>JSON de secciones por período (data[]); si no hay, la búsqueda responde vacía.</summary>
    public Dictionary<string, string> SeccionesPorPeriodo { get; } = new();

    /// <summary>Si se asigna, la búsqueda responde esta página HTML (lo que pasa cuando la sesión caduca) en vez de JSON.</summary>
    public string? ResultadosHtml { get; set; }

    /// <summary>Materias (txt_subject) cuya búsqueda responde HTTP 500.</summary>
    public HashSet<string> MateriasConError { get; } = new();

    /// <summary>Materias (txt_subject) cuya búsqueda responde la página de inicio de sesión (la sesión caducó a mitad).</summary>
    public HashSet<string> MateriasConSesionCaducada { get; } = new();

    public ServidorBanner9()
    {
        _escucha.Start();
        _ = Task.Run(AceptarAsync);
    }

    public int Puerto => ((IPEndPoint)_escucha.LocalEndpoint).Port;
    public string Base => $"http://127.0.0.1:{Puerto}";
    public string UrlProgramacion => Base + "/StudentRegistrationSsb/ssb/term/termSelection?mode=search";

    public IReadOnlyList<Peticion> Peticiones { get { lock (_peticiones) return _peticiones.ToList(); } }

    private async Task AceptarAsync()
    {
        try
        {
            while (!_fin.IsCancellationRequested)
            {
                var cliente = await _escucha.AcceptTcpClientAsync(_fin.Token);
                _ = Task.Run(() => AtenderAsync(cliente));
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
    }

    private async Task AtenderAsync(TcpClient cliente)
    {
        try
        {
            using var _ = cliente;
            var flujo = cliente.GetStream();
            var acumulado = new StringBuilder();
            var buffer = new byte[8192];
            int fin;
            while ((fin = acumulado.ToString().IndexOf("\r\n\r\n", StringComparison.Ordinal)) < 0)
            {
                var n = await flujo.ReadAsync(buffer, _fin.Token);
                if (n == 0) return;
                acumulado.Append(Encoding.UTF8.GetString(buffer, 0, n));
            }

            var texto = acumulado.ToString();
            var lineas = texto[..fin].Split("\r\n");
            var inicio = lineas[0].Split(' ');
            var cabeceras = lineas.Skip(1).Select(l => l.Split(':', 2)).Where(p => p.Length == 2)
                .ToDictionary(p => p[0].Trim().ToLowerInvariant(), p => p[1].Trim());

            var largo = cabeceras.TryGetValue("content-length", out var cl) ? int.Parse(cl) : 0;
            var cuerpo = texto[(fin + 4)..];
            while (Encoding.UTF8.GetByteCount(cuerpo) < largo)
            {
                var n = await flujo.ReadAsync(buffer, _fin.Token);
                if (n == 0) break;
                cuerpo += Encoding.UTF8.GetString(buffer, 0, n);
            }

            var partes = inicio[1].Split('?', 2);
            var peticion = new Peticion(inicio[0], partes[0], partes.Length > 1 ? partes[1] : "", cabeceras, cuerpo);
            lock (_peticiones) _peticiones.Add(peticion);

            var (estado, tipo, contenido) = Responder(peticion);
            var bytes = Encoding.UTF8.GetBytes(contenido);
            var cabecera = $"HTTP/1.1 {estado}\r\nContent-Type: {tipo}\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n";
            await flujo.WriteAsync(Encoding.ASCII.GetBytes(cabecera), _fin.Token);
            await flujo.WriteAsync(bytes, _fin.Token);
        }
        catch (Exception) { /* el navegador cerró la conexión */ }
    }

    private static string Parametro(string consulta, string nombre) =>
        consulta.Split('&').Select(p => p.Split('=', 2)).Where(p => p.Length == 2 && p[0] == nombre)
            .Select(p => WebUtility.UrlDecode(p[1])).FirstOrDefault() ?? "";

    private (string Estado, string Tipo, string Contenido) Responder(Peticion p)
    {
        const string json = "application/json;charset=UTF-8";
        switch (p.Ruta)
        {
            case "/StudentRegistrationSsb/ssb/term/termSelection":
                return ("200 OK", "text/html; charset=utf-8",
                    $"<html><head><meta name=\"synchronizerToken\" content=\"{Token}\"/></head><body><h1 id=\"titulo\">Buscar clases</h1><input id=\"txt_term\"/></body></html>");

            case "/StudentRegistrationSsb/ssb/classSearch/getTerms":
                return ("200 OK", json, PeriodosJson);

            case "/StudentRegistrationSsb/ssb/classSearch/get_subject":
                return ("200 OK", json, PeriodosVacios.Contains(Parametro(p.Consulta, "term"))
                    ? "\n[\n]" : "[ { \"code\": \"ISO\", \"description\": \"SOFTWARE\" } ]");

            case "/StudentRegistrationSsb/ssb/term/search":
                // Como el Banner real: sin el token de sincronización rechaza la petición.
                return p.Cabeceras.TryGetValue("x-synchronizer-token", out var t) && t == Token
                    ? ("200 OK", json, "{\n  \"fwdURL\": \"/StudentRegistrationSsb/ssb/classSearch/classSearch\"\n}")
                    : ("403 Forbidden", json, "{\"error\":\"token\"}");

            case "/StudentRegistrationSsb/ssb/searchResults/searchResults":
            {
                if (ResultadosHtml is not null) return ("200 OK", "text/html; charset=utf-8", ResultadosHtml);
                var periodo = Parametro(p.Consulta, "txt_term");
                var materia = Parametro(p.Consulta, "txt_subject");
                if (MateriasConError.Contains(materia)) return ("500 Internal Server Error", json, "{\"error\":\"falló\"}");
                if (MateriasConSesionCaducada.Contains(materia)) return ("200 OK", "text/html; charset=utf-8", "<html><body><input type=\"password\"></body></html>");
                var curso = Parametro(p.Consulta, "txt_courseNumber");
                var desde = int.TryParse(Parametro(p.Consulta, "pageOffset"), out var o) ? o : 0;
                var tamano = int.TryParse(Parametro(p.Consulta, "pageMaxSize"), out var t2) && t2 > 0 ? t2 : 10;

                var todas = SeccionesPorPeriodo.TryGetValue(periodo, out var s)
                    ? System.Text.Json.Nodes.JsonNode.Parse(s)!.AsArray().Where(x =>
                        (materia == "" || (string?)x!["subject"] == materia) && (curso == "" || (string?)x!["courseNumber"] == curso)).ToList()
                    : new List<System.Text.Json.Nodes.JsonNode?>();
                var pagina = todas.Skip(desde).Take(tamano).Select(x => x!.ToJsonString()).ToList();
                return ("200 OK", json,
                    $"{{\"success\":true,\"totalCount\":{todas.Count},\"data\":[{string.Join(",", pagina)}],\"pageOffset\":{desde},\"pageMaxSize\":{tamano},\"sectionsFetchedCount\":{pagina.Count}}}");
            }
        }
        return ("404 Not Found", "text/plain", "no existe");
    }

    public void Dispose() { _fin.Cancel(); _escucha.Stop(); }
}
