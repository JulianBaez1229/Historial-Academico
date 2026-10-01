using System.Text.Json;
using HistorialAcademico.Banner;
using Xunit;

namespace HistorialAcademico.Tests;

/// <summary>
/// Herramienta de captura de horarios (E01-A) contra un servidor local que imita Banner 9, con un Chromium real:
/// comprueba el recorrido, los archivos que guarda y cómo hace las peticiones (token, sesión única, orden).
/// </summary>
public class BannerHorariosCapturaTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "horarios-test-" + Guid.NewGuid().ToString("N"));
    private readonly ServidorBanner9 _servidor = new();

    private const string SeccionesFalsas = """
        [
          { "id": 1, "term": "202630", "courseReferenceNumber": "1001", "subject": "ISO", "courseNumber": "100", "subjectCourse": "ISO100",
            "sequenceNumber": "1", "courseTitle": "FUNDAMENTOS DE PRUEBA", "maximumEnrollment": 30, "enrollment": 29, "seatsAvailable": 1, "openSection": true,
            "faculty": [], "meetingsFaculty": [] }
        ]
        """;

    public BannerHorariosCapturaTests()
    {
        Directory.CreateDirectory(Path.Combine(_dir, ".auth"));
        File.WriteAllText(Path.Combine(_dir, ".auth", "banner.json"), "{\"cookies\":[],\"origins\":[]}");
        _servidor.SeccionesPorPeriodo["202630"] = SeccionesFalsas;
        _servidor.SeccionesPorPeriodo["202620"] = SeccionesFalsas.Replace("202630", "202620");
    }

    public void Dispose() { _servidor.Dispose(); try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private BannerClient Cliente(bool hostPermitido = true) => new(new BannerOptions
    {
        BaseUrl = _servidor.Base + "/",
        CarpetaDatos = _dir,
        UrlProgramacionAcademica = _servidor.UrlProgramacion,
        HostsBanner = hostPermitido ? new() { "127.0.0.1" } : new() { "unapec.edu.do" },
        TiempoNavegacionSegundos = 20,
    });

    [Fact]
    public async Task RecorreLaProgramacionYGuardaLasRespuestasReales()
    {
        var (carpeta, pasos) = await Cliente().ExplorarHorariosAsync("ISO");

        // Archivos capturados.
        foreach (var archivo in new[]
        {
            "01-seleccion-periodo.html", "01-elementos.txt", "02-periodos.json", "pasos.log",
            "03-materias-202710.json", "04-fijar-periodo-202710.json", "05-secciones-202710-ISO.json",
            "03-materias-202630.json", "05-secciones-202630-ISO.json", "03-materias-202620.json", "05-secciones-202620-ISO.json",
        })
            Assert.True(File.Exists(Path.Combine(carpeta, archivo)), $"Falta {archivo}");

        Assert.Contains("id=\"txt_term\"", File.ReadAllText(Path.Combine(carpeta, "01-seleccion-periodo.html")));
        Assert.Contains(File.ReadAllLines(Path.Combine(carpeta, "01-elementos.txt")), l => l.StartsWith("input#txt_term"));

        // Elige el período más reciente (aunque esté vacío) y los dos siguientes de Grado con materias publicadas; ignora Posgrado.
        Assert.Contains(pasos, p => p.Contains("Períodos que se capturan: 202710, 202630, 202620"));
        Assert.DoesNotContain(_servidor.Peticiones, p => p.Consulta.Contains("term=202635") || p.Consulta.Contains("txt_term=202635"));
        Assert.Contains(pasos, p => p.Contains("[202710]") && p.Contains("totalCount=0"));       // el estado vacío se registra, no es un error
        Assert.Contains(pasos, p => p.Contains("[202630]") && p.Contains("totalCount=1"));
        Assert.DoesNotContain(pasos, p => p.Contains("FALLÓ"));
        Assert.Equal(pasos, File.ReadAllLines(Path.Combine(carpeta, "pasos.log")).Select(l => l).ToList());
    }

    [Fact]
    public async Task MandaElTokenDeSincronizacionYUnaSolaSesionEnTodasLasBusquedas()
    {
        await Cliente().ExplorarHorariosAsync("ISO");

        var fijar = _servidor.Peticiones.Where(p => p.Ruta.EndsWith("/term/search")).ToList();
        Assert.Equal(3, fijar.Count);   // uno por período capturado
        Assert.All(fijar, p =>
        {
            Assert.Equal("POST", p.Metodo);
            Assert.Equal(ServidorBanner9.Token, p.Cabeceras["x-synchronizer-token"]);   // sin él Banner responde 403
            Assert.Contains("mode=search", p.Consulta);
            Assert.Contains("application/x-www-form-urlencoded", p.Cabeceras["content-type"]);
        });

        var sesiones = _servidor.Peticiones.Where(p => p.Ruta.EndsWith("/searchResults"))
            .Select(p => p.Consulta.Split('&').First(x => x.StartsWith("uniqueSessionId=")))
            .Concat(fijar.Select(p => p.Cuerpo.Split('&').First(x => x.StartsWith("uniqueSessionId="))))
            .Distinct().ToList();
        Assert.Single(sesiones);   // el mismo identificador de sesión en todo el recorrido
    }

    [Fact]
    public async Task FijaElPeriodoAntesDeBuscarLasSecciones()
    {
        await Cliente().ExplorarHorariosAsync("ISO");

        var orden = _servidor.Peticiones.Select(p => p.Ruta.Split('/')[^1]).ToList();
        for (var i = 0; i < orden.Count; i++)
            if (orden[i] == "searchResults")
                Assert.Contains("search", orden.Take(i));   // term/search siempre precede a la búsqueda
        Assert.Contains(_servidor.Peticiones, p => p.Ruta.EndsWith("/searchResults") && p.Consulta.Contains("txt_subject=ISO") && p.Consulta.Contains("pageMaxSize=50"));
    }

    [Fact]
    public async Task ElPeriodoSinSeccionesQuedaComoJsonVacioValido()
    {
        var (carpeta, _) = await Cliente().ExplorarHorariosAsync("ISO");

        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(carpeta, "05-secciones-202710-ISO.json")));
        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(0, doc.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Equal(0, doc.RootElement.GetProperty("data").GetArrayLength());
        Assert.Equal("[]", File.ReadAllText(Path.Combine(carpeta, "03-materias-202710.json")).Replace("\n", "").Replace(" ", ""));
    }

    [Fact]
    public async Task SiElHostNoEsDeBannerLoTomaPorAutenticacionYPideIniciarSesion()
    {
        // Es la misma regla que protege contra login.microsoftonline.com: fuera de HostsBanner = pantalla de autenticación.
        await Assert.ThrowsAsync<BannerSesionExpiradaException>(() => Cliente(hostPermitido: false).ExplorarHorariosAsync("ISO"));
        Assert.DoesNotContain(_servidor.Peticiones, p => p.Ruta.Contains("searchResults"));   // no consultó nada
    }

    [Fact]
    public async Task SinSesionGuardadaPideIniciarSesionSinAbrirElNavegador()
    {
        File.Delete(Path.Combine(_dir, ".auth", "banner.json"));
        await Assert.ThrowsAsync<BannerSesionExpiradaException>(() => Cliente().ExplorarHorariosAsync("ISO"));
        Assert.Empty(_servidor.Peticiones);
    }

    [Fact]
    public void LosHostsDeBannerPorDefectoSonLosDeUnapec()
    {
        Assert.Equal(new[] { "unapec.edu.do" }, new BannerOptions().HostsBanner);
        Assert.EndsWith("termSelection?mode=search", new BannerOptions().UrlProgramacionAcademica);
    }
}
