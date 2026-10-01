using HistorialAcademico.Banner;
using HistorialAcademico.Core.Entities;
using HistorialAcademico.Core.Horarios;
using HistorialAcademico.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace HistorialAcademico.Tests;

/// <summary>
/// Consulta y guardado de secciones (E01-B) con una base real en memoria y un Banner falso.
/// Van en la colección «consultas a Banner» porque el candado de una consulta a la vez es estático: en paralelo se estorbarían.
/// </summary>
[Collection(ColeccionConsultas.Nombre)]
public class HorariosServiceTests : IDisposable
{
    private static readonly string FixtureJson = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "secciones-anonimizadas.json"));
    private const string P = "209930";   // el período del fixture anonimizado

    private readonly BdPrueba _bd = new();
    private readonly BannerFalso _banner = new();
    private readonly HorariosService _servicio;

    public HorariosServiceTests()
    {
        _servicio = new HorariosService(_bd.Db, _banner);
        // Por defecto responde con las secciones del fixture que correspondan a lo consultado (como haría Banner).
        _banner.Secciones = (periodo, consultas) => Task.FromResult(Filtrar(SeccionesParser.Parse(FixtureJson), consultas));
    }

    public void Dispose() => _bd.Dispose();

    private static bool Coincide(ConsultaBanner c, string codigo) => c.Curso is null ? codigo.StartsWith(c.Materia) : codigo == c.Codigo;

    private static ResultadoBusqueda Filtrar(ResultadoBusqueda todo, IReadOnlyList<ConsultaBanner> consultas)
    {
        var secciones = todo.Secciones.Where(s => consultas.Any(c => Coincide(c, s.Codigo))).ToList();
        return new ResultadoBusqueda { Total = secciones.Count, Secciones = secciones };
    }

    private static SeccionBanner Seccion(string periodo, string nrc, string codigo, params string[] profesores) => new()
    {
        Periodo = periodo, Nrc = nrc, Codigo = codigo, Materia = new string(codigo.TakeWhile(char.IsLetter).ToArray()),
        Curso = new string(codigo.SkipWhile(char.IsLetter).ToArray()), Seccion = "1", Titulo = "PRUEBA", Creditos = 3,
        Profesores = profesores.ToList(), CupoMaximo = 30, Inscritos = 10, CuposDisponibles = 20, Abierta = true,
    };

    private void Responde(params SeccionBanner[] secciones) =>
        _banner.Secciones = (_, _) => Task.FromResult(new ResultadoBusqueda { Total = secciones.Length, Secciones = secciones.ToList() });

    // ── Guardar ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GuardaLasSeccionesConElNombreDelProfesor()
    {
        var r = await _servicio.ConsultarAsync("XYZ100", P, permitirLogin: false);

        Assert.True(r.Exito, r.Mensaje);
        Assert.Contains("2 secciones de XYZ100", r.Mensaje);
        var filas = await _bd.Db.SeccionesOfertadas.AsNoTracking().OrderBy(s => s.Nrc).ToListAsync();
        Assert.Equal(new[] { "90001", "90002" }, filas.Select(f => f.Nrc));
        Assert.Equal("Pedro Ejemplo Prueba Uno", filas[0].Profesor);
        Assert.Equal("Marta Álvarez Ficticia Prueba Dos", filas[1].Profesor);
        Assert.Equal(("DISEÑO DE PRUEBA I", 5m, 30, 29, 1), (filas[0].Titulo, filas[0].Creditos, filas[0].CupoMaximo, filas[0].Inscritos, filas[0].CuposDisponibles));
    }

    [Fact]
    public async Task NoGuardaCorreosNiMatriculasDeProfesoresEnNingunaColumna()
    {
        await _servicio.ConsultarAsync("XYZ100", P, false);
        await _servicio.ConsultarAsync("XYZ300", P, false);

        var todo = await _bd.Db.SeccionesOfertadas.AsNoTracking().ToListAsync();
        Assert.NotEmpty(todo);
        var volcado = string.Join("|", todo.Select(f => System.Text.Json.JsonSerializer.Serialize(f)));
        Assert.DoesNotContain("@", volcado);
        Assert.DoesNotContain("correo.falso", volcado);
        Assert.DoesNotContain("9999001", volcado);
        Assert.DoesNotContain("bannerId", volcado, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("email", volcado, StringComparison.OrdinalIgnoreCase);

        // Y la propia tabla no tiene columnas para eso.
        var columnas = _bd.Db.Model.FindEntityType(typeof(SeccionOfertada))!.GetProperties().Select(p => p.Name).ToList();
        Assert.DoesNotContain(columnas, c => c.Contains("mail", StringComparison.OrdinalIgnoreCase) || c.Contains("Banner", StringComparison.OrdinalIgnoreCase) || c.Contains("Matricula", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ConsultarDeNuevoReemplazaLoGuardadoSinDuplicar()
    {
        await _servicio.ConsultarAsync("XYZ100", P, false);
        Responde(Seccion(P, "90001", "XYZ100", "Pedro Ejemplo Prueba Uno"));   // ahora Banner solo trae una
        await _servicio.ConsultarAsync("XYZ100", P, false);

        Assert.Equal(new[] { "90001" }, await _bd.Db.SeccionesOfertadas.Select(s => s.Nrc).ToListAsync());
        Assert.Equal(1, await _bd.Db.ConsultasSecciones.CountAsync());   // el registro de la consulta se actualiza, no se duplica
    }

    [Fact]
    public async Task ConsultarUnaMateriaNoToca_LasDeOtrasMateriasNiLasDeOtrosPeriodos()
    {
        await _servicio.ConsultarAsync("XYZ100", P, false);
        await _servicio.ConsultarAsync("XYZ300", P, false);
        Responde(Seccion("209920", "80001", "XYZ100", "Otro Profesor"));
        await _servicio.ConsultarAsync("XYZ100", "209920", false);

        var porCodigo = (await _bd.Db.SeccionesOfertadas.ToListAsync()).GroupBy(s => (s.Periodo, s.Codigo)).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(2, porCodigo[(P, "XYZ100")]);
        Assert.Equal(2, porCodigo[(P, "XYZ300")]);
        Assert.Equal(1, porCodigo[("209920", "XYZ100")]);
    }

    [Fact]
    public async Task UnFalloDeBannerNoBorraLoQueYaEstabaGuardado()
    {
        await _servicio.ConsultarAsync("XYZ100", P, false);
        _banner.Secciones = (_, _) => throw new BannerException("Banner no respondió a tiempo.");

        var r = await _servicio.ConsultarAsync("XYZ100", P, false);

        Assert.False(r.Exito);
        Assert.Equal("Banner no respondió a tiempo.", r.Mensaje);
        Assert.Equal(2, await _bd.Db.SeccionesOfertadas.CountAsync());
    }

    // ── Estado vacío ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LaMateriaSinSeccionesEsUnEstadoNormalYQuedaComoConsultada()
    {
        _banner.Secciones = (_, _) => Task.FromResult(new ResultadoBusqueda());

        var antes = await _servicio.VerAsync("ISO725", "202710");
        var r = await _servicio.ConsultarAsync("ISO725", "202710", permitirLogin: false);
        var despues = await _servicio.VerAsync("ISO725", "202710");

        Assert.False(antes.YaConsultada);
        Assert.False(antes.SinSecciones);                                   // «no he consultado» no es «no hay secciones»
        Assert.True(r.Exito);                                              // no es un error
        Assert.Contains("aún no tiene secciones publicadas de ISO725 para ENE-ABR 2027", r.Mensaje);
        Assert.True(despues.YaConsultada);
        Assert.True(despues.SinSecciones);
        Assert.Empty(despues.Secciones);
        Assert.NotNull(despues.Consultada);
    }

    // ── Qué se consulta ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UnaElectivaConsultaTodasSusOpcionesYSeGuardaComoUnaSolaConsultada()
    {
        var r = await _servicio.ConsultarAsync("E077", "202630", false);

        Assert.True(r.Exito);
        Assert.Equal(("202630", "ADM103,ADM536,ADM540"), Assert.Single(_banner.ConsultasSecciones));
        Assert.Equal(3, await _bd.Db.ConsultasSecciones.CountAsync());
        Assert.True((await _servicio.VerAsync("E077", "202630")).YaConsultada);
    }

    [Fact]
    public async Task ElDeporteConsultaToda_LaMateriaDep_YMuestraTodasSusSecciones()
    {
        Responde(Seccion("202630", "70001", "DEP101", "Entrenador Uno"), Seccion("202630", "70002", "DEP205", "Entrenador Dos"));

        await _servicio.ConsultarAsync("ODEP", "202630", false);

        Assert.Equal(("202630", "DEP"), Assert.Single(_banner.ConsultasSecciones));
        var vista = await _servicio.VerAsync("ODEP", "202630");
        Assert.Equal(new[] { "DEP101", "DEP205" }, vista.Secciones.Select(s => s.Codigo));
    }

    [Theory]
    [InlineData("TFG")]
    [InlineData("PAS261")]
    [InlineData("")]
    public async Task LoQueNoSeConsultaPorHorarioNoLlamaABanner(string codigo)
    {
        var r = await _servicio.ConsultarAsync(codigo, "202630", true);

        Assert.False(r.Exito);
        Assert.False(string.IsNullOrWhiteSpace(r.Mensaje));
        Assert.Empty(_banner.ConsultasSecciones);
        Assert.NotNull((await _servicio.VerAsync(codigo, "202630")).NoConsultable);
    }

    [Theory]
    [InlineData("202635")]   // Posgrado
    [InlineData("basura")]
    public async Task UnPeriodoQueNoEsDeGradoNoLlamaABanner(string periodo)
    {
        var r = await _servicio.ConsultarAsync("ISO725", periodo, true);

        Assert.False(r.Exito);
        Assert.Contains("no es de Grado", r.Mensaje);
        Assert.Empty(_banner.ConsultasSecciones);
    }

    // ── Sesión de Banner ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ConLaSesionCaducadaSinPermisoPideIniciarSesionSinAbrirElNavegador()
    {
        _banner.Secciones = (_, _) => throw new BannerSesionExpiradaException();

        var r = await _servicio.ConsultarAsync("XYZ100", P, permitirLogin: false);

        Assert.False(r.Exito);
        Assert.True(r.RequiereLogin);
        Assert.Equal(0, _banner.Logins);
    }

    [Fact]
    public async Task ConLaSesionCaducadaYPermisoInicia_SesionYReintentaUnaVez()
    {
        var llamadas = 0;
        var buenas = _banner.Secciones;
        _banner.Secciones = (p, c) => ++llamadas == 1 ? throw new BannerSesionExpiradaException() : buenas(p, c);

        var r = await _servicio.ConsultarAsync("XYZ100", P, permitirLogin: true);

        Assert.True(r.Exito, r.Mensaje);
        Assert.Equal(1, _banner.Logins);
        Assert.Equal(2, llamadas);
        Assert.Equal(2, await _bd.Db.SeccionesOfertadas.CountAsync());
    }

    [Fact]
    public async Task SiElInicioDeSesionFallaLoDiceYNoReintenta()
    {
        _banner.Secciones = (_, _) => throw new BannerSesionExpiradaException();
        _banner.Login = () => throw new BannerException("Se cerró la ventana de Banner antes de iniciar sesión.");

        var r = await _servicio.ConsultarAsync("XYZ100", P, permitirLogin: true);

        Assert.False(r.Exito);
        Assert.Contains("No se pudo iniciar sesión en Banner: Se cerró la ventana", r.Mensaje);
        Assert.Single(_banner.ConsultasSecciones);   // no volvió a consultar
    }

    [Fact]
    public async Task UnaSesionQueSigueCaducadaTrasElLoginNoEntraEnCiclo()
    {
        _banner.Secciones = (_, _) => throw new BannerSesionExpiradaException();

        var r = await _servicio.ConsultarAsync("XYZ100", P, permitirLogin: true);

        Assert.False(r.Exito);
        Assert.Equal(1, _banner.Logins);                 // un solo intento de login
        Assert.Equal(2, _banner.ConsultasSecciones.Count);   // la consulta y un reintento, nada más
    }

    [Theory]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(TimeoutException))]
    [InlineData(typeof(InvalidOperationException))]
    public async Task UnaExcepcionInesperadaSeConvierteEnMensajeSinRomperLaPantalla(Type tipo)
    {
        _banner.Secciones = (_, _) => throw (Exception)Activator.CreateInstance(tipo, "falló algo")!;

        var r = await _servicio.ConsultarAsync("XYZ100", P, false);

        Assert.False(r.Exito);
        Assert.Contains(tipo.Name, r.Mensaje);
    }

    // ── Lo guardado ───────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LasSeccionesGuardadasConservanHorariosYSeLeenSinIrABanner()
    {
        await _servicio.ConsultarAsync("XYZ100", P, false);
        var llamadasAntes = _banner.ConsultasSecciones.Count;

        var vista = await _servicio.VerAsync("XYZ100", P);

        Assert.Equal(llamadasAntes, _banner.ConsultasSecciones.Count);   // ver no consulta
        var s = vista.Secciones.First(x => x.Nrc == "90001");
        Assert.Equal(new[] { "Pedro Ejemplo Prueba Uno" }, s.Profesores);
        Assert.Equal(DiasSemana.Martes | DiasSemana.Jueves, s.Bloques[0].Dias);
        Assert.Equal(new TimeOnly(8, 0), s.Bloques[0].Inicio);
        Assert.Equal(new TimeOnly(10, 0), s.Bloques[0].Fin);
        Assert.Equal("EDIF-03", s.Bloques[0].Edificio);
        Assert.True(s.Bloques[1].SinDiaFijo);
        Assert.Equal(new DateOnly(2099, 12, 13), s.Bloques[0].FechaFin);
        Assert.Equal("SEP-DIC 2099", vista.PeriodoNombre);
    }

    [Fact]
    public async Task UnaSeccionSinProfesorSeGuardaSinNombre()
    {
        await _servicio.ConsultarAsync("XYZ200", P, false);

        var s = Assert.Single((await _servicio.VerAsync("XYZ200", P)).Secciones);
        Assert.Empty(s.Profesores);
        Assert.Empty(s.Bloques);
    }

    [Fact]
    public async Task LaOfertaPreviaListaSoloLosPeriodosConSeccionesDelMasRecienteAlMasAntiguo()
    {
        Responde(Seccion("202530", "1", "ISO725", "A B"), Seccion("202530", "2", "ISO725", "C D"));
        await _servicio.ConsultarAsync("ISO725", "202530", false);
        _banner.Secciones = (_, _) => Task.FromResult(new ResultadoBusqueda());
        await _servicio.ConsultarAsync("ISO725", "202520", false);          // consultado, pero sin secciones
        Responde(Seccion("202510", "3", "ISO725", "E F"));
        await _servicio.ConsultarAsync("ISO725", "202510", false);

        var previa = await _servicio.OfertaPreviaAsync("ISO725");

        Assert.Equal(new[] { ("202530", "SEP-DIC 2025", 2), ("202510", "ENE-ABR 2025", 1) }, previa.Select(o => (o.Periodo, o.PeriodoNombre, o.Secciones)));
        Assert.Empty(await _servicio.OfertaPreviaAsync("ISO999"));
    }

    [Fact]
    public async Task BuscarAnterioresRevisaSoloLosPeriodosQueFaltanYDiceCuandoSeOfrecio()
    {
        Responde(Seccion("202530", "1", "ISO725", "A B"));
        await _servicio.ConsultarAsync("ISO725", "202530", false);          // este ya estaba consultado
        _banner.ConsultasSecciones.Clear();
        _banner.Secciones = (_, _) => Task.FromResult(new ResultadoBusqueda());

        var r = await _servicio.BuscarAnterioresAsync("ISO725", "202610", false);

        Assert.True(r.Exito, r.Mensaje);
        Assert.Equal(new[] { "202520", "202510" }, _banner.ConsultasSecciones.Select(c => c.Periodo));   // 202530 no se vuelve a preguntar
        Assert.Contains("ISO725 se ofreció en SEP-DIC 2025", r.Mensaje);
    }

    [Fact]
    public async Task BuscarAnterioresSinNadaQueRevisarNoLlamaABannerYSeLoDice()
    {
        _banner.Secciones = (_, _) => Task.FromResult(new ResultadoBusqueda());
        await _servicio.BuscarAnterioresAsync("ISO725", "202610", false);
        _banner.ConsultasSecciones.Clear();

        var r = await _servicio.BuscarAnterioresAsync("ISO725", "202610", false);

        Assert.Empty(_banner.ConsultasSecciones);
        Assert.Contains("Ya tenía consultados", r.Mensaje);
        Assert.Contains("no aparece con secciones", r.Mensaje);
    }
}
