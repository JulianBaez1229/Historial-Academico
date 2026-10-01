using HistorialAcademico.Core.Entities;
using HistorialAcademico.Core.Indice;
using HistorialAcademico.Core.Pensum;
using Xunit;

namespace HistorialAcademico.Tests;

public class ReglasIndiceTests
{
    [Theory]
    [InlineData("A", 4), InlineData("B", 3), InlineData("C", 2), InlineData("D", 1), InlineData("F", 0), InlineData("a", 4)]
    public void EscalaSinMasNiMenos(string letra, int puntos) => Assert.Equal(puntos, ReglasIndice.PuntosPorLetra(letra));

    [Theory]
    [InlineData("E"), InlineData("A+"), InlineData(""), InlineData(null)]
    public void LaExentaYLasLetrasDesconocidasNoTienenPuntos(string? letra)
    {
        Assert.Null(ReglasIndice.PuntosPorLetra(letra));
        Assert.False(ReglasIndice.CuentaParaIndice(letra));
    }

    [Fact]
    public void LaExentaCuentaComoAprobadaPeroNoParaElIndice()
    {
        Assert.True(ReglasIndice.EsExenta("E"));
        Assert.True(ReglasIndice.CuentaComoAprobada("E"));
        Assert.False(ReglasIndice.CuentaParaIndice("E"));
        Assert.False(ReglasIndice.CuentaComoAprobada("F"));
        Assert.True(ReglasIndice.CuentaParaIndice("F"));   // la F sí baja el índice
    }
}

public class CalculadoraIndiceTests
{
    private static readonly DatosLab.Lab Lab = DatosLab.Cargar();

    [Fact]
    public void ElIndiceGlobalDelLaboratorioEs304()
    {
        var g = CalculadoraIndice.Calcular(Lab.Periodos).Global;
        Assert.Equal(143m, g.HorasAprobadas);
        Assert.Equal(134m, g.HorasPga);
        Assert.Equal(408m, g.PuntosCalidad);
        Assert.Equal(3.04m, g.Indice);
    }

    [Fact]
    public void ElPgaDeCadaPeriodoCoincideConElLaboratorio()
    {
        var r = CalculadoraIndice.Calcular(Lab.Periodos);
        // Calculados a mano (fuera de C#) a partir de Fixtures/laboratorio-sintetico.md.
        Assert.Equal(new[] { 3.14m, 3.33m, 2.78m, 2.71m, 2.68m, 3.53m, 3.27m }, r.Periodos.Select(p => p.DelPeriodo.Indice));
        Assert.Equal(new[] { 3.14m, 3.23m, 3.10m, 3.00m, 2.93m, 3.02m, 3.04m }, r.Periodos.Select(p => p.Acumulado.Indice));
    }

    [Fact]
    public void LasMateriasExentasYDeCeroCreditosNoAfectanElIndice()
    {
        var conE = new[] { new MateriaCursada { Codigo = "A1", Calificacion = "A", HorasCredito = 3 } };
        var mas = conE.Append(new MateriaCursada { Codigo = "B1", Calificacion = "E", HorasCredito = 3 })
                      .Append(new MateriaCursada { Codigo = "ENG001", Calificacion = "B", HorasCredito = 0 });
        var a = CalculadoraIndice.Totales(conE);
        var b = CalculadoraIndice.Totales(mas);
        Assert.Equal(a.Indice, b.Indice);
        Assert.Equal(a.PuntosCalidad, b.PuntosCalidad);
        Assert.Equal(a.HorasPga, b.HorasPga);
        Assert.Equal(a.HorasAprobadas + 3, b.HorasAprobadas);   // la E sí suma horas aprobadas
    }

    [Fact]
    public void ElRedondeoEsAlejandoseDelCero()
    {
        // 3 créditos A + 3 créditos B + 1 C = 12+9+2 = 23 / 7 = 3.2857 → 3.29 ; caso límite x.xx5 → hacia arriba
        Assert.Equal(3.29m, CalculadoraIndice.Totales(new[]
        {
            new MateriaCursada { Calificacion = "A", HorasCredito = 3 },
            new MateriaCursada { Calificacion = "B", HorasCredito = 3 },
            new MateriaCursada { Calificacion = "C", HorasCredito = 1 },
        }).Indice);
        Assert.Equal(2.51m, ReglasIndice.Redondear(2.505m));
    }

    [Fact]
    public void SinHorasElIndiceEsCeroYNoDivideEntreCero() =>
        Assert.Equal(0m, CalculadoraIndice.Totales(new[] { new MateriaCursada { Calificacion = "E", HorasCredito = 3 } }).Indice);

    [Fact]
    public void ElCalculoCoincideConLosTotalesQuePublicaBanner()
    {
        var calculado = CalculadoraIndice.Calcular(Lab.Periodos);
        var banner = Lab.Periodos.Select(p =>
        {
            var t = calculado.Periodos.Single(x => x.Periodo == p.Nombre);
            return new Periodo
            {
                Orden = p.Orden, Nombre = p.Nombre,
                HorasIntentadas = t.DelPeriodo.HorasIntentadas, HorasAprobadas = t.DelPeriodo.HorasAprobadas,
                HorasPga = t.DelPeriodo.HorasPga, PuntosCalidad = t.DelPeriodo.PuntosCalidad, Pga = t.DelPeriodo.Indice,
                AcumPga = t.Acumulado.Indice,
            };
        }).ToList();
        var alumno = new DatosAlumno { TotalHorasAprobadas = 143, TotalHorasPga = 134, TotalPuntosCalidad = 408, TotalPga = 3.04m };

        Assert.Empty(ComparadorConBanner.Comparar(calculado, banner, alumno));
    }

    [Fact]
    public void AdviertePorCadaDiferenciaConBanner()
    {
        var calculado = CalculadoraIndice.Calcular(Lab.Periodos);
        var banner = new List<Periodo> { new() { Orden = 1, Nombre = "MAY-AGO 2024", HorasPga = 22, PuntosCalidad = 90, Pga = 4.09m } };
        var alumno = new DatosAlumno { TotalHorasAprobadas = 143, TotalHorasPga = 134, TotalPuntosCalidad = 450, TotalPga = 3.36m };

        var avisos = ComparadorConBanner.Comparar(calculado, banner, alumno);

        Assert.Contains(avisos, a => a.StartsWith("MAY-AGO 2024: Puntos de Calidad calculado = 69"));
        Assert.Contains(avisos, a => a.StartsWith("Global: Puntos de Calidad calculado = 408 pero Banner publica 450"));
        Assert.Contains(avisos, a => a.StartsWith("Global: Índice calculado = 3.04 pero Banner publica 3.36"));
        Assert.DoesNotContain(avisos, a => a.StartsWith("Global: Horas Aprobadas"));
    }
}

public class PrerrequisitoParserTests
{
    [Fact]
    public void SinPrerrequisitosDevuelveListaVacia()
    {
        Assert.Empty(PrerrequisitoParser.Parse(null));
        Assert.Empty(PrerrequisitoParser.Parse("  "));
    }

    [Fact]
    public void LeeUnCodigo() => Assert.Equal(new[] { Requisito.DeMateria("ISO200") }, PrerrequisitoParser.Parse("iso200"));

    [Fact]
    public void LeeUnPorcentaje() => Assert.Equal(new[] { Requisito.DePorcentaje(59) }, PrerrequisitoParser.Parse("59% créditos aprobados"));

    [Fact]
    public void LeeCodigoMasPorcentajeSeparadosPorPuntoYComa()
    {
        Assert.Equal(new[] { Requisito.DeMateria("E077"), Requisito.DePorcentaje(67) }, PrerrequisitoParser.Parse("E077; 67% créditos aprobados"));
        Assert.Equal(new[] { Requisito.DeMateria("SOC253"), Requisito.DePorcentaje(90) }, PrerrequisitoParser.Parse("SOC253; 90% créditos aprobados"));
    }

    [Theory]
    [InlineData("ISO 200 y algo")]
    [InlineData("0% créditos")]
    [InlineData("150% créditos")]
    [InlineData("créditos aprobados")]
    public void RechazaTextoQueNoEntiende(string texto)
    {
        Assert.False(PrerrequisitoParser.TryParse(texto, out _, out var error));
        Assert.NotNull(error);
        Assert.Throws<FormatException>(() => PrerrequisitoParser.Parse(texto));
    }
}

public class PensumCsvParserTests
{
    private const string Encabezado = "codigo,nombre,creditos,cuatrimestre,prerrequisitos,es_electiva\n";

    [Fact]
    public void LeeElCsvRealDelPensum()
    {
        var r = PensumCsvParser.Parse(File.ReadAllText(DatosLab.RutaCsv));

        Assert.True(r.EsValido, string.Join(" | ", r.Errores));
        Assert.Empty(r.Advertencias);
        Assert.Equal(75, r.Materias.Count);
        Assert.Equal(218, r.Materias.Sum(m => m.Creditos));
        Assert.Equal(Enumerable.Range(1, 12), r.Materias.Select(m => m.Cuatrimestre).Distinct().OrderBy(c => c));
        Assert.Equal(new[] { "E077", "E078", "E079" }, r.Materias.Where(m => m.EsElectiva).Select(m => m.Codigo));
        Assert.Equal("E077; 67% créditos aprobados", r.Materias.Single(m => m.Codigo == "E078").Prerrequisitos);
        Assert.Null(r.Materias.Single(m => m.Codigo == "ISO100").Prerrequisitos);
        Assert.Equal("Desarrollo de Software con Tecnologías Propietarias y Open Source I", r.Materias.Single(m => m.Codigo == "ISO615").Nombre);
    }

    [Fact]
    public void AceptaBomYFinesDeLineaDeWindows()
    {
        var r = PensumCsvParser.Parse("﻿" + Encabezado.Replace("\n", "\r\n") + "ISO100,Fundamentos,5,1,,false\r\n");
        Assert.True(r.EsValido);
        Assert.Equal("ISO100", r.Materias.Single().Codigo);
    }

    [Fact]
    public void RespetaCampoEntreComillasConComa()
    {
        var r = PensumCsvParser.Parse(Encabezado + "ISO100,\"Fundamentos, Informática\",5,1,,false\n");
        Assert.Equal("Fundamentos, Informática", r.Materias.Single().Nombre);
    }

    [Theory]
    [InlineData("", "vacío")]
    [InlineData("codigo,nombre\nISO100,X\n", "Encabezado")]
    public void RechazaArchivosSinElFormatoEsperado(string csv, string texto)
    {
        var r = PensumCsvParser.Parse(csv);
        Assert.False(r.EsValido);
        Assert.Contains(r.Errores, e => e.Contains(texto));
    }

    [Theory]
    [InlineData("ISO100,X,cinco,1,,false", "créditos inválidos")]
    [InlineData("ISO100,X,5,13,,false", "cuatrimestre")]
    [InlineData("ISO100,X,5,0,,false", "cuatrimestre")]
    [InlineData("ISO100,X,5,1,,quizás", "es_electiva")]
    [InlineData("ISO100,X,5,1,ISO 200 raro,false", "Prerrequisito no reconocido")]
    [InlineData(",X,5,1,,false", "falta el código")]
    [InlineData("ISO100,,5,1,,false", "falta el nombre")]
    [InlineData("ISO100,X,5,1", "columnas")]
    public void ReportaLaLineaConError(string fila, string texto)
    {
        var r = PensumCsvParser.Parse(Encabezado + fila + "\n");
        Assert.False(r.EsValido);
        var error = Assert.Single(r.Errores);
        Assert.StartsWith("Línea 2", error);
        Assert.Contains(texto, error);
    }

    [Fact]
    public void RechazaCodigosRepetidos()
    {
        var r = PensumCsvParser.Parse(Encabezado + "ISO100,A,5,1,,false\nISO100,B,5,1,,false\n");
        Assert.Contains(r.Errores, e => e.Contains("Línea 3") && e.Contains("repetido"));
    }

    [Fact]
    public void AdviertePeroNoBloqueaCuandoUnPrerrequisitoNoExiste()
    {
        var r = PensumCsvParser.Parse(Encabezado + "ISO200,A,5,2,ISO999,false\n");
        Assert.True(r.EsValido);
        Assert.Contains(r.Advertencias, a => a.Contains("ISO200") && a.Contains("ISO999"));
    }
}
