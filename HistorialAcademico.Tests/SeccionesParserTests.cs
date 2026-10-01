using System.Text.Json;
using HistorialAcademico.Banner;
using HistorialAcademico.Core.Horarios;
using HistorialAcademico.Core.Planificacion;

namespace HistorialAcademico.Tests;

/// <summary>El parser de secciones sobre un JSON con la estructura real de Banner 9, pero con datos inventados.</summary>
public class SeccionesParserTests
{
    private static readonly string Json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "secciones-anonimizadas.json"));
    private static ResultadoBusqueda Resultado => SeccionesParser.Parse(Json);

    private static SeccionBanner Seccion(string nrc) => Resultado.Secciones.Single(s => s.Nrc == nrc);

    [Fact]
    public void LeeTodasLasSeccionesYElTotal()
    {
        Assert.Equal(5, Resultado.Total);
        Assert.Equal(5, Resultado.Secciones.Count);
        Assert.False(Resultado.SinSecciones);
    }

    [Fact]
    public void LeeLosDatosBasicosDeUnaSeccionConVariosBloques()
    {
        var s = Seccion("90001");
        Assert.Equal("209930", s.Periodo);
        Assert.Equal("SEP-DIC 2099 GRADO", s.PeriodoDescripcion);
        Assert.Equal("XYZ100", s.Codigo);
        Assert.Equal("XYZ", s.Materia);
        Assert.Equal("100", s.Curso);
        Assert.Equal("1", s.Seccion);
        Assert.Equal("CAMPUS - PRUEBA", s.Campus);
        Assert.Equal("PRESENCIAL", s.TipoHorario);
        Assert.Equal("LABORATORIO", s.Metodo);
        Assert.Equal((30, 29, 1), (s.CupoMaximo, s.Inscritos, s.CuposDisponibles));
        Assert.True(s.Abierta);
        Assert.False(s.Llena);
    }

    [Fact]
    public void LosCreditosSalenDeCreditHourLowPorqueCreditHoursViaNulo()
    {
        Assert.Equal(5m, Seccion("90001").Creditos);
        Assert.Equal(3m, Seccion("90003").Creditos);
    }

    [Fact]
    public void DecodificaLasEntidadesHtmlDeLosTitulosYLosNombres()
    {
        Assert.Equal("DISEÑO DE PRUEBA I", Seccion("90001").Titulo);
        Assert.Equal("SÁBADO Y DOMINGO", Seccion("90004").Titulo);
        Assert.Equal(new[] { "Marta Álvarez Ficticia Prueba Dos" }, Seccion("90002").Profesores);   // y sin repetirla
    }

    [Fact]
    public void ElNombreDelProfesorQuedaEnUnaSolaLineaSinComaNiEspaciosDobles()
    {
        Assert.Equal(new[] { "Pedro Ejemplo Prueba Uno" }, Seccion("90001").Profesores);
        Assert.Equal(new[] { "Juan Nadie Prueba Tres" }, Seccion("90004").Profesores);
    }

    [Fact]
    public void ElBloquePresencialTraeDiasHorasAulaYFechas()
    {
        var b = Seccion("90001").Bloques[0];
        Assert.Equal(DiasSemana.Martes | DiasSemana.Jueves, b.Dias);
        Assert.Equal(new TimeOnly(8, 0), b.Inicio);      // Banner escribe 0801
        Assert.Equal(new TimeOnly(10, 0), b.Fin);
        Assert.Equal("EDIF-03", b.Edificio);
        Assert.Equal("12", b.Aula);
        Assert.Equal("LAB", b.TipoHorario);
        Assert.Equal("CLASE", b.TipoReunion);
        Assert.Equal(new DateOnly(2099, 9, 1), b.FechaInicio);    // «Sep»
        Assert.Equal(new DateOnly(2099, 12, 13), b.FechaFin);     // «Dic»
        Assert.False(b.SinDiaFijo);
    }

    [Fact]
    public void LaParteVirtualSinDiaNoOcupaNingunDiaDeLaSemana()
    {
        var b = Seccion("90001").Bloques[1];
        Assert.True(b.SinDiaFijo);
        Assert.Equal(DiasSemana.Ninguno, b.Dias);
        Assert.Equal(new TimeOnly(18, 0), b.Inicio);
        Assert.Equal("VIR", b.TipoHorario);
        Assert.Equal("", b.Aula);
    }

    [Fact]
    public void UnaSeccionLlenaSeMarcaComoLlenaYCerrada()
    {
        var s = Seccion("90002");
        Assert.True(s.Llena);
        Assert.False(s.Abierta);
        Assert.Equal(DiasSemana.Lunes | DiasSemana.Miercoles, s.Bloques.Single().Dias);
        Assert.Equal(new TimeOnly(18, 0), s.Bloques.Single().Inicio);
        Assert.Equal(new TimeOnly(21, 0), s.Bloques.Single().Fin);
    }

    [Fact]
    public void UnCursoEspecialConCupoCeroYSeccionConLetrasNoEstaLlenoSinoSinCupoAsignado()
    {
        // Forma real observada en Banner: sección «TU1», ScheduleType «CURSO ESPECIAL», cupo 0 y cerrada.
        var r = SeccionesParser.Parse("""
            {"success":true,"totalCount":1,"data":[{"term":"209930","courseReferenceNumber":"90099","subject":"XYZ","courseNumber":"605",
             "subjectCourse":"XYZ605","sequenceNumber":"TU1","courseTitle":"CURSO ESPECIAL DE PRUEBA","creditHourLow":3,
             "scheduleTypeDescription":"CURSO ESPECIAL","maximumEnrollment":0,"enrollment":0,"seatsAvailable":0,"openSection":false,
             "faculty":[],"meetingsFaculty":[]}]}
            """);

        var s = Assert.Single(r.Secciones);
        Assert.Equal("TU1", s.Seccion);
        Assert.True(s.SinCupoAsignado);
        Assert.False(s.Llena);
        Assert.False(s.Abierta);
    }

    [Fact]
    public void UnaSeccionSinProfesorNiHorarioEsValida()
    {
        var s = Seccion("90003");
        Assert.Empty(s.Profesores);
        Assert.Empty(s.Bloques);
        Assert.True(s.Abierta);
    }

    [Fact]
    public void SabadoYDomingoYMesesEnEspanol()
    {
        var b = Seccion("90004").Bloques.Single();
        Assert.Equal(DiasSemana.Sabado | DiasSemana.Domingo, b.Dias);
        Assert.Equal(new DateOnly(2099, 1, 1), b.FechaInicio);    // «Ene»
    }

    [Fact]
    public void HorasYFechasQueFaltanOSonInvalidasQuedanNulasSinRomperNada()
    {
        var b = Seccion("90005").Bloques.Single();
        Assert.Null(b.Inicio);
        Assert.Null(b.Fin);
        Assert.Null(b.FechaInicio);
        Assert.Equal(new DateOnly(2099, 12, 31), b.FechaFin);
        Assert.True(b.SinDiaFijo);
    }

    [Fact]
    public void NoConservaElCorreoNiLaMatriculaDelProfesor()
    {
        var serializado = JsonSerializer.Serialize(Resultado);
        Assert.DoesNotContain("correo.falso", serializado);
        Assert.DoesNotContain("@", serializado);
        Assert.DoesNotContain("9999001", serializado);
        Assert.DoesNotContain("bannerId", serializado, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("email", serializado, StringComparison.OrdinalIgnoreCase);
    }

    // ── Estado vacío y errores ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("""{"success":true,"totalCount":0,"data":[],"pageOffset":0,"pageMaxSize":50,"sectionsFetchedCount":0}""")]
    [InlineData("""{"success":true,"totalCount":0,"data":[]}""")]
    [InlineData("""{"data":[]}""")]
    public void LaRespuestaSinSeccionesEsUnEstadoNormalNoUnError(string json)
    {
        var r = SeccionesParser.Parse(json);
        Assert.True(r.SinSecciones);
        Assert.Empty(r.Secciones);
    }

    [Fact]
    public void UnaPaginaDeLoginEnLugarDeJsonSignificaSesionCaducada()
    {
        Assert.Throws<BannerSesionExpiradaException>(() => SeccionesParser.Parse("<html><body><form><input type=\"password\"></form></body></html>"));
    }

    [Theory]
    [InlineData("no es json")]
    [InlineData("""{"success":true}""")]
    [InlineData("""{"success":false,"data":[]}""")]
    [InlineData("""[1,2,3]""")]
    public void UnaRespuestaQueNoSePuedeLeerDaUnErrorEnEspanol(string json)
    {
        var ex = Assert.Throws<BannerException>(() => SeccionesParser.Parse(json));
        Assert.Contains("Banner", ex.Message);
    }

    // ── Piezas sueltas ────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("0801", true, 8, 0)]
    [InlineData("1701", true, 17, 0)]
    [InlineData("2001", true, 20, 0)]
    [InlineData("0830", true, 8, 30)]        // una hora que no termina en 1 se respeta
    [InlineData("1000", false, 10, 0)]
    [InlineData("2100", false, 21, 0)]
    public void NormalizaLasHorasDeBanner(string texto, bool inicio, int hora, int minuto) =>
        Assert.Equal(new TimeOnly(hora, minuto), SeccionesParser.Hora(texto, inicio));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("8:00")]
    [InlineData("2560")]
    public void HorasInvalidasDanNulo(string? texto) => Assert.Null(SeccionesParser.Hora(texto, true));

    [Theory]
    [InlineData("13-Dic-2026", 2026, 12, 13)]
    [InlineData("01-Sep-2026", 2026, 9, 1)]
    [InlineData("01-Ene-2027", 2027, 1, 1)]
    [InlineData("28-Abr-2027", 2027, 4, 28)]
    [InlineData("15-Aug-2026", 2026, 8, 15)]
    [InlineData("15-AGO-2026", 2026, 8, 15)]
    public void LeeFechasConMesesEnEspanolOIngles(string texto, int a, int m, int d) =>
        Assert.Equal(new DateOnly(a, m, d), SeccionesParser.Fecha(texto));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("31-Feb-2026")]
    [InlineData("13/12/2026")]
    [InlineData("13-Xyz-2026")]
    public void FechasInvalidasDanNulo(string? texto) => Assert.Null(SeccionesParser.Fecha(texto));
}

/// <summary>Cómo se traducen períodos y materias del pénsum a lo que entiende Banner.</summary>
public class MapeoBannerTests
{
    [Theory]
    [InlineData("ENE-ABR 2027", "202710")]
    [InlineData("MAY-AGO 2026", "202620")]
    [InlineData("SEP-DIC 2026", "202630")]
    public void ElCodigoDeBannerSaleDelPeriodo(string periodo, string codigo) =>
        Assert.Equal(codigo, MapeoBanner.CodigoDePeriodo(PeriodoAcademico.Parse(periodo)));

    [Theory]
    [InlineData("202710", "ENE-ABR 2027")]
    [InlineData("202620", "MAY-AGO 2026")]
    [InlineData("202630", "SEP-DIC 2026")]
    public void ElPeriodoSaleDelCodigoDeBanner(string codigo, string esperado)
    {
        Assert.True(MapeoBanner.TryPeriodoDeCodigo(codigo, out var p));
        Assert.Equal(esperado, p.Nombre);
    }

    [Theory]
    [InlineData("202635")]   // Posgrado
    [InlineData("202670")]   // Educación Continuada
    [InlineData("2026")]
    [InlineData("")]
    [InlineData(null)]
    public void LosCodigosQueNoSonDeGradoNoSeTraducen(string? codigo) => Assert.False(MapeoBanner.TryPeriodoDeCodigo(codigo, out _));

    [Fact]
    public void ElViajeDePeriodoACodigoYVueltaEsExacto()
    {
        var p = new PeriodoAcademico(2026, 0);
        for (var i = 0; i < 9; i++, p = p.Siguiente())
        {
            Assert.True(MapeoBanner.TryPeriodoDeCodigo(MapeoBanner.CodigoDePeriodo(p), out var vuelta));
            Assert.Equal(p, vuelta);
        }
    }

    [Fact]
    public void LosPeriodosAnterioresVanDelMasRecienteAlMasAntiguoYCruzanElAnio()
    {
        var anteriores = MapeoBanner.Anteriores(PeriodoAcademico.Parse("ENE-ABR 2027"), 3).Select(p => p.Nombre).ToList();
        Assert.Equal(new[] { "SEP-DIC 2026", "MAY-AGO 2026", "ENE-ABR 2026" }, anteriores);
    }

    [Theory]
    [InlineData("ISO625", "ISO", "625")]
    [InlineData("iso725", "ISO", "725")]
    [InlineData(" MAT110 ", "MAT", "110")]
    public void UnaMateriaNormalEsUnaSolaConsulta(string codigo, string materia, string curso)
    {
        var (consultas, motivo) = MapeoBanner.ConsultasDe(codigo);
        Assert.Null(motivo);
        Assert.Equal(new ConsultaBanner(materia, curso), Assert.Single(consultas));
    }

    [Fact]
    public void UnaElectivaSeConsultaPorTodasSusOpciones()
    {
        var (consultas, _) = MapeoBanner.ConsultasDe("E077");
        Assert.Equal(new[] { "ADM103", "ADM536", "ADM540" }, consultas.Select(c => c.Codigo));
    }

    [Fact]
    public void ElDeporteSeConsultaPorTodaLaMateriaDepDeBanner()
    {
        var (consultas, _) = MapeoBanner.ConsultasDe("ODEP");
        Assert.Equal(new ConsultaBanner("DEP", null), Assert.Single(consultas));
    }

    [Theory]
    [InlineData("TFG")]
    [InlineData("PAS261")]
    [InlineData("")]
    [InlineData("???")]
    [InlineData(null)]
    public void LoQueNoTieneHorarioNoSeConsultaYExplicaPorQue(string? codigo)
    {
        var (consultas, motivo) = MapeoBanner.ConsultasDe(codigo);
        Assert.Empty(consultas);
        Assert.False(string.IsNullOrWhiteSpace(motivo));
    }
}
