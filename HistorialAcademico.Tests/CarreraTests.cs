using System.Net;
using System.Text.RegularExpressions;
using HistorialAcademico.Core.Entities;
using HistorialAcademico.Core.Pensum;
using HistorialAcademico.Core.Universidad;
using HistorialAcademico.Web.Data;
using HistorialAcademico.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace HistorialAcademico.Tests;

/// <summary>Un catálogo de pénsums temporal: UNAPEC (copiado del repositorio) y una universidad de prueba con dos carreras, una con dos versiones.</summary>
internal sealed class CatalogoTemporal : IDisposable
{
    public string Carpeta { get; } = Path.Combine(Path.GetTempPath(), "catalogo-" + Guid.NewGuid().ToString("N"), "pensums");

    public CatalogoTemporal()
    {
        Directory.CreateDirectory(Path.Combine(Carpeta, "unapec"));
        File.Copy(Path.Combine(PensumEjemplo.CarpetaPensums, "unapec", "universidad.json"), Path.Combine(Carpeta, "unapec", "universidad.json"));
        File.Copy(PensumEjemplo.Archivo, Path.Combine(Carpeta, "unapec", "ingenieria-software-11.json"));

        Escribir("uni-prueba", "universidad.json", UniversidadDePrueba.Json);
        Escribir("uni-prueba", "derecho-2019.json", Pensum("derecho", "Derecho", "2019", ("AAA100", 3, 1, ""), ("BBB200", 5, 2, "AAA100")));
        Escribir("uni-prueba", "derecho-2023.json", Pensum("derecho", "Derecho", "2023", ("CCC100", 3, 1, ""), ("DDD200", 4, 2, "CCC100"), ("EEE300", 3, 2, "")));
        Escribir("uni-prueba", "administracion-2020.json", Pensum("administracion", "Administración de Empresas", "2020", ("FFF100", 6, 1, "")));
    }

    public void Escribir(string universidad, string archivo, string contenido)
    {
        Directory.CreateDirectory(Path.Combine(Carpeta, universidad));
        File.WriteAllText(Path.Combine(Carpeta, universidad, archivo), contenido);
    }

    public static string Pensum(string carrera, string nombre, string version, params (string Codigo, int Creditos, int Cuat, string Prer)[] materias)
    {
        var lista = string.Join(",", materias.Select(m =>
            $"{{ \"codigo\": \"{m.Codigo}\", \"nombre\": \"Materia {m.Codigo}\", \"creditos\": {m.Creditos}, \"cuatrimestre\": {m.Cuat}, \"prerrequisitos\": [{(m.Prer.Length == 0 ? "" : $"\"{m.Prer}\"")}] }}"));
        return $$"""{ "formato": 1, "universidad": "uni-prueba", "carrera": "{{carrera}}", "nombreCarrera": "{{nombre}}", "version": "{{version}}", "totalCreditos": {{materias.Sum(m => m.Creditos)}}, "cuatrimestres": {{materias.Max(m => m.Cuat)}}, "materias": [ {{lista}} ] }""";
    }

    public ReglasUniversidadService Reglas(string universidadInicial = "unapec") =>
        new(new EntornoFalso(Path.GetDirectoryName(Carpeta)!),
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Pensums:Carpeta"] = Carpeta, ["Pensums:Universidad"] = universidadInicial }).Build(),
            NullLogger<ReglasUniversidadService>.Instance);

    public void Dispose() { try { Directory.Delete(Path.GetDirectoryName(Carpeta)!, true); } catch (IOException) { } }
}

/// <summary>Elegir carrera: lista filtrable, activar un pénsum del catálogo y sus efectos en el resto de la aplicación.</summary>
public class CarreraServiceTests : IDisposable
{
    private readonly CatalogoTemporal _catalogo = new();
    private readonly BdPrueba _bd = new();
    private readonly ReglasUniversidadService _reglas;
    private readonly CarreraService _s;

    public CarreraServiceTests()
    {
        _reglas = _catalogo.Reglas();
        _s = new CarreraService(_bd.Db, _reglas, new AcademicoService(_bd.Db, _reglas));
    }

    public void Dispose() { _bd.Dispose(); _catalogo.Dispose(); }

    // ── Lista y filtro ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListaTodosLosPensumsOrdenadosPorUniversidadCarreraYVersionMasNuevaPrimero()
    {
        var v = await _s.ObtenerAsync(null, null);

        Assert.Equal(4, v.TotalEnCatalogo);
        Assert.Equal(new[] { "unapec/ingenieria-software-11.json", "uni-prueba/administracion-2020.json", "uni-prueba/derecho-2023.json", "uni-prueba/derecho-2019.json" },
            v.Pensums.Select(p => p.Definicion.Clave));
        Assert.Equal(new[] { "UNAPEC – Universidad APEC", "Universidad de Prueba" }, v.Universidades.Select(u => u.Nombre));        Assert.True(v.CarpetaEncontrada);
        Assert.Empty(v.Problemas);
    }

    [Fact]
    public async Task ElOrdenPonePrimeroLaVersionMasNuevaDeCadaCarrera()
    {
        var derecho = (await _s.ObtenerAsync("uni-prueba", "derecho")).Pensums.Select(p => p.Definicion.Version).ToList();

        Assert.Equal(new[] { "2023", "2019" }, derecho);
    }

    [Theory]
    [InlineData("ingenieria", 1)]          // sin acento encuentra «Ingeniería»
    [InlineData("INGENIERÍA", 1)]
    [InlineData("software", 1)]            // también por el identificador de la carrera
    [InlineData("derecho", 2)]
    [InlineData("administracion", 1)]
    [InlineData("empresas", 1)]
    [InlineData("apec", 1)]                // por el nombre de la universidad
    [InlineData("2019", 1)]                // por la versión
    [InlineData("  derecho  ", 2)]
    [InlineData("medicina", 0)]
    public async Task ElTextoBuscaEnCarreraUniversidadYVersionSinImportarMayusculasNiAcentos(string texto, int esperados) =>
        Assert.Equal(esperados, (await _s.ObtenerAsync(null, texto)).Pensums.Count);

    [Fact]
    public async Task ElFiltroDeUniversidadEsExactoYSeCombinaConElTexto()
    {
        Assert.Equal(3, (await _s.ObtenerAsync("uni-prueba", null)).Pensums.Count);
        Assert.Single((await _s.ObtenerAsync("unapec", null)).Pensums);
        Assert.Empty((await _s.ObtenerAsync("unapec", "derecho")).Pensums);
        Assert.Equal(2, (await _s.ObtenerAsync("uni-prueba", "derecho")).Pensums.Count);
        Assert.Empty((await _s.ObtenerAsync("no-existe", null)).Pensums);
        Assert.Equal(4, (await _s.ObtenerAsync("", "")).Pensums.Count);            // filtros vacíos = todo
        Assert.Equal(4, (await _s.ObtenerAsync("uni-prueba", "derecho")).TotalEnCatalogo);   // el total no depende del filtro
    }

    [Fact]
    public async Task CadaPensumMuestraSusCreditosCuatrimestresYMaterias()
    {
        var unapec = (await _s.ObtenerAsync("unapec", null)).Pensums.Single().Definicion;
        var derecho = (await _s.ObtenerAsync("uni-prueba", "derecho")).Pensums.Single(p => p.Definicion.Version == "2023").Definicion;

        Assert.Equal((218, 12), (unapec.TotalCreditos, unapec.Cuatrimestres));
        Assert.Equal((10, 2, 3), (derecho.TotalCreditos, derecho.Cuatrimestres, derecho.Materias.Count));
    }

    [Fact]
    public async Task UnArchivoConErroresNoApareceEnLaListaPeroSusErroresSiSeMuestran()
    {
        _catalogo.Escribir("uni-prueba", "rota-1.json", "{ no es json");
        _catalogo.Escribir("uni-prueba", "derecho-2024.json", CatalogoTemporal.Pensum("derecho", "Derecho", "2024", ("AAA100", 3, 1, "ZZZ999")));   // prerrequisito que no existe

        var v = await _s.ObtenerAsync(null, null);

        Assert.Equal(4, v.Pensums.Count);
        Assert.Equal(2, v.Problemas.Count(p => p.StartsWith("uni-prueba/")));
        Assert.Contains(v.Problemas, p => p.Contains("rota-1.json") && p.Contains("no es un JSON válido"));
        Assert.Contains(v.Problemas, p => p.Contains("derecho-2024.json") && p.Contains("ZZZ999 no existe"));
    }

    [Fact]
    public async Task SinCarpetaDePensumsLaListaEstaVaciaYLoDice()
    {
        var sinCarpeta = ReglasUniversidadService.Fijas(ReglasUniversidad.Unapec);
        var s = new CarreraService(_bd.Db, sinCarpeta, new AcademicoService(_bd.Db, sinCarpeta));

        var v = await s.ObtenerAsync(null, null);

        Assert.False(v.CarpetaEncontrada);
        Assert.Empty(v.Pensums);
    }

    // ── Activar ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ActivarReemplazaLasMateriasDelPensumYGuardaCualEsElActivo()
    {
        _bd.Db.MateriasPensum.AddRange(DatosLab.Pensum());
        await _bd.Db.SaveChangesAsync();

        var r = await _s.ActivarAsync("uni-prueba/derecho-2023.json");

        Assert.True(r.Ok, r.Mensaje);
        Assert.Contains("Pénsum activo: Derecho (plan 2023): 3 materias, 10 créditos, 2 cuatrimestres.", r.Mensaje);
        Assert.Equal(new[] { "CCC100", "DDD200", "EEE300" }, (await _bd.Db.MateriasPensum.AsNoTracking().OrderBy(m => m.Codigo).ToListAsync()).Select(m => m.Codigo));
        var activo = Assert.Single(await _bd.Db.PensumActivo.AsNoTracking().ToListAsync());
        Assert.Equal(("uni-prueba/derecho-2023.json", "Derecho"), (activo.Clave, activo.NombreCarrera));
        Assert.True((DateTime.UtcNow - activo.Aplicado).TotalMinutes < 1);
        Assert.Equal("DDD200", (await _bd.Db.MateriasPensum.AsNoTracking().SingleAsync(m => m.Codigo == "DDD200")).Codigo);
        Assert.Equal("CCC100", (await _bd.Db.MateriasPensum.AsNoTracking().SingleAsync(m => m.Codigo == "DDD200")).Prerrequisitos);
    }

    [Fact]
    public async Task ActivarUnPensumCambiaLasReglasALasDeSuUniversidad()
    {
        Assert.Equal("unapec", _reglas.Activa.Id);

        await _s.ActivarAsync("uni-prueba/derecho-2019.json");
        Assert.Equal("uni-prueba", _reglas.Activa.Id);
        Assert.Equal(new[] { "SEM1", "SEM2" }, _reglas.Activa.Periodos.Periodos.Select(p => p.Nombre));

        await _s.ActivarAsync("unapec/ingenieria-software-11.json");
        Assert.Equal("unapec", _reglas.Activa.Id);
        Assert.Equal(ReglasUniversidad.Unapec, _reglas.Activa);
    }

    [Fact]
    public async Task ActivarDosVecesDejaUnaSolaFila()
    {
        await _s.ActivarAsync("uni-prueba/derecho-2019.json");
        await _s.ActivarAsync("uni-prueba/derecho-2023.json");
        await _s.ActivarAsync("uni-prueba/derecho-2023.json");

        var activo = Assert.Single(await _bd.Db.PensumActivo.ToListAsync());
        Assert.Equal("2023", activo.Version);
        Assert.Equal(3, await _bd.Db.MateriasPensum.CountAsync());
    }

    [Theory]
    [InlineData("uni-prueba/no-existe-2020.json")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("../../etc/passwd")]
    public async Task UnPensumQueNoEstaEnElCatalogoNoSeActivaYNoSeTocaNada(string? clave)
    {
        _bd.Db.MateriasPensum.AddRange(DatosLab.Pensum());
        await _bd.Db.SaveChangesAsync();
        var antes = await _bd.Db.MateriasPensum.CountAsync();

        var r = await _s.ActivarAsync(clave);

        Assert.False(r.Ok);
        Assert.Contains("no está en el catálogo", r.Mensaje);
        Assert.Equal(antes, await _bd.Db.MateriasPensum.CountAsync());
        Assert.Empty(await _bd.Db.PensumActivo.ToListAsync());
    }

    [Fact]
    public async Task UnPensumConErroresNoSePuedeActivar()
    {
        _catalogo.Escribir("uni-prueba", "derecho-2024.json", CatalogoTemporal.Pensum("derecho", "Derecho", "2024", ("AAA100", 3, 1, "ZZZ999")));

        var r = await _s.ActivarAsync("uni-prueba/derecho-2024.json");

        Assert.False(r.Ok);
        Assert.Empty(await _bd.Db.MateriasPensum.ToListAsync());
    }

    [Fact]
    public async Task UnPensumDeUnaUniversidadSinReglasValidasNoSePuedeActivar()
    {
        _catalogo.Escribir("otra", "carrera-x-1.json", CatalogoTemporal.Pensum("carrera-x", "X", "1", ("AAA100", 3, 1, "")).Replace("uni-prueba", "otra"));   // no hay otra/universidad.json

        var r = await _s.ActivarAsync("otra/carrera-x-1.json");

        Assert.False(r.Ok);
        Assert.Empty(await _bd.Db.PensumActivo.ToListAsync());
    }

    [Fact]
    public async Task ElHistoricoDeBannerYLasEquivalenciasSeConservanAlCambiarDeCarrera()
    {
        _bd.Db.Periodos.Add(new Periodo { Nombre = "ENE-ABR 2025", Orden = 1, Materias = new() { new MateriaCursada { Codigo = "AAA100", Calificacion = "A", HorasCredito = 3 } } });
        _bd.Db.Equivalencias.Add(new Equivalencia { CodigoBanner = "ING701", CodigoPensum = "ING716" });
        _bd.Db.DatosAlumno.Add(new DatosAlumno { Nombre = "Estudiante de Prueba" });
        await _bd.Db.SaveChangesAsync();

        await _s.ActivarAsync("uni-prueba/derecho-2019.json");

        Assert.Equal(1, await _bd.Db.MateriasCursadas.CountAsync());
        Assert.Equal(1, await _bd.Db.Equivalencias.CountAsync());
        Assert.Equal("Estudiante de Prueba", (await _bd.Db.DatosAlumno.SingleAsync()).Nombre);
    }

    [Fact]
    public async Task LoQueApuntabaAMateriasQueYaNoExistenSeQuitaYSeDiceCuantas()
    {
        _bd.Db.MateriasPensum.AddRange(DatosLab.Pensum());
        var plan = new PlanEstudio { Nombre = "Plan 1", Creado = DateTime.UtcNow, Actualizado = DateTime.UtcNow, Periodos = new()
        {
            new PeriodoPlanificado { Nombre = "ENE-ABR 2026", Materias = new() { new MateriaPlanificada { Codigo = "ISO200" }, new MateriaPlanificada { Codigo = "AAA100" } } },
            new PeriodoPlanificado { Nombre = "MAY-AGO 2026", Materias = new() { new MateriaPlanificada { Codigo = "ISO300" } } },
        } };
        _bd.Db.PlanesEstudio.Add(plan);
        _bd.Db.AperturasSolicitadas.AddRange(
            new AperturaSolicitada { Codigo = "ISO725", Nota = "", Marcada = DateTime.UtcNow },
            new AperturaSolicitada { Codigo = "BBB200", Nota = "", Marcada = DateTime.UtcNow });
        await _bd.Db.SaveChangesAsync();

        var r = await _s.ActivarAsync("uni-prueba/derecho-2019.json");   // AAA100 y BBB200

        Assert.Contains("Se quitaron 2 materias de tus escenarios del planificador que no existen en este pénsum.", r.Mensaje);
        Assert.Contains("Se quitó 1 materia de la lista de solicitar apertura que no existe en este pénsum.", r.Mensaje);
        _bd.Db.ChangeTracker.Clear();
        Assert.Equal(new[] { "AAA100" }, await _bd.Db.MateriasPlanificadas.Select(m => m.Codigo).ToListAsync());
        Assert.Equal(new[] { "ENE-ABR 2026" }, await _bd.Db.PeriodosPlanificados.Select(p => p.Nombre).ToListAsync());   // el período que quedó vacío se borra
        Assert.Equal(new[] { "BBB200" }, await _bd.Db.AperturasSolicitadas.Select(a => a.Codigo).ToListAsync());
        Assert.Equal(1, await _bd.Db.PlanesEstudio.CountAsync());                                                        // el escenario se conserva
    }

    [Fact]
    public async Task SinNadaQueQuitarElMensajeNoMencionaLimpiezas()
    {
        var r = await _s.ActivarAsync("uni-prueba/derecho-2019.json");

        Assert.DoesNotContain("Se quitaron", r.Mensaje);
    }

    [Fact]
    public async Task ConHistoricoElMensajeDiceCuantoLlevasConElNuevoPensum()
    {
        _bd.Db.Periodos.Add(new Periodo { Nombre = "SEM1 2025", Orden = 1, Materias = new() { new MateriaCursada { Codigo = "AAA100", Calificacion = "A", HorasCredito = 3 } } });
        await _bd.Db.SaveChangesAsync();

        var r = await _s.ActivarAsync("uni-prueba/derecho-2019.json");   // AAA100 (3) y BBB200 (5): llevas 3 de 8; BBB200 queda disponible

        Assert.Contains($"llevas 3 de 8 créditos aprobados ({37.5m.ToString("0.0", HistorialAcademico.Web.Helpers.Ui.Cultura)} %)", r.Mensaje);
        Assert.Contains("tienes 1 materia disponible.", r.Mensaje);
    }

    [Fact]
    public async Task ElEstadoDeLasMateriasSeRecalculaConElNuevoPensum()
    {
        _bd.Db.Periodos.Add(new Periodo { Nombre = "SEM1 2025", Orden = 1, Materias = new() { new MateriaCursada { Codigo = "AAA100", Calificacion = "A", HorasCredito = 3 } } });
        await _bd.Db.SaveChangesAsync();
        var academico = new AcademicoService(_bd.Db, _reglas);

        await _s.ActivarAsync("uni-prueba/derecho-2019.json");
        var e = await academico.ObtenerAsync();

        Assert.Equal(EstadoMateria.Aprobada, e.Pensum.Buscar("AAA100")!.Estado);
        Assert.Equal(EstadoMateria.Disponible, e.Pensum.Buscar("BBB200")!.Estado);
        Assert.Equal("uni-prueba", e.Reglas.Id);
        Assert.Equal(8, e.Pensum.CreditosTotales);
    }

    // ── Cuál está en uso y si coincide con el cargado ─────────────────────────────────────

    [Fact]
    public async Task ElPensumActivoSeMarcaEnLaLista()
    {
        await _s.ActivarAsync("uni-prueba/derecho-2023.json");

        var v = await _s.ObtenerAsync(null, null);

        Assert.Equal(new[] { "uni-prueba/derecho-2023.json" }, v.Pensums.Where(p => p.EsActivo).Select(p => p.Definicion.Clave));
        Assert.Equal("Derecho", v.Activo!.NombreCarrera);
        Assert.Equal((3, 10), (v.MateriasCargadas, v.CreditosCargados));
        Assert.Equal(new[] { "uni-prueba/derecho-2023.json" }, v.Pensums.Where(p => p.CoincideConCargado).Select(p => p.Definicion.Clave));
    }

    [Fact]
    public async Task UnPensumCargadoDesdeCsvQueEsIgualAlDelCatalogoSeReconoce()
    {
        _bd.Db.MateriasPensum.AddRange(DatosLab.Pensum());
        await _bd.Db.SaveChangesAsync();

        var v = await _s.ObtenerAsync(null, null);

        Assert.Null(v.Activo);                                      // no se eligió del catálogo…
        Assert.True(v.HayPensumCargado);
        var igual = Assert.Single(v.Pensums, p => p.CoincideConCargado);
        Assert.Equal("unapec/ingenieria-software-11.json", igual.Definicion.Clave);   // …pero es el mismo
        Assert.False(igual.EsActivo);
    }

    [Fact]
    public async Task UnCambioMinimoEnElPensumCargadoYaNoCoincide()
    {
        var materias = DatosLab.Pensum();
        materias.First(m => m.Codigo == "ISO625").Creditos = 5;   // el catálogo dice 4
        _bd.Db.MateriasPensum.AddRange(materias);
        await _bd.Db.SaveChangesAsync();

        var v = await _s.ObtenerAsync(null, null);

        Assert.DoesNotContain(v.Pensums, p => p.CoincideConCargado);
    }

    [Fact]
    public async Task SinNingunPensumCargadoNiActivoNoHayCoincidencia()
    {
        var v = await _s.ObtenerAsync(null, null);

        Assert.Null(v.Activo);
        Assert.False(v.HayPensumCargado);
        Assert.DoesNotContain(v.Pensums, p => p.CoincideConCargado || p.EsActivo);
    }

    [Fact]
    public void ElRecorridoEnUnapecUsaLasReglasIncorporadasSiNoSeActivaOtraUniversidad()
    {
        Assert.Equal(ReglasUniversidad.Unapec, _reglas.Activa);
    }

    // ── Normalizar ────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Ingeniería", "ingenieria")]
    [InlineData("  AÑO Ñandú ", "ano nandu")]
    [InlineData("", "")]
    [InlineData(null, "")]
    [InlineData("   ", "")]
    public void NormalizarQuitaAcentosYMayusculas(string? texto, string esperado) => Assert.Equal(esperado, CarreraService.Normalizar(texto));

    // ── Reglas: cambiar de universidad ────────────────────────────────────────────────────

    [Fact]
    public void EstablecerUnaUniversidadValidaCambiaLasReglasYUnaInvalidaNo()
    {
        Assert.True(_reglas.Establecer("uni-prueba"));
        Assert.Equal("uni-prueba", _reglas.Activa.Id);
        Assert.Equal(_reglas.Activa, _reglas.Activa);

        Assert.False(_reglas.Establecer("no-existe"));
        Assert.Equal("uni-prueba", _reglas.Activa.Id);           // no cambió
        Assert.Empty(_reglas.Problemas);
    }
}

/// <summary>Utilidades para recorrer la pantalla «Carrera y pénsum» con cookies, token antifalsificación y redirecciones.</summary>
internal static class ClienteCarrera
{
    public static async Task<(HttpResponseMessage Respuesta, string Html)> PostAsync(AppFactory app, string url, params (string, string)[] campos)
    {
        using var c = app.CreateClient();
        var pagina = await c.GetStringAsync("/Carrera");
        var token = Regex.Match(pagina, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        var datos = campos.Select(x => new KeyValuePair<string, string>(x.Item1, x.Item2)).Append(new("__RequestVerificationToken", token));
        var r = await c.PostAsync(url, new FormUrlEncodedContent(datos));
        return (r, WebUtility.HtmlDecode(await r.Content.ReadAsStringAsync()));
    }
}

/// <summary>La pantalla «Carrera y pénsum» solo de lectura, con el catálogo real del repositorio (esta clase no cambia la base).</summary>
public class CarreraPantallaTests : IClassFixture<AppConDatosFactory>
{
    private readonly AppConDatosFactory _app;

    public CarreraPantallaTests(AppConDatosFactory app) => _app = app;

    private async Task<string> GetAsync(string url) => (await _app.GetAsync(url)).Html;

    [Fact]
    public async Task LaPantallaListaElPensumDeUnapecConSusCreditosYCuatrimestres()
    {
        var (estado, html) = await _app.GetAsync("/Carrera");

        Assert.Equal(HttpStatusCode.OK, estado);
        Assert.Contains("Carrera y pénsum", html);
        Assert.Contains("id=\"tabla-carreras\"", html);
        Assert.Contains("Ingeniería de Software", html);
        Assert.Contains("UNAPEC – Universidad APEC", html);
        Assert.Matches("<td class=\"text-end\">218</td>\\s*<td class=\"text-end\">12</td>", html);   // créditos y cuatrimestres
        Assert.Contains("data-clave=\"unapec/ingenieria-software-11.json\"", html);
        Assert.Contains("Usar este pénsum", html);
        Assert.Contains("id=\"filtro-carrera\"", html);
        Assert.Contains("Todas las universidades", html);
    }

    [Fact]
    public async Task ConElPensumCargadoDesdeCsvDiceQueEsIdenticoAlDelCatalogo()
    {
        // La base de la fábrica tiene el pénsum del CSV cargado a mano (no elegido del catálogo).
        var html = await GetAsync("/Carrera");

        Assert.Contains("Tienes un pénsum cargado desde un archivo CSV", html);
        Assert.Contains("id=\"coincide\"", html);
        Assert.Contains("Es idéntico a <strong>Ingeniería de Software</strong>", html);
        Assert.DoesNotContain("onsubmit=\"return confirm(", html);   // si es idéntico no hace falta advertir que se reemplaza
    }

    [Fact]
    public async Task LasReglasDeLaUniversidadActivaSeMuestran()
    {
        var html = await GetAsync("/Carrera");

        Assert.Contains("Reglas de UNAPEC – Universidad APEC que se están usando", html);
        Assert.Contains("A = 4, B = 3, C = 2, D = 1, F = 0", html);
        Assert.Contains("sin puntos (no entran en el índice): E", html);
        Assert.Contains("ENE-ABR, MAY-AGO, SEP-DIC", html);
        Assert.Contains($"25 (27 si tu índice supera {HistorialAcademico.Web.Helpers.Ui.Indice(3.40m)})", html);
    }

    [Theory]
    [InlineData("/Carrera?q=ingenieria", true)]
    [InlineData("/Carrera?q=INGENIERÍA+de+software", true)]
    [InlineData("/Carrera?universidad=unapec", true)]
    [InlineData("/Carrera?universidad=unapec&q=software", true)]
    [InlineData("/Carrera?q=derecho", false)]
    [InlineData("/Carrera?universidad=no-existe", false)]
    public async Task ElFiltroMuestraSoloLoQueCoincide(string url, bool hayResultados)
    {
        var html = await GetAsync(url);

        Assert.Equal(hayResultados, html.Contains("id=\"tabla-carreras\""));
        Assert.Equal(!hayResultados, html.Contains("id=\"sin-resultados\""));
        if (!hayResultados) Assert.Contains("Ningún pénsum coincide con lo que buscas (1 en el catálogo)", html);
    }

    [Fact]
    public async Task ElFiltroConservaLoQueEscribisteSinInterpretarloComoHtml()
    {
        using var c = _app.CreateClient();

        var crudo = await c.GetStringAsync("/Carrera?universidad=unapec&q=softw%3Cb%3E");

        Assert.Contains("value=\"softw&lt;b&gt;\"", crudo);       // escapado
        Assert.DoesNotContain("softw<b>", crudo);
        Assert.Matches("<option value=\"unapec\" selected", crudo);
        Assert.Contains("Limpiar", crudo);
    }

    [Fact]
    public async Task SiTuCarreraNoEstaHayUnEnlaceALaGuia()
    {
        var html = await GetAsync("/Carrera");
        var guia = await _app.GetAsync("/Carrera/Guia");

        Assert.Contains("¿No está tu carrera?", html);
        Assert.Contains("href=\"/Carrera/Guia\"", html);
        Assert.Equal(HttpStatusCode.OK, guia.Estado);
        Assert.Contains("Agregar una carrera", guia.Html);
        Assert.Contains("<h2>Cómo agregar tu carrera (un pénsum nuevo)</h2>", guia.Html);   // la guía CONTRIBUTING-pensums.md, ya convertida a HTML
        Assert.Contains("Paso 4: abre el Pull Request", guia.Html);
        Assert.Contains("<pre class=\"guia-codigo\">", guia.Html);
        Assert.Contains("class=\"table table-sm guia-tabla\"", guia.Html);
        Assert.Contains("universidad.json", guia.Html);
        Assert.DoesNotContain("<pre class=\"guia\">", guia.Html);
        Assert.DoesNotContain("```", guia.Html);
    }

    [Fact]
    public async Task ActivarUnPensumQueNoExisteMuestraElError()
    {
        var (_, html) = await ClienteCarrera.PostAsync(_app, "/Carrera/Activar", ("clave", "uni-x/inventado-1.json"));

        Assert.Contains("Ese pénsum no está en el catálogo", html);
        Assert.Contains("alert-danger", html);
    }

    [Fact]
    public async Task ActivarSinTokenSeRechaza()
    {
        using var c = _app.CreateClient();

        var r = await c.PostAsync("/Carrera/Activar", new FormUrlEncodedContent(new Dictionary<string, string> { ["clave"] = "unapec/ingenieria-software-11.json" }));

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task ElMenuTieneCarreraYPensumYMarcaLaSeccionActiva()
    {
        var html = await GetAsync("/Carrera");

        Assert.Matches("class=\"menu-item activo\"[^>]*title=\"Carrera y pénsum\"", html);
        Assert.Single(Regex.Matches(html, "class=\"menu-item activo\""));
        Assert.Contains("title=\"Pénsum (CSV)\"", html);
    }
}

/// <summary>Elegir una carrera y subir un CSV a mano cambian la base: cada prueba usa su propia aplicación.</summary>
public class CarreraPantallaCambiosTests
{
    private static readonly int MateriasUnapec = PensumCsvParser.Parse(PensumEjemplo.Csv).Materias.Count;

    /// <summary>Una aplicación con datos de prueba y su propia base (al crearla a mano hay que llamar a InitializeAsync).</summary>
    private static async Task<AppConDatosFactory> NuevaAppAsync()
    {
        var app = new AppConDatosFactory();
        await app.InitializeAsync();
        return app;
    }

    [Fact]
    public async Task ActivarUnPensumLoMarcaEnUsoYAvisaLoQueCambio()
    {
        using var app = await NuevaAppAsync();

        var (respuesta, html) = await ClienteCarrera.PostAsync(app, "/Carrera/Activar", ("clave", "unapec/ingenieria-software-11.json"));

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);   // tras la redirección
        Assert.Contains($"Pénsum activo: Ingeniería de Software (plan 11): {MateriasUnapec} materias, 218 créditos, 12 cuatrimestres.", html);
        Assert.Contains("créditos aprobados", html);              // con el histórico sintético: cuánto llevas
        Assert.Contains("<span class=\"badge bg-success\">En uso</span>", html);
        Assert.Contains("Ingeniería de Software</strong> · UNAPEC – Universidad APEC · plan 11", html);
        Assert.DoesNotContain("Tienes un pénsum cargado desde un archivo CSV", html);
        Assert.Matches("class=\"fila-activa\" data-clave=\"unapec/ingenieria-software-11.json\"", html);

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HistorialContext>();
        Assert.Equal("unapec/ingenieria-software-11.json", (await db.PensumActivo.AsNoTracking().SingleAsync()).Clave);
        Assert.Equal(MateriasUnapec, await db.MateriasPensum.CountAsync());
    }

    [Fact]
    public async Task ElRestoDeLasPantallasSiguenFuncionandoTrasElegirCarrera()
    {
        using var app = await NuevaAppAsync();
        await ClienteCarrera.PostAsync(app, "/Carrera/Activar", ("clave", "unapec/ingenieria-software-11.json"));

        foreach (var ruta in new[] { "/", "/Estudiante/MateriasFaltantes", "/Estudiante/IndiceAcademico", "/Pensum/Mapa", "/Pensum/QueInscribir", "/Planificador" })
            Assert.Equal(HttpStatusCode.OK, (await app.GetAsync(ruta)).Estado);
    }

    [Fact]
    public async Task SubirUnCsvADesmanoDejaDeSerElPensumDelCatalogo()
    {
        using var app = await NuevaAppAsync();
        await ClienteCarrera.PostAsync(app, "/Carrera/Activar", ("clave", "unapec/ingenieria-software-11.json"));
        using var c = app.CreateClient();
        var pagina = await c.GetStringAsync("/Pensum");
        var token = Regex.Match(pagina, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        var contenido = new MultipartFormDataContent
        {
            { new StringContent(token), "__RequestVerificationToken" },
            { new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(PensumEjemplo.Csv)), "archivo", "pensum.csv" },
        };

        var r = await c.PostAsync("/Pensum/Subir", contenido);
        var html = WebUtility.HtmlDecode(await r.Content.ReadAsStringAsync());

        Assert.Contains("Pénsum cargado desde el CSV", html);
        Assert.Contains("Carrera y pénsum", html);                // el aviso apunta a la pantalla nueva
        using var scope = app.Services.CreateScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<HistorialContext>().PensumActivo.ToListAsync());
    }
}
