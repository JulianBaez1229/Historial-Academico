using System.Diagnostics;
using System.Text;
using HistorialAcademico.Banner;
using HistorialAcademico.Core.Horarios;

namespace HistorialAcademico.Tests;

/// <summary>
/// Consulta de secciones de una materia (E01-B) contra un servidor local que imita Banner 9, con un Chromium real:
/// paginación, pausas, período fijado una sola vez, estado vacío y sesión caducada.
/// </summary>
public class BannerConsultaSeccionesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "consulta-test-" + Guid.NewGuid().ToString("N"));
    private readonly ServidorBanner9 _servidor = new();

    public BannerConsultaSeccionesTests()
    {
        Directory.CreateDirectory(Path.Combine(_dir, ".auth"));
        File.WriteAllText(Path.Combine(_dir, ".auth", "banner.json"), "{\"cookies\":[],\"origins\":[]}");
    }

    public void Dispose() { _servidor.Dispose(); try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private static string Json(string materia, string curso, int cuantas, int primerNrc)
    {
        var sb = new StringBuilder("[");
        for (var i = 0; i < cuantas; i++)
            sb.Append(i > 0 ? "," : "").Append(
                $"{{\"term\":\"202630\",\"courseReferenceNumber\":\"{primerNrc + i}\",\"subject\":\"{materia}\",\"courseNumber\":\"{curso}\",\"subjectCourse\":\"{materia}{curso}\"," +
                $"\"sequenceNumber\":\"{i + 1}\",\"courseTitle\":\"PRUEBA\",\"creditHourLow\":3,\"maximumEnrollment\":30,\"enrollment\":1,\"seatsAvailable\":29,\"openSection\":true,\"faculty\":[],\"meetingsFaculty\":[]}}");
        return sb.Append(']').ToString();
    }

    /// <summary>Junta varios arreglos JSON en uno.</summary>
    private static string Unir(params string[] arreglos) => "[" + string.Join(",", arreglos.Select(x => x.Trim('[', ']'))) + "]";

    private BannerClient Cliente(int pausaMs = 0, bool hostPermitido = true) => new(new BannerOptions
    {
        BaseUrl = _servidor.Base + "/",
        CarpetaDatos = _dir,
        UrlProgramacionAcademica = _servidor.UrlProgramacion,
        HostsBanner = hostPermitido ? new() { "127.0.0.1" } : new() { "unapec.edu.do" },
        TiempoNavegacionSegundos = 20,
        PausaEntreConsultasMs = pausaMs,
        TamanoPaginaSecciones = 50,
    });

    private static readonly ConsultaBanner Iso100 = new("ISO", "100");

    private IEnumerable<ServidorBanner9.Peticion> Busquedas => _servidor.Peticiones.Where(p => p.Ruta.EndsWith("/searchResults"));

    [Fact]
    public async Task PaginaHastaTraerTodasLasSecciones()
    {
        _servidor.SeccionesPorPeriodo["202630"] = Json("ISO", "100", 120, 5000);

        var r = await Cliente().ConsultarSeccionesAsync("202630", new[] { Iso100 });

        Assert.Equal(120, r.Total);
        Assert.Equal(120, r.Secciones.Count);
        Assert.Equal(120, r.Secciones.Select(s => s.Nrc).Distinct().Count());
        var paginas = Busquedas.Select(p => p.Consulta.Split('&').Where(x => x.StartsWith("pageOffset=") || x.StartsWith("pageMaxSize=")).OrderBy(x => x).ToList()).ToList();
        Assert.Equal(3, paginas.Count);
        Assert.Equal(new[] { "0", "50", "100" }, Busquedas.Select(p => p.Consulta.Split('&').First(x => x.StartsWith("pageOffset=")).Split('=')[1]));
        Assert.All(paginas, x => Assert.Contains("pageMaxSize=50", x));
    }

    [Fact]
    public async Task UnaSolaPaginaSeConsultaUnaSolaVez()
    {
        _servidor.SeccionesPorPeriodo["202630"] = Json("ISO", "100", 50, 5000);   // justo una página llena: no pide otra

        var r = await Cliente().ConsultarSeccionesAsync("202630", new[] { Iso100 });

        Assert.Equal(50, r.Secciones.Count);
        Assert.Single(Busquedas);
    }

    [Fact]
    public async Task DejaUnaPausaEntrePeticionesYNuncaVaARafagas()
    {
        _servidor.SeccionesPorPeriodo["202630"] = Json("ISO", "100", 120, 5000);   // 3 páginas = 2 pausas

        var reloj = Stopwatch.StartNew();
        var cliente = Cliente(pausaMs: 400);
        await cliente.ConsultarSeccionesAsync("202630", new[] { Iso100 });
        reloj.Stop();

        Assert.Equal(3, Busquedas.Count());
        // Solo el lanzamiento del navegador puede sumar; las 2 pausas de 400 ms como mínimo tienen que estar.
        Assert.True(reloj.ElapsedMilliseconds >= 800, $"Tardó {reloj.ElapsedMilliseconds} ms: faltó la pausa entre páginas");
    }

    [Fact]
    public async Task FijaElPeriodoUnaVezConElTokenYUnaSolaSesion()
    {
        _servidor.SeccionesPorPeriodo["202630"] = Unir(Json("ADM", "103", 1, 1), Json("ADM", "536", 1, 2));

        await Cliente().ConsultarSeccionesAsync("202630", new[] { new ConsultaBanner("ADM", "103"), new ConsultaBanner("ADM", "536") });

        var fijar = _servidor.Peticiones.Where(p => p.Ruta.EndsWith("/term/search")).ToList();
        Assert.Single(fijar);
        Assert.Equal("POST", fijar[0].Metodo);
        Assert.Equal(ServidorBanner9.Token, fijar[0].Cabeceras["x-synchronizer-token"]);
        Assert.Contains("term=202630", fijar[0].Cuerpo);

        var sesiones = Busquedas.Select(p => p.Consulta.Split('&').First(x => x.StartsWith("uniqueSessionId=")))
            .Append(fijar[0].Cuerpo.Split('&').First(x => x.StartsWith("uniqueSessionId="))).Distinct().ToList();
        Assert.Single(sesiones);
        // El período se fija antes de la primera búsqueda.
        var orden = _servidor.Peticiones.Select(p => p.Ruta.Split('/')[^1]).ToList();
        Assert.True(orden.IndexOf("search") < orden.IndexOf("searchResults"));
    }

    [Fact]
    public async Task ConsultaCadaMateriaPorSuMateriaYSuNumeroDeCurso()
    {
        _servidor.SeccionesPorPeriodo["202630"] = Unir(Json("ADM", "103", 2, 10), Json("ADM", "536", 1, 20), Json("ADM", "540", 3, 30), Json("ISO", "100", 4, 40));

        var r = await Cliente().ConsultarSeccionesAsync("202630",
            new[] { new ConsultaBanner("ADM", "103"), new ConsultaBanner("ADM", "536"), new ConsultaBanner("ADM", "540") });

        Assert.Equal(6, r.Secciones.Count);
        Assert.Equal(new[] { "ADM103", "ADM536", "ADM540" }, r.Secciones.Select(s => s.Codigo).Distinct().OrderBy(x => x));
        Assert.DoesNotContain(r.Secciones, s => s.Materia == "ISO");
        var cursos = Busquedas.Select(p => p.Consulta).ToList();
        Assert.Contains(cursos, c => c.Contains("txt_subject=ADM") && c.Contains("txt_courseNumber=103"));
        Assert.Contains(cursos, c => c.Contains("txt_courseNumber=536"));
        Assert.Contains(cursos, c => c.Contains("txt_courseNumber=540"));
    }

    [Fact]
    public async Task UnaMateriaSinNumeroDeCursoTraeTodaLaMateria()
    {
        _servidor.SeccionesPorPeriodo["202630"] = Unir(Json("DEP", "101", 1, 10), Json("DEP", "205", 1, 20));

        var r = await Cliente().ConsultarSeccionesAsync("202630", new[] { new ConsultaBanner("DEP", null) });

        Assert.Equal(2, r.Secciones.Count);
        Assert.DoesNotContain(Busquedas, p => p.Consulta.Contains("txt_courseNumber"));
    }

    [Fact]
    public async Task UnPeriodoSinSeccionesEsUnResultadoVacioNoUnError()
    {
        var r = await Cliente().ConsultarSeccionesAsync("202710", new[] { Iso100 });

        Assert.True(r.SinSecciones);
        Assert.Empty(r.Secciones);
        Assert.Single(Busquedas);
    }

    [Fact]
    public async Task UnaPaginaDeLoginEnLugarDeJsonSignificaSesionCaducada()
    {
        _servidor.ResultadosHtml = "<html><body><form><input type=\"password\"></form></body></html>";

        await Assert.ThrowsAsync<BannerSesionExpiradaException>(() => Cliente().ConsultarSeccionesAsync("202630", new[] { Iso100 }));
    }

    [Fact]
    public async Task SoloEscribeEnBannerParaFijarElPeriodo_TodoLoDemasEsLectura()
    {
        _servidor.SeccionesPorPeriodo["202630"] = Json("ISO", "100", 120, 5000);

        await Cliente().ConsultarSeccionesAsync("202630", new[] { Iso100 });

        Assert.All(_servidor.Peticiones.Where(p => p.Metodo != "GET"), p => Assert.EndsWith("/term/search", p.Ruta));
    }

    [Theory]
    [InlineData("2026")]
    [InlineData("")]
    [InlineData("SEP-DIC 2026")]
    [InlineData("202630; drop")]
    public async Task UnCodigoDePeriodoInvalidoSeRechazaSinTocarBanner(string periodo)
    {
        var ex = await Assert.ThrowsAsync<BannerException>(() => Cliente().ConsultarSeccionesAsync(periodo, new[] { Iso100 }));

        Assert.Contains("no es un código de Banner válido", ex.Message);
        Assert.Empty(_servidor.Peticiones);
    }

    [Fact]
    public async Task SinNadaQueConsultarDevuelveVacioSinAbrirElNavegador()
    {
        var r = await Cliente().ConsultarSeccionesAsync("202630", Array.Empty<ConsultaBanner>());

        Assert.True(r.SinSecciones);
        Assert.Empty(_servidor.Peticiones);
    }

    [Fact]
    public async Task SinSesionGuardadaPideIniciarSesion()
    {
        File.Delete(Path.Combine(_dir, ".auth", "banner.json"));

        await Assert.ThrowsAsync<BannerSesionExpiradaException>(() => Cliente().ConsultarSeccionesAsync("202630", new[] { Iso100 }));
        Assert.Empty(_servidor.Peticiones);
    }

    // ── Varias materias con una sola sesión (E01-C) ───────────────────────────────────────

    private static readonly LoteConsulta[] TresMaterias =
    {
        new("ISO725", new[] { new ConsultaBanner("ISO", "725") }),
        new("ISO800", new[] { new ConsultaBanner("ISO", "800") }),
        new("ADM103", new[] { new ConsultaBanner("ADM", "103") }),
    };

    private void SembrarTres() =>
        _servidor.SeccionesPorPeriodo["202630"] = Unir(Json("ISO", "725", 2, 100), Json("ISO", "800", 1, 200), Json("ADM", "103", 3, 300));

    private async Task<List<(string Clave, ResultadoLote Resultado)>> Lote(BannerClient cliente, IReadOnlyList<LoteConsulta> lote, CancellationToken ct = default)
    {
        var entregados = new List<(string, ResultadoLote)>();
        await cliente.ConsultarLoteAsync("202630", lote, (item, r) => { entregados.Add((item.Clave, r)); return Task.CompletedTask; }, ct);
        return entregados;
    }

    [Fact]
    public async Task ElLoteEntregaCadaMateriaEnOrdenConUnaSolaSesionYElPeriodoFijadoUnaVez()
    {
        SembrarTres();

        var entregados = await Lote(Cliente(), TresMaterias);

        Assert.Equal(new[] { "ISO725", "ISO800", "ADM103" }, entregados.Select(e => e.Clave));
        Assert.Equal(new[] { 2, 1, 3 }, entregados.Select(e => e.Resultado.Busqueda!.Secciones.Count));
        Assert.All(entregados, e => Assert.Null(e.Resultado.Error));
        Assert.Single(_servidor.Peticiones.Where(p => p.Ruta.EndsWith("/term/search")));   // un solo navegador y un solo «Continuar»
        Assert.Equal(3, Busquedas.Count());
        var sesiones = Busquedas.Select(p => p.Consulta.Split('&').First(x => x.StartsWith("uniqueSessionId="))).Distinct().ToList();
        Assert.Single(sesiones);
    }

    [Fact]
    public async Task ElLoteDejaPausaEntreUnaMateriaYLaSiguiente()
    {
        SembrarTres();

        var reloj = Stopwatch.StartNew();
        await Lote(Cliente(pausaMs: 400), TresMaterias);
        reloj.Stop();

        Assert.True(reloj.ElapsedMilliseconds >= 800, $"Tardó {reloj.ElapsedMilliseconds} ms: faltó la pausa entre materias");
    }

    [Fact]
    public async Task UnaMateriaSinSeccionesEnElLoteEsUnResultadoVacioNoUnError()
    {
        _servidor.SeccionesPorPeriodo["202630"] = Json("ISO", "725", 1, 100);   // ISO800 y ADM103 no tienen nada

        var entregados = await Lote(Cliente(), TresMaterias);

        Assert.Equal(new[] { false, true, true }, entregados.Select(e => e.Resultado.Busqueda!.SinSecciones));
        Assert.All(entregados, e => Assert.Null(e.Resultado.Error));
    }

    [Fact]
    public async Task UnaMateriaQueDaErrorSeAnotaYElLoteSigue()
    {
        SembrarTres();
        _servidor.MateriasConError.Add("ISO");   // las dos ISO responden HTTP 500

        var entregados = await Lote(Cliente(), TresMaterias);

        Assert.Equal(3, entregados.Count);
        Assert.All(entregados.Take(2), e => { Assert.Null(e.Resultado.Busqueda); Assert.Contains("HTTP 500", e.Resultado.Error); });
        Assert.Equal(3, entregados[2].Resultado.Busqueda!.Secciones.Count);   // ADM103 sí llegó
    }

    [Fact]
    public async Task SiLaSesionCaducaAMitadSeCortaPeroLoYaEntregadoSeConserva()
    {
        SembrarTres();
        _servidor.MateriasConSesionCaducada.Add("ISO");   // ADM responde bien; en cuanto se pide una ISO llega el login
        var entregados = new List<string>();

        await Assert.ThrowsAsync<BannerSesionExpiradaException>(() =>
            Cliente().ConsultarLoteAsync("202630", new[] { TresMaterias[2], TresMaterias[0], TresMaterias[1] }, (item, _) => { entregados.Add(item.Clave); return Task.CompletedTask; }));

        Assert.Equal(new[] { "ADM103" }, entregados);   // la primera (ADM) llegó; al pedir ISO la sesión estaba caducada
    }

    [Fact]
    public async Task CancelarDetieneElLoteSinConsultarLasQueFaltan()
    {
        SembrarTres();
        using var cancelacion = new CancellationTokenSource();
        var entregados = new List<string>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Cliente().ConsultarLoteAsync("202630", TresMaterias, (item, _) => { entregados.Add(item.Clave); cancelacion.Cancel(); return Task.CompletedTask; }, cancelacion.Token));

        Assert.Equal(new[] { "ISO725" }, entregados);
        Assert.Single(Busquedas.Where(p => p.Consulta.Contains("txt_courseNumber=725")));
        Assert.DoesNotContain(Busquedas, p => p.Consulta.Contains("txt_courseNumber=800") || p.Consulta.Contains("txt_courseNumber=103"));
    }

    [Fact]
    public async Task UnLoteVacioNoAbreElNavegadorNiTocaBanner()
    {
        await Cliente().ConsultarLoteAsync("202630", Array.Empty<LoteConsulta>(), (_, _) => Task.CompletedTask);

        Assert.Empty(_servidor.Peticiones);
    }

    [Fact]
    public async Task ElLoteRechazaUnPeriodoInvalidoSinTocarBanner()
    {
        await Assert.ThrowsAsync<BannerException>(() =>
            Cliente().ConsultarLoteAsync("2026", TresMaterias, (_, _) => Task.CompletedTask));
        Assert.Empty(_servidor.Peticiones);
    }

    [Fact]
    public async Task ElLoteSinSesionGuardadaPideIniciarSesion()
    {
        File.Delete(Path.Combine(_dir, ".auth", "banner.json"));

        await Assert.ThrowsAsync<BannerSesionExpiradaException>(() => Lote(Cliente(), TresMaterias));
        Assert.Empty(_servidor.Peticiones);
    }

    [Fact]
    public async Task ElLoteSoloEscribeEnBannerParaFijarElPeriodo()
    {
        SembrarTres();

        await Lote(Cliente(), TresMaterias);

        Assert.All(_servidor.Peticiones.Where(p => p.Metodo != "GET"), p => Assert.EndsWith("/term/search", p.Ruta));
    }

    [Fact]
    public async Task SiElHostNoEsDeBannerLoTomaPorAutenticacion()
    {
        await Assert.ThrowsAsync<BannerSesionExpiradaException>(() => Cliente(hostPermitido: false).ConsultarSeccionesAsync("202630", new[] { Iso100 }));
        Assert.Empty(Busquedas);
    }
}
