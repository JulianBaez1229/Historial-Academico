using System.Text.Json;
using HistorialAcademico.Banner;
using HistorialAcademico.Core.Entities;
using HistorialAcademico.Core.Horarios;
using HistorialAcademico.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace HistorialAcademico.Tests;

/// <summary>Datos de prueba para las secciones guardadas.</summary>
internal static class SeccionesDePrueba
{
    public static BloqueBanner Bloque(DiasSemana dias, string inicio, string fin) =>
        new() { Dias = dias, Inicio = TimeOnly.Parse(inicio), Fin = TimeOnly.Parse(fin), Edificio = "EDIF-01", Aula = "3" };

    public static SeccionOfertada Fila(string periodo, string nrc, string codigo, string seccion, string profesor, params BloqueBanner[] bloques) => new()
    {
        Periodo = periodo, Nrc = nrc, Codigo = codigo, Seccion = seccion, Titulo = "MATERIA " + codigo, Creditos = 3, Campus = "CAMPUS - PRUEBA",
        Metodo = "TEORIA", Profesor = profesor, CupoMaximo = 30, Inscritos = 10, CuposDisponibles = 20, Abierta = true, Consultada = DateTime.UtcNow,
        BloquesJson = JsonSerializer.Serialize(bloques),
    };

    public static async Task SembrarAsync(BdPrueba bd, params SeccionOfertada[] filas)
    {
        bd.Db.SeccionesOfertadas.AddRange(filas);
        await bd.Db.SaveChangesAsync();
    }
}

/// <summary>Horarios tentativos, horas no disponibles y escenarios: reglas del servicio con una base real en memoria.</summary>
public class HorarioTentativoServiceTests : IDisposable
{
    private const DiasSemana MJ = DiasSemana.Martes | DiasSemana.Jueves;
    private const string P = "202710";

    private readonly BdPrueba _bd = new();
    private readonly HorarioTentativoService _s;

    public HorarioTentativoServiceTests() => _s = new HorarioTentativoService(_bd.Db);

    public void Dispose() => _bd.Dispose();

    private async Task<int> HorarioAsync(string nombre = "Mi horario", string periodo = P)
    {
        var r = await _s.CrearAsync(nombre, periodo);
        Assert.True(r.Ok, r.Mensaje);
        return r.Id!.Value;
    }

    /// <summary>ISO625 (dos secciones, una choca con ISO800-1) e ISO800 (dos secciones).</summary>
    private Task SembrarAsync() => SeccionesDePrueba.SembrarAsync(_bd,
        SeccionesDePrueba.Fila(P, "1001", "ISO625", "1", "Pedro Uno", SeccionesDePrueba.Bloque(MJ, "08:00", "10:00")),
        SeccionesDePrueba.Fila(P, "1002", "ISO625", "2", "Marta Dos", SeccionesDePrueba.Bloque(DiasSemana.Lunes | DiasSemana.Miercoles, "18:00", "20:00")),
        SeccionesDePrueba.Fila(P, "2001", "ISO800", "1", "Juan Tres", SeccionesDePrueba.Bloque(DiasSemana.Jueves, "09:00", "11:00")),
        SeccionesDePrueba.Fila(P, "2002", "ISO800", "2", "", SeccionesDePrueba.Bloque(DiasSemana.Sabado, "08:00", "12:00")),
        SeccionesDePrueba.Fila("202720", "9001", "ISO900", "1", "Otro", SeccionesDePrueba.Bloque(DiasSemana.Lunes, "08:00", "10:00")));   // de otro período

    // ── Crear y gestionar ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreaUnHorarioParaUnPeriodoDeGrado()
    {
        var r = await _s.CrearAsync("  Trabajo en las tardes ", P);

        Assert.True(r.Ok);
        Assert.Contains("ENE-ABR 2027", r.Mensaje);
        var h = Assert.Single(await _s.ListarAsync());
        Assert.Equal(("Trabajo en las tardes", P, (int?)null), (h.Nombre, h.Periodo, h.PlanEstudioId));
    }

    [Theory]
    [InlineData("", "202710", "Escribe un nombre")]
    [InlineData("   ", "202710", "Escribe un nombre")]
    [InlineData(null, "202710", "Escribe un nombre")]
    [InlineData("Uno", "202735", "período de Grado")]     // Posgrado
    [InlineData("Uno", "", "período de Grado")]
    public async Task NoCreaHorariosInvalidos(string? nombre, string periodo, string esperado)
    {
        var r = await _s.CrearAsync(nombre, periodo);

        Assert.False(r.Ok);
        Assert.Contains(esperado, r.Mensaje);
        Assert.Empty(await _s.ListarAsync());
    }

    [Fact]
    public async Task ElNombreNoSePuedeRepetirNiPasarDeSesentaCaracteres()
    {
        await HorarioAsync("Mi horario");

        var repetido = await _s.CrearAsync("MI HORARIO", P);
        var largo = await _s.CrearAsync(new string('x', 61), P);

        Assert.False(repetido.Ok);
        Assert.Contains("Ya existe", repetido.Mensaje);
        Assert.False(largo.Ok);
        Assert.Contains("60 caracteres", largo.Mensaje);
    }

    [Fact]
    public async Task RenombraYValidaElNuevoNombre()
    {
        var a = await HorarioAsync("A");
        await HorarioAsync("B");

        Assert.True((await _s.RenombrarAsync(a, "Nuevo")).Ok);
        Assert.False((await _s.RenombrarAsync(a, "b")).Ok);          // ya existe «B»
        Assert.True((await _s.RenombrarAsync(a, "NUEVO")).Ok);       // el mismo horario puede cambiar solo las mayúsculas
        Assert.False((await _s.RenombrarAsync(999, "X")).Ok);
    }

    [Fact]
    public async Task EliminarUnHorarioBorraTambienSusSecciones()
    {
        await SembrarAsync();
        var id = await HorarioAsync();
        await _s.AgregarAsync(id, "1001");

        var r = await _s.EliminarAsync(id);

        Assert.True(r.Ok);
        Assert.Empty(await _s.ListarAsync());
        Assert.Empty(await _bd.Db.SeccionesElegidas.ToListAsync());
        Assert.Equal(5, await _bd.Db.SeccionesOfertadas.CountAsync());   // las secciones consultadas no se tocan
        Assert.False((await _s.EliminarAsync(id)).Ok);
    }

    // ── Elegir secciones ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AgregaUnaSeccionConsultadaDeEsePeriodo()
    {
        await SembrarAsync();
        var id = await HorarioAsync();

        var r = await _s.AgregarAsync(id, "1001");

        Assert.True(r.Ok);
        Assert.Equal("Agregada ISO625-1.", r.Mensaje);
        var elegida = Assert.Single((await _s.ObtenerAsync(id))!.Elegidas);
        Assert.Equal(("1001", "ISO625"), (elegida.Nrc, elegida.Codigo));
    }

    [Theory]
    [InlineData("9999")]     // no existe
    [InlineData("9001")]     // existe, pero es de otro período
    [InlineData("")]
    [InlineData(null)]
    public async Task NoAgregaUnaSeccionQueNoEstaConsultadaEnElPeriodoDelHorario(string? nrc)
    {
        await SembrarAsync();
        var id = await HorarioAsync();

        var r = await _s.AgregarAsync(id, nrc);

        Assert.False(r.Ok);
        Assert.Contains("no está entre las consultadas", r.Mensaje);
        Assert.Empty((await _s.ObtenerAsync(id))!.Elegidas);
    }

    [Fact]
    public async Task ElegirOtraSeccionDeLaMismaMateriaLaReemplaza()
    {
        await SembrarAsync();
        var id = await HorarioAsync();
        await _s.AgregarAsync(id, "1001");

        var r = await _s.AgregarAsync(id, "1002");

        Assert.Equal("Reemplazada ISO625-1 por ISO625-2.", r.Mensaje);
        Assert.Equal(new[] { "1002" }, (await _s.ObtenerAsync(id))!.Elegidas.Select(e => e.Nrc));
        Assert.Equal(1, await _bd.Db.SeccionesElegidas.CountAsync());
    }

    [Fact]
    public async Task AgregarLaMismaSeccionDosVecesNoDuplica()
    {
        await SembrarAsync();
        var id = await HorarioAsync();
        await _s.AgregarAsync(id, "1001");

        var r = await _s.AgregarAsync(id, "1001");

        Assert.True(r.Ok);
        Assert.Contains("ya estaba", r.Mensaje);
        Assert.Equal(1, await _bd.Db.SeccionesElegidas.CountAsync());
    }

    [Fact]
    public async Task QuitaUnaSeccionYAvisaSiYaNoEstaba()
    {
        await SembrarAsync();
        var id = await HorarioAsync();
        await _s.AgregarAsync(id, "1001");

        Assert.True((await _s.QuitarAsync(id, "1001")).Ok);
        Assert.False((await _s.QuitarAsync(id, "1001")).Ok);
        Assert.Empty((await _s.ObtenerAsync(id))!.Elegidas);
    }

    [Fact]
    public async Task ElHorarioSumaCreditosYDetectaLosChoques()
    {
        await SembrarAsync();
        var id = await HorarioAsync();
        await _s.AgregarAsync(id, "1001");      // ISO625-1: martes y jueves 8-10
        await _s.AgregarAsync(id, "2001");      // ISO800-1: jueves 9-11 → choca con la anterior

        var v = (await _s.ObtenerAsync(id))!;

        Assert.Equal(6m, v.Creditos);
        Assert.True(v.HayChoques);
        var choque = Assert.Single(v.Grilla.Choques);
        Assert.Equal((DiasSemana.Jueves, new TimeOnly(9, 0), new TimeOnly(10, 0)), (choque.Dias, choque.Desde, choque.Hasta));
        // Ambas secciones se marcan completas (también el martes de ISO625-1), como pide la historia: «ambas se marcan en rojo».
        Assert.All(v.Grilla.Celdas.Where(c => c.Tipo == TipoCelda.Clase), c => Assert.True(c.ConChoque));
        Assert.Equal(3, v.Grilla.Celdas.Count(c => c.Tipo == TipoCelda.Clase));   // ISO625: martes y jueves; ISO800: jueves
    }

    [Fact]
    public async Task ElegirLaOtraSeccionQuitaElChoque()
    {
        await SembrarAsync();
        var id = await HorarioAsync();
        await _s.AgregarAsync(id, "1001");
        await _s.AgregarAsync(id, "2001");

        await _s.AgregarAsync(id, "2002");   // ISO800 en sábado

        Assert.False((await _s.ObtenerAsync(id))!.HayChoques);
    }

    // ── Opciones y horas no disponibles ───────────────────────────────────────────────────

    [Fact]
    public async Task LasOpcionesSoloTraenLoDeEsePeriodoAgrupadoPorMateria()
    {
        await SembrarAsync();
        var id = await HorarioAsync();

        var v = (await _s.ObtenerAsync(id))!;

        Assert.Equal(new[] { "ISO625", "ISO800" }, v.Opciones.Select(g => g.Codigo));   // ISO900 es de otro período
        Assert.Equal(new[] { 2, 2 }, v.Opciones.Select(g => g.Secciones.Count));
        Assert.Contains("Materia", v.Opciones[0].Nombre);    // sin pénsum cargado usa el título de Banner (arreglado)
    }

    [Fact]
    public async Task LasSeccionesQueChocanConMisHorasNoDisponiblesVanAlFinalDeSuMateria()
    {
        await SembrarAsync();
        await _s.GuardarNoDisponiblesAsync(new[] { (DiasSemana.Martes, 8), (DiasSemana.Martes, 9) });   // ISO625-1 cae ahí
        var id = await HorarioAsync();

        var iso625 = (await _s.ObtenerAsync(id))!.Opciones.First(g => g.Codigo == "ISO625");

        Assert.Equal(new[] { "2", "1" }, iso625.Secciones.Select(o => o.Seccion.Seccion));   // la compatible primero
        Assert.Equal(new[] { false, true }, iso625.Secciones.Select(o => o.ChocaConNoDisponible));
    }

    [Fact]
    public async Task SinHorasNoDisponiblesLasSeccionesVanEnSuOrdenNormal()
    {
        await SembrarAsync();
        var id = await HorarioAsync();

        var iso625 = (await _s.ObtenerAsync(id))!.Opciones.First(g => g.Codigo == "ISO625");

        Assert.Equal(new[] { "1", "2" }, iso625.Secciones.Select(o => o.Seccion.Seccion));
        Assert.All(iso625.Secciones, o => Assert.False(o.ChocaConNoDisponible));
    }

    [Fact]
    public async Task UnaOpcionAvisaConQueSeccionElegidaChoca()
    {
        await SembrarAsync();
        var id = await HorarioAsync();
        await _s.AgregarAsync(id, "1001");

        var iso800 = (await _s.ObtenerAsync(id))!.Opciones.First(g => g.Codigo == "ISO800");

        Assert.Equal(new[] { "ISO625-1" }, iso800.Secciones.Single(o => o.Seccion.Nrc == "2001").ChocaCon);
        Assert.Empty(iso800.Secciones.Single(o => o.Seccion.Nrc == "2002").ChocaCon);
    }

    [Fact]
    public async Task UnaSeccionElegidaYaNoCuentaComoChoqueConSuPropiaMateria()
    {
        await SembrarAsync();
        var id = await HorarioAsync();
        await _s.AgregarAsync(id, "1001");

        var iso625 = (await _s.ObtenerAsync(id))!.Opciones.First(g => g.Codigo == "ISO625");

        Assert.All(iso625.Secciones, o => Assert.Empty(o.ChocaCon));   // cambiar de sección no choca con la que se reemplaza
        Assert.True(iso625.TieneElegida);
        Assert.True(iso625.Secciones.Single(o => o.Seccion.Nrc == "1001").Elegida);
    }

    [Fact]
    public async Task UnaSeccionElegidaQueDejoDeApareceSeListaComoPerdida()
    {
        await SembrarAsync();
        var id = await HorarioAsync();
        await _s.AgregarAsync(id, "1001");
        _bd.Db.SeccionesOfertadas.Remove(await _bd.Db.SeccionesOfertadas.FirstAsync(x => x.Nrc == "1001"));   // al volver a consultar, Banner ya no la trae
        await _bd.Db.SaveChangesAsync();

        var v = (await _s.ObtenerAsync(id))!;

        Assert.Empty(v.Elegidas);
        var perdida = Assert.Single(v.Perdidas);
        Assert.Equal(("1001", "ISO625-1"), (perdida.Nrc, perdida.Etiqueta));
        Assert.Equal(0m, v.Creditos);
    }

    [Fact]
    public async Task ObtenerUnHorarioQueNoExisteDaNulo() => Assert.Null(await _s.ObtenerAsync(404));

    // ── Escenarios del planificador ───────────────────────────────────────────────────────

    private async Task<int> PlanAsync(string nombre)
    {
        var plan = new PlanEstudio { Nombre = nombre, Creado = DateTime.UtcNow, Actualizado = DateTime.UtcNow };
        _bd.Db.PlanesEstudio.Add(plan);
        await _bd.Db.SaveChangesAsync();
        return plan.Id;
    }

    [Fact]
    public async Task AsociaElHorarioAUnEscenarioYLoDesasocia()
    {
        var plan = await PlanAsync("Plan 1");
        var id = await HorarioAsync();

        Assert.True((await _s.AsociarPlanAsync(id, plan)).Ok);
        Assert.Equal("Plan 1", (await _s.ObtenerAsync(id))!.PlanNombre);
        Assert.Equal(id, Assert.Single(await _s.DelPlanAsync(plan)).Id);
        Assert.Contains(await _s.PlanesAsync(), p => p == (plan, "Plan 1"));

        Assert.True((await _s.AsociarPlanAsync(id, null)).Ok);
        Assert.Null((await _s.ObtenerAsync(id))!.PlanNombre);
        Assert.Empty(await _s.DelPlanAsync(plan));
    }

    [Fact]
    public async Task NoAsociaAUnEscenarioOUnHorarioQueNoExisten()
    {
        var id = await HorarioAsync();

        Assert.False((await _s.AsociarPlanAsync(id, 999)).Ok);
        Assert.False((await _s.AsociarPlanAsync(404, null)).Ok);
    }

    [Fact]
    public async Task SiSeBorraElEscenarioElHorarioSeConservaSinAsociar()
    {
        var plan = await PlanAsync("Plan B");
        var id = await HorarioAsync();
        await _s.AsociarPlanAsync(id, plan);

        _bd.Db.PlanesEstudio.Remove(await _bd.Db.PlanesEstudio.FirstAsync(p => p.Id == plan));
        await _bd.Db.SaveChangesAsync();
        _bd.Db.ChangeTracker.Clear();

        var v = (await _s.ObtenerAsync(id))!;
        Assert.Null(v.Horario.PlanEstudioId);
        Assert.Null(v.PlanNombre);
    }

    // ── Horas no disponibles: celdas y franjas ────────────────────────────────────────────

    [Fact]
    public void LasHorasSeguidasDeUnDiaSeJuntanEnUnaSolaFranja()
    {
        var franjas = HorarioTentativoService.Franjas(new[]
        {
            (DiasSemana.Lunes, 8), (DiasSemana.Lunes, 9), (DiasSemana.Lunes, 10), (DiasSemana.Lunes, 14), (DiasSemana.Martes, 8), (DiasSemana.Lunes, 9),
        });

        Assert.Equal(new[] { (DiasSemana.Lunes, 480, 660), (DiasSemana.Lunes, 840, 900), (DiasSemana.Martes, 480, 540) },
            franjas.Select(f => (f.Dia, f.DesdeMin, f.HastaMin)));
    }

    [Fact]
    public void LasCeldasSonLasHorasQueCubreCadaFranja()
    {
        var celdas = HorarioTentativoService.Celdas(new[]
        {
            new BloqueNoDisponible { Dia = DiasSemana.Lunes, DesdeMin = 480, HastaMin = 660 },
            new BloqueNoDisponible { Dia = DiasSemana.Viernes, DesdeMin = 840, HastaMin = 900 },
        });

        Assert.Equal(new HashSet<(DiasSemana, int)> { (DiasSemana.Lunes, 8), (DiasSemana.Lunes, 9), (DiasSemana.Lunes, 10), (DiasSemana.Viernes, 14) }, celdas);
    }

    [Fact]
    public async Task GuardarNoDisponiblesReemplazaTodoYSePuedeVaciar()
    {
        var laboral = Enumerable.Range(8, 9).Select(h => (DiasSemana.Lunes, h)).ToList();   // lunes 8:00 a 17:00

        var r = await _s.GuardarNoDisponiblesAsync(laboral);
        var franja = Assert.Single(await _s.NoDisponiblesAsync());
        Assert.True(r.Ok);
        Assert.Equal((DiasSemana.Lunes, 480, 1020), (franja.Dia, franja.DesdeMin, franja.HastaMin));

        await _s.GuardarNoDisponiblesAsync(new[] { (DiasSemana.Sabado, 9) });
        Assert.Equal(new[] { (DiasSemana.Sabado, 540) }, (await _s.NoDisponiblesAsync()).Select(b => (b.Dia, b.DesdeMin)));   // reemplazó, no sumó

        var vacio = await _s.GuardarNoDisponiblesAsync(Array.Empty<(DiasSemana, int)>());
        Assert.Contains("Ya no tienes", vacio.Mensaje);
        Assert.Empty(await _s.NoDisponiblesAsync());
    }

    [Fact]
    public async Task GuardarNoDisponiblesIgnoraHorasYDiasInvalidos()
    {
        await _s.GuardarNoDisponiblesAsync(new[]
        {
            (DiasSemana.Lunes, -1), (DiasSemana.Lunes, 24), (DiasSemana.Ninguno, 9),
            (DiasSemana.Lunes | DiasSemana.Martes, 9),        // un solo día por celda
            (DiasSemana.Jueves, 9),                            // la única válida
        });

        var unica = Assert.Single(await _s.NoDisponiblesAsync());
        Assert.Equal((DiasSemana.Jueves, 540, 600), (unica.Dia, unica.DesdeMin, unica.HastaMin));
    }
}

/// <summary>Materias que necesito que abran: lista, notas y texto para el correo.</summary>
public class AperturaServiceTests : IDisposable
{
    private readonly BdPrueba _bd = new();
    private readonly AperturaService _s;

    public AperturaServiceTests()
    {
        _bd.Db.MateriasPensum.AddRange(DatosLab.Pensum());
        _bd.Db.SaveChanges();
        _s = new AperturaService(_bd.Db);
    }

    public void Dispose() => _bd.Dispose();

    [Fact]
    public async Task MarcaUnaMateriaDelPensumConSuNota()
    {
        var r = await _s.MarcarAsync("iso725", "  la necesito para graduarme ");

        Assert.True(r.Ok);
        var item = Assert.Single(await _s.ListarAsync());
        Assert.Equal(("ISO725", "la necesito para graduarme"), (item.Codigo, item.Nota));
        Assert.False(string.IsNullOrEmpty(item.Nombre) || item.Nombre == "ISO725");   // trae el nombre del pénsum
        Assert.True(item.Creditos > 0);
    }

    [Fact]
    public async Task MarcarDeNuevoActualizaLaNotaSinDuplicar()
    {
        await _s.MarcarAsync("ISO725", "uno");

        var r = await _s.MarcarAsync("ISO725", "dos");

        Assert.Contains("Nota de ISO725 actualizada", r.Mensaje);
        Assert.Equal("dos", Assert.Single(await _s.ListarAsync()).Nota);
    }

    [Theory]
    [InlineData("XYZ999")]
    [InlineData("")]
    [InlineData(null)]
    public async Task NoMarcaMateriasQueNoEstanEnElPensum(string? codigo)
    {
        var r = await _s.MarcarAsync(codigo, "x");

        Assert.False(r.Ok);
        Assert.Empty(await _s.ListarAsync());
    }

    [Fact]
    public async Task LaNotaTieneUnLimite()
    {
        var r = await _s.MarcarAsync("ISO725", new string('n', AperturaService.MaxNota + 1));

        Assert.False(r.Ok);
        Assert.Contains("300 caracteres", r.Mensaje);
    }

    [Fact]
    public async Task QuitaUnaMateriaYAvisaSiNoEstaba()
    {
        await _s.MarcarAsync("ISO725", "");

        Assert.True((await _s.QuitarAsync("iso725")).Ok);
        Assert.False((await _s.QuitarAsync("ISO725")).Ok);
        Assert.Empty(await _s.MarcadasAsync());
    }

    [Fact]
    public async Task ElTextoParaElCorreoTraeLasMateriasNumeradasConSusNotas()
    {
        _bd.Db.DatosAlumno.Add(new DatosAlumno { Nombre = "Estudiante de Prueba", Programa = "Ingeniería de Software" });
        await _bd.Db.SaveChangesAsync();
        await _s.MarcarAsync("ISO725", "es de las últimas");
        await _s.MarcarAsync("ISO800", "");

        var texto = await _s.TextoAsync("ENE-ABR 2027");

        Assert.StartsWith("Solicitud de apertura de secciones – ENE-ABR 2027", texto);
        Assert.Contains("Estudiante: Estudiante de Prueba", texto);
        Assert.Contains("Programa: Ingeniería de Software", texto);
        Assert.Contains("las siguientes materias", texto);
        Assert.Contains("1. ISO725 – ", texto);
        Assert.Contains("   Nota: es de las últimas", texto);
        Assert.Contains("2. ISO800 – ", texto);
        Assert.Contains("créditos)", texto);
        Assert.DoesNotContain("Nota: \n", texto);          // sin nota, no hay línea de nota
        Assert.EndsWith("Gracias." + Environment.NewLine, texto);
    }

    [Fact]
    public async Task ElTextoConUnaSolaMateriaVaEnSingular()
    {
        await _s.MarcarAsync("ISO725", "");

        Assert.Contains("de la siguiente materia", await _s.TextoAsync("ENE-ABR 2027"));
    }

    [Fact]
    public async Task SinMateriasMarcadasElTextoLoDice()
    {
        var texto = await _s.TextoAsync("ENE-ABR 2027");

        Assert.Contains("No hay materias marcadas", texto);
        Assert.DoesNotContain("Gracias", texto);
    }

    [Fact]
    public async Task ElTextoNoLlevaMatriculaNiDatosDeTerceros()
    {
        _bd.Db.DatosAlumno.Add(new DatosAlumno { Nombre = "Estudiante de Prueba", Programa = "Programa", FechaNacimiento = new DateOnly(1990, 1, 1) });
        await _bd.Db.SaveChangesAsync();
        await SeccionesDePrueba.SembrarAsync(_bd, SeccionesDePrueba.Fila("202710", "1", "ISO725", "1", "Profesor Ajeno Uno"));
        await _s.MarcarAsync("ISO725", "");

        var texto = await _s.TextoAsync("ENE-ABR 2027");

        Assert.DoesNotContain("1990", texto);
        Assert.DoesNotContain("Profesor Ajeno", texto);
        Assert.DoesNotContain("@", texto);
    }
}

/// <summary>Historial de profesores por materia, a partir de las secciones ya consultadas.</summary>
public class ProfesoresDeMateriaTests : IDisposable
{
    private readonly BdPrueba _bd = new();
    private readonly HorariosService _s;

    public ProfesoresDeMateriaTests() => _s = new HorariosService(_bd.Db, new BannerFalso());

    public void Dispose() => _bd.Dispose();

    [Fact]
    public async Task ListaLosProfesoresConSusPeriodosDelMasRecienteAlMasAntiguo()
    {
        await SeccionesDePrueba.SembrarAsync(_bd,
            SeccionesDePrueba.Fila("202530", "1", "ISO725", "1", "Ana Vieja"),
            SeccionesDePrueba.Fila("202610", "2", "ISO725", "1", "Pedro Reciente"),
            SeccionesDePrueba.Fila("202610", "3", "ISO725", "2", "Ana Vieja"),
            SeccionesDePrueba.Fila("202510", "4", "ISO725", "1", "Ana Vieja"));

        var lista = await _s.ProfesoresDeAsync("ISO725");

        Assert.Equal(new[] { "Ana Vieja", "Pedro Reciente" }, lista.Select(p => p.Nombre).OrderBy(x => x));
        Assert.Equal(new[] { "ENE-ABR 2026", "SEP-DIC 2025", "ENE-ABR 2025" }, lista.Single(p => p.Nombre == "Ana Vieja").Periodos);
        Assert.Equal(new[] { "ENE-ABR 2026" }, lista.Single(p => p.Nombre == "Pedro Reciente").Periodos);
        Assert.Equal(new[] { "Ana Vieja", "Pedro Reciente" }, lista.Select(p => p.Nombre));   // los del mismo último período, por nombre
    }

    [Fact]
    public async Task UnaSeccionConVariosProfesoresLosSeparaYNoContaLosVacios()
    {
        await SeccionesDePrueba.SembrarAsync(_bd,
            SeccionesDePrueba.Fila("202610", "1", "ISO725", "1", "Ana Uno; Beto Dos"),
            SeccionesDePrueba.Fila("202610", "2", "ISO725", "2", ""));

        var lista = await _s.ProfesoresDeAsync("ISO725");

        Assert.Equal(new[] { "Ana Uno", "Beto Dos" }, lista.Select(p => p.Nombre));
    }

    [Fact]
    public async Task SoloConsideraLaMateriaPedidaYLasElectivasPorSusOpciones()
    {
        await SeccionesDePrueba.SembrarAsync(_bd,
            SeccionesDePrueba.Fila("202610", "1", "ISO725", "1", "De Otra Materia"),
            SeccionesDePrueba.Fila("202610", "2", "ADM103", "1", "De La Electiva"),
            SeccionesDePrueba.Fila("202610", "3", "DEP101", "1", "Del Deporte"));

        Assert.Equal(new[] { "De La Electiva" }, (await _s.ProfesoresDeAsync("E077")).Select(p => p.Nombre));
        Assert.Equal(new[] { "Del Deporte" }, (await _s.ProfesoresDeAsync("ODEP")).Select(p => p.Nombre));
        Assert.Equal(new[] { "De Otra Materia" }, (await _s.ProfesoresDeAsync("ISO725")).Select(p => p.Nombre));
    }

    [Theory]
    [InlineData("TFG")]
    [InlineData("PAS261")]
    [InlineData("")]
    [InlineData("ISO999")]
    public async Task LoQueNoTieneHistorialDaListaVacia(string codigo) => Assert.Empty(await _s.ProfesoresDeAsync(codigo));

    [Fact]
    public void ElHistorialSoloTieneNombresYPeriodosNadaDeOpinionesNiCalificaciones()
    {
        var propiedades = typeof(ProfesorDeMateria).GetProperties().Select(p => p.Name).ToList();

        Assert.Equal(new[] { "Nombre", "Periodos" }, propiedades);
    }
}
