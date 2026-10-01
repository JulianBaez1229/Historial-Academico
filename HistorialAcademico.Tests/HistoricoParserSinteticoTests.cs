using HistorialAcademico.Banner;
using Xunit;

namespace HistorialAcademico.Tests;

/// <summary>Pruebas del parser contra un HTML sintético (datos falsos) con la misma estructura que Banner. Corren siempre.</summary>
public class HistoricoParserSinteticoTests
{
    private static readonly Core.Models.HistoricoBanner H = HistoricoParser.Parse(Muestras.LeerSintetico());

    [Fact]
    public void LeeDatosDelAlumno()
    {
        var a = H.Alumno;
        Assert.Equal("ESTUDIANTE DE PRUEBA", a.Nombre);
        Assert.Equal(new DateOnly(2001, 3, 5), a.FechaNacimiento);
        Assert.Equal("ANTIGUO", a.TipoAlumno);
        Assert.Equal("ADMINISTRACION DE PRUEBA", a.Programa);   // el del currículum actual, no el de "Grado a obtener"
        Assert.Equal("ESCUELA DE PRUEBA", a.Escuela);
        Assert.Equal("CAMPUS - PRUEBA", a.Campus);
        Assert.Equal("LICENCIADO", a.GradoAObtener);
        Assert.Equal("EN OBSERVACION", a.EstadoAcademico);      // "Último Estado Académico" gana al del período
    }

    [Fact]
    public void LeePeriodosCerradosYEnProgreso()
    {
        Assert.Equal(new[] { "ENE-ABR 2025", "MAY-AGO 2025" }, H.Periodos.Select(p => p.Nombre));
        Assert.All(H.Periodos, p => Assert.Equal("GRADO", p.Nivel));
        var progreso = Assert.Single(H.EnProgreso);
        Assert.Equal("SEP-DIC 2025", progreso.Nombre);
    }

    [Fact]
    public void UneMateriaYCursoEnElCodigo()
    {
        var p = H.Periodos[0];
        Assert.Equal(new[] { "ENG001", "MAT101", "ISO200" }, p.Materias.Select(m => m.Codigo));
        var mat = p.Materias[1];
        Assert.Equal("MAT", mat.Materia);
        Assert.Equal("101", mat.Curso);
        Assert.Equal("CALCULO BASICO", mat.Titulo);
        Assert.Equal("A", mat.Calificacion);
        Assert.Equal(3m, mat.HorasCredito);
        Assert.Equal(12m, mat.PuntosCalidad);
    }

    [Fact]
    public void LeeCalificacionesEspeciales()
    {
        Assert.Equal("E", H.Periodos[0].Materias[0].Calificacion);
        Assert.Equal("F", H.Periodos[1].Materias[1].Calificacion);
    }

    [Fact]
    public void LeeDatosDelPeriodo()
    {
        var p = H.Periodos[0];
        Assert.Equal("ESCUELA DE PRUEBA", p.Escuela);
        Assert.Equal("NUEVO INGRESO", p.TipoAlumno);
        Assert.Equal("NORMAL", p.EstadoAcademico);
    }

    [Fact]
    public void LeeTotalesDePeriodoYAcumulados()
    {
        Assert.Equal(new Core.Models.TotalesBanner(6, 3, 3, 6, 6, 1.00m), H.Periodos[1].TotalesPeriodo);
        Assert.Equal(new Core.Models.TotalesBanner(13, 10, 10, 13, 30, 2.31m), H.Periodos[1].TotalesAcumulados);
    }

    [Fact]
    public void LosPuntosDeLasMateriasCuadranConLosTotalesDelPeriodo()
    {
        foreach (var p in H.Periodos)
            Assert.Equal(p.TotalesPeriodo!.PuntosCalidad, p.Materias.Sum(m => m.PuntosCalidad));
    }

    [Fact]
    public void LeeTotalesGlobalesYDistingueElResumenSuperior()
    {
        Assert.Equal(new Core.Models.TotalesBanner(13, 10, 10, 13, 30, 2.31m), H.TotalGlobal);
        Assert.Equal(H.TotalGlobal, H.TotalInstitucion);
        Assert.Equal(new Core.Models.TotalesBanner(0, 0, 0, 0, 0, 0), H.TotalTransferido);
        // El resumen de arriba ("Institución") es otra fila y no debe confundirse con "Global".
        Assert.Equal(11m, H.ResumenSuperior!.HorasAprobadas);
    }

    [Fact]
    public void LeeCursosEnProgresoConSuOtroFormatoDeColumnas()
    {
        var cursos = H.EnProgreso[0].Cursos;
        Assert.Equal(new[] { "ISO400", "MAT102" }, cursos.Select(c => c.Codigo));
        Assert.Equal(4m, cursos[0].HorasCredito);
        Assert.Equal("ANALISIS Y DISENO", cursos[0].Titulo);
    }

    [Fact]
    public void LasTablasAnidadasNoGeneranMaterias()
    {
        Assert.Equal(3, H.Periodos[0].Materias.Count);
        Assert.Equal(2, H.Periodos[1].Materias.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("<html><body><p>Inicia sesión</p></body></html>")]
    public void LanzaExcepcionSiNoEsUnHistorico(string html) =>
        Assert.Throws<HistoricoParseException>(() => HistoricoParser.Parse(html));

    [Fact]
    public void LanzaExcepcionSiFaltanLosTotalesGlobales()
    {
        var html = Muestras.LeerSintetico().Replace("Global:", "Otro:");
        var ex = Assert.Throws<HistoricoParseException>(() => HistoricoParser.Parse(html));
        Assert.Contains("Global", ex.Message);
    }

    [Fact]
    public void LanzaExcepcionSiUnNumeroNoSePuedeLeer()
    {
        var html = Muestras.LeerSintetico().Replace("<td class=\"dddefault\">12.00</td>", "<td class=\"dddefault\">doce</td>");
        Assert.Throws<HistoricoParseException>(() => HistoricoParser.Parse(html));
    }
}
