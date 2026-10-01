using HistorialAcademico.Banner;
using HistorialAcademico.Core.Models;
using Xunit;

namespace HistorialAcademico.Tests;

/// <summary>
/// Pruebas contra tests/samples/historico-anonimizado.html: la forma de un histórico real (siete períodos cerrados, exentas, códigos del plan
/// anterior y un período en progreso) con identidad y calificaciones ficticias. Los valores esperados son los de esa muestra.
/// </summary>
public class HistoricoParserRealTests
{
    private static HistoricoBanner Cargar() => HistoricoParser.Parse(Muestras.LeerAnonimizado());

    [Fact]
    public void TotalesGlobalesCoincidenConBanner()
    {
        var g = Cargar().TotalGlobal!;
        Assert.Equal(143m, g.HorasAprobadas);
        Assert.Equal(134m, g.HorasPga);
        Assert.Equal(351m, g.PuntosCalidad);
        Assert.Equal(2.62m, g.Pga);
    }

    [Fact]
    public void HaySieteCerradosYUnoEnProgreso()
    {
        var h = Cargar();
        Assert.Equal(7, h.Periodos.Count);
        Assert.Equal(
            new[] { "MAY-AGO 2024", "SEP-DIC 2024", "ENE-ABR 2025", "MAY-AGO 2025", "SEP-DIC 2025", "ENE-ABR 2026", "MAY-AGO 2026" },
            h.Periodos.Select(p => p.Nombre));
        var progreso = Assert.Single(h.EnProgreso);
        Assert.Equal("SEP-DIC 2026", progreso.Nombre);
        Assert.Equal(
            new[] { "ADM535", "INF900", "ISO720", "ISO735", "ISO931", "ISO934" },
            progreso.Cursos.Select(c => c.Codigo).OrderBy(c => c));
        Assert.Equal(20m, progreso.Cursos.Sum(c => c.HorasCredito));
    }

    [Fact]
    public void PgaPorPeriodoCoincideConLaMuestra()
    {
        Assert.Equal(
            new[] { 2.32m, 3.00m, 2.94m, 3.05m, 2.64m, 2.32m, 1.64m },
            Cargar().Periodos.Select(p => p.TotalesPeriodo!.Pga));
    }

    [Fact]
    public void CantidadDeMateriasPorPeriodo()
    {
        Assert.Equal(new[] { 15, 5, 9, 6, 6, 6, 3 }, Cargar().Periodos.Select(p => p.Materias.Count));
    }

    [Fact]
    public void LosPuntosDeLasMateriasCuadranConLosTotalesDeCadaPeriodo()
    {
        foreach (var p in Cargar().Periodos)
        {
            Assert.Equal(p.TotalesPeriodo!.PuntosCalidad, p.Materias.Sum(m => m.PuntosCalidad));
            Assert.Equal(p.TotalesPeriodo.HorasIntentadas, p.Materias.Sum(m => m.HorasCredito));
        }
    }

    [Fact]
    public void DatosDelAlumno()
    {
        var a = Cargar().Alumno;
        Assert.False(string.IsNullOrWhiteSpace(a.Nombre));
        Assert.Equal(new DateOnly(2001, 3, 5), a.FechaNacimiento);
        Assert.Equal("ANTIGUO", a.TipoAlumno);
        Assert.Equal("ADMINISTRACION DE PRUEBA", a.Programa);
        Assert.Equal("ESCUELA DE PRUEBA", a.Escuela);
        Assert.Equal("CAMPUS - PRUEBA", a.Campus);
        Assert.Equal("NORMAL", a.EstadoAcademico);
    }

    [Fact]
    public void MateriasConCodigosDelPlanAnteriorYExentas()
    {
        var todas = Cargar().Periodos.SelectMany(p => p.Materias).ToList();
        foreach (var codigo in new[] { "MAT126", "ESP102", "ING701", "ISO700", "SOC253" })
            Assert.Contains(todas, m => m.Codigo == codigo);
        var ing716 = todas.Single(m => m.Codigo == "ING716");
        Assert.Equal("E", ing716.Calificacion);
        Assert.Equal(0m, ing716.PuntosCalidad);   // las exentas no suman puntos
        Assert.Equal(1, todas.Count(m => m.Codigo == "ENG008" && m.Calificacion == "B"));
    }
}
