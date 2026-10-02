using HistorialAcademico.Banner;
using HistorialAcademico.Core.Entities;
using HistorialAcademico.Web.Data;
using HistorialAcademico.Web.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HistorialAcademico.Tests;

/// <summary>SQLite en memoria con las migraciones reales aplicadas.</summary>
public sealed class BdPrueba : IDisposable
{
    private readonly SqliteConnection _conexion = new("Data Source=:memory:");
    public HistorialContext Db { get; }
    public SincronizacionService Sync { get; }

    public BdPrueba()
    {
        _conexion.Open();
        Db = new HistorialContext(new DbContextOptionsBuilder<HistorialContext>().UseSqlite(_conexion).Options);
        Db.Database.Migrate();
        Sync = new SincronizacionService(Db, new BannerClient(new BannerOptions()));
    }

    public void Dispose() { Db.Dispose(); _conexion.Dispose(); }

    /// <summary>Foto del contenido de los datos de Banner, para comprobar que "no se tocó nada".</summary>
    public async Task<string> FotoAsync()
    {
        var periodos = await Db.Periodos.AsNoTracking().OrderBy(p => p.Orden).Select(p => $"{p.Nombre}|{p.PuntosCalidad}|{p.Materias.Count}").ToListAsync();
        var materias = await Db.MateriasCursadas.AsNoTracking().OrderBy(m => m.Id).Select(m => m.Codigo + m.Calificacion).ToListAsync();
        var cursos = await Db.CursosEnProgreso.AsNoTracking().OrderBy(c => c.Id).Select(c => c.Codigo).ToListAsync();
        var alumnos = await Db.DatosAlumno.AsNoTracking().Select(a => a.Programa + a.EstadoAcademico).ToListAsync();
        return string.Join(";", periodos) + "#" + string.Join(",", materias) + "#" + string.Join(",", cursos) + "#" + string.Join(",", alumnos);
    }
}

public class SincronizacionTests
{
    [Fact]
    public async Task GuardaElHistoricoSinteticoCompleto()
    {
        using var bd = new BdPrueba();
        var r = await bd.Sync.AplicarHtmlAsync(Muestras.LeerSintetico());

        Assert.True(r.Exito, r.Mensaje);
        Assert.Equal(new[] { "ENE-ABR 2025", "MAY-AGO 2025" }, await bd.Db.Periodos.OrderBy(p => p.Orden).Select(p => p.Nombre).ToListAsync());
        Assert.Equal(5, await bd.Db.MateriasCursadas.CountAsync());
        Assert.Equal(new[] { "ISO400", "MAT102" }, await bd.Db.CursosEnProgreso.OrderBy(c => c.Codigo).Select(c => c.Codigo).ToListAsync());

        var p2 = await bd.Db.Periodos.Include(p => p.Materias).SingleAsync(p => p.Orden == 2);
        Assert.Equal(1.00m, p2.Pga);
        Assert.Equal(30.00m, p2.AcumPuntosCalidad);
        Assert.Equal(new[] { "ESP101", "ISO300" }, p2.Materias.Select(m => m.Codigo).OrderBy(c => c));

        var alumno = await bd.Db.DatosAlumno.SingleAsync();
        Assert.Equal(new DateOnly(2001, 3, 5), alumno.FechaNacimiento);
        Assert.Equal("EN OBSERVACION", alumno.EstadoAcademico);
        Assert.Equal(10m, alumno.TotalHorasAprobadas);
        Assert.Equal(2.31m, alumno.TotalPga);

        var log = await bd.Db.Sincronizaciones.SingleAsync();
        Assert.Equal(ResultadoSincronizacion.Exito, log.Resultado);
    }

    [Fact]
    public async Task SincronizarDosVecesReemplazaSinDuplicar()
    {
        using var bd = new BdPrueba();
        await bd.Sync.AplicarHtmlAsync(Muestras.LeerSintetico());
        var primera = await bd.FotoAsync();
        var r = await bd.Sync.AplicarHtmlAsync(Muestras.LeerSintetico());

        Assert.True(r.Exito);
        Assert.Equal(primera, await bd.FotoAsync());
        Assert.Equal(1, await bd.Db.DatosAlumno.CountAsync());
        Assert.Equal(2, await bd.Db.Sincronizaciones.CountAsync());   // el historial de intentos se conserva
    }

    [Fact]
    public async Task LaSincronizacionNoTocaLosDatosPropios()
    {
        using var bd = new BdPrueba();
        bd.Db.MateriasPensum.Add(new MateriaPensum { Codigo = "ISO100", Nombre = "Fundamentos", Creditos = 5, Cuatrimestre = 1 });
        bd.Db.Equivalencias.Add(new Equivalencia { CodigoBanner = "ING701", CodigoPensum = "ING716" });
        await bd.Db.SaveChangesAsync();

        await bd.Sync.AplicarHtmlAsync(Muestras.LeerSintetico());

        Assert.Equal(1, await bd.Db.MateriasPensum.CountAsync());
        Assert.Equal(1, await bd.Db.Equivalencias.CountAsync());
    }

    [Theory]
    [InlineData("<html><body>Inicia sesión</body></html>", "No pude leer")]
    [InlineData("", "No pude leer")]
    public async Task SiElParserFallaNoTocaLaBaseYRegistraElError(string htmlMalo, string esperado)
    {
        using var bd = new BdPrueba();
        await bd.Sync.AplicarHtmlAsync(Muestras.LeerSintetico());
        var antes = await bd.FotoAsync();

        var r = await bd.Sync.AplicarHtmlAsync(htmlMalo);

        Assert.False(r.Exito);
        Assert.Contains(esperado, r.Mensaje);
        Assert.Equal(antes, await bd.FotoAsync());
        var ultimo = await bd.Db.Sincronizaciones.OrderByDescending(s => s.Id).FirstAsync();
        Assert.Equal(ResultadoSincronizacion.Error, ultimo.Resultado);
        Assert.Equal(r.Mensaje, ultimo.Mensaje);
    }

    [Fact]
    public async Task SiLosTotalesNoCuadranNoTocaLaBaseYRegistraElError()
    {
        using var bd = new BdPrueba();
        await bd.Sync.AplicarHtmlAsync(Muestras.LeerSintetico());
        var antes = await bd.FotoAsync();

        var html = Muestras.LeerSintetico().Replace("<td class=\"dddefault\">3.000</td><td class=\"dddefault\">12.00</td>", "<td class=\"dddefault\">3.000</td><td class=\"dddefault\">15.00</td>");
        var r = await bd.Sync.AplicarHtmlAsync(html);

        Assert.False(r.Exito);
        Assert.Contains("no cuadran", r.Mensaje);
        Assert.Equal(antes, await bd.FotoAsync());
        Assert.Equal(ResultadoSincronizacion.Error, (await bd.Db.Sincronizaciones.OrderByDescending(s => s.Id).FirstAsync()).Resultado);
    }

    [Fact]
    public async Task SiFallaAlGuardarSeRevierteTodoLoBorrado()
    {
        using var bd = new BdPrueba();
        await bd.Sync.AplicarHtmlAsync(Muestras.LeerSintetico());
        var antes = await bd.FotoAsync();

        // Un histórico válido cuyo período choca con un índice único (dos períodos con el mismo nombre)
        // obliga a fallar en SaveChanges, después de haber borrado los datos anteriores dentro de la transacción.
        var html = Muestras.LeerSintetico().Replace("Periodo: MAY-AGO 2025 GRADO", "Periodo: ENE-ABR 2025 GRADO");
        var r = await bd.Sync.AplicarHtmlAsync(html);

        Assert.False(r.Exito);
        Assert.Contains("base de datos", r.Mensaje);
        Assert.Equal(antes, await bd.FotoAsync());   // lo borrado se restauró: la transacción hizo rollback
    }

    [Fact]
    public async Task SinSesionGuardadaPideIniciarSesionYLoRegistra()
    {
        using var bd = new BdPrueba();
        var banner = new BannerClient(new BannerOptions { BaseUrl = "https://alumnos.invalid/", CarpetaDatos = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")) });
        var sync = new SincronizacionService(bd.Db, banner);

        var r = await sync.SincronizarDesdeBannerAsync();

        Assert.False(r.Exito);
        Assert.True(r.RequiereLogin);
        Assert.Equal(ResultadoSincronizacion.Error, (await bd.Db.Sincronizaciones.SingleAsync()).Resultado);
    }

    [Fact]
    public async Task SincronizaElHistoricoAnonimizado()
    {
        using var bd = new BdPrueba();
        var r = await bd.Sync.AplicarHtmlAsync(Muestras.LeerAnonimizado());

        Assert.True(r.Exito, r.Mensaje);
        Assert.Equal(7, await bd.Db.Periodos.CountAsync());
        Assert.Equal(50, await bd.Db.MateriasCursadas.CountAsync());
        Assert.Equal(6, await bd.Db.CursosEnProgreso.CountAsync());
        var a = await bd.Db.DatosAlumno.SingleAsync();
        Assert.Equal(143m, a.TotalHorasAprobadas);
        Assert.Equal(134m, a.TotalHorasPga);
        Assert.Equal(351m, a.TotalPuntosCalidad);
        Assert.Equal(2.62m, a.TotalPga);
        Assert.Equal("SEP-DIC 2026", (await bd.Db.CursosEnProgreso.FirstAsync()).Periodo);
        Assert.DoesNotContain("Ojo", r.Mensaje);   // un histórico que cuadra no lleva avisos
    }

    [Fact]
    public async Task UnHistoricoDondeBannerCuentaMenosHorasIntentadasQueLasMateriasQueListaSeGuardaYAvisa()
    {
        // Lo que le pasó a un tester tras cambiarse de pénsum: Banner lista materias que no cuenta como intentadas.
        using var bd = new BdPrueba();
        var html = ValidadorHistoricoTests.ConHorasIntentadasCambiadas(Muestras.LeerAnonimizado(), periodo: 2, cambio: -5m);

        var r = await bd.Sync.AplicarHtmlAsync(html);

        Assert.True(r.Exito, r.Mensaje);
        Assert.Contains("Sincronizado: 7 períodos", r.Mensaje);
        Assert.Contains("Ojo, en 1 período Banner cuenta menos horas intentadas que las materias que lista", r.Mensaje);
        Assert.Contains("tus puntos y tu índice sí cuadran", r.Mensaje);
        Assert.Contains("ENE-ABR 2025: las materias suman 27 horas y Banner cuenta 22", r.Mensaje);
        Assert.Equal(7, await bd.Db.Periodos.CountAsync());                 // y se guardó todo
        Assert.Equal(50, await bd.Db.MateriasCursadas.CountAsync());
        Assert.Equal(ResultadoSincronizacion.Exito, (await bd.Db.Sincronizaciones.SingleAsync()).Resultado);
    }

    [Fact]
    public async Task SiFaltanMateriasRespectoDeLasHorasIntentadasNoSeGuardaNada()
    {
        using var bd = new BdPrueba();
        var html = ValidadorHistoricoTests.ConHorasIntentadasCambiadas(Muestras.LeerAnonimizado(), periodo: 2, cambio: +3m);

        var r = await bd.Sync.AplicarHtmlAsync(html);

        Assert.False(r.Exito);
        Assert.Contains("Los totales de Banner no cuadran, no guardé nada", r.Mensaje);
        Assert.Equal(0, await bd.Db.Periodos.CountAsync());
    }
}
