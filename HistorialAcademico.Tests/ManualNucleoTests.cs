using HistorialAcademico.Core.Entities;
using HistorialAcademico.Core.Indice;
using HistorialAcademico.Core.Manual;
using HistorialAcademico.Core.Universidad;
using Xunit;

namespace HistorialAcademico.Tests;

internal static class ManualEjemplo
{
    public static readonly ReglasUniversidad Reglas = ReglasUniversidad.Unapec;

    public static readonly List<MateriaPensum> Pensum = new()
    {
        new() { Codigo = "ISO200", Nombre = "Programación I", Creditos = 4, Cuatrimestre = 1 },
        new() { Codigo = "MAT101", Nombre = "Cálculo I", Creditos = 3, Cuatrimestre = 1 },
        new() { Codigo = "ING301", Nombre = "Inglés III", Creditos = 2, Cuatrimestre = 2 },
        new() { Codigo = "E077", Nombre = "Electiva", Creditos = 3, Cuatrimestre = 3, EsElectiva = true },
    };

    public static MateriaManual M(int id, string codigo, string periodo, string nota = "") =>
        new() { Id = id, Codigo = codigo, Periodo = periodo, Calificacion = nota };

    /// <summary>Un período de Banner con sus materias y los acumulados que Banner publicaría.</summary>
    public static Periodo Banner(int orden, string nombre, decimal acumIntentadas, decimal acumPuntos, decimal acumPga, params (string Codigo, string Nota, int Creditos)[] materias)
    {
        var p = new Periodo { Id = orden, Orden = orden, Nombre = nombre, Nivel = "Grado" };
        foreach (var m in materias)
            p.Materias.Add(new MateriaCursada { Id = orden * 100 + p.Materias.Count, PeriodoId = orden, Codigo = m.Codigo, Calificacion = m.Nota, HorasCredito = m.Creditos, Nivel = "Grado" });
        p.AcumHorasIntentadas = acumIntentadas; p.AcumHorasAprobadas = acumIntentadas; p.AcumHorasGanadas = acumIntentadas; p.AcumHorasPga = acumPga;
        p.AcumPuntosCalidad = acumPuntos; p.AcumPga = acumPga == 0 ? 0 : Math.Round(acumPuntos / acumPga, 2);
        return p;
    }
}

/// <summary>Revisar lo que se escribe al registrar una materia a mano.</summary>
public class ValidadorManualTests
{
    private static (FilaManual? Fila, string? Error) V(string? codigo, string? periodo, string? nota = "") =>
        ValidadorManual.Normalizar(codigo, periodo, nota, ManualEjemplo.Pensum, ManualEjemplo.Reglas);

    [Fact]
    public void UnaMateriaCorrectaSeNormaliza()
    {
        var (fila, error) = V("  iso200 ", "ene-abr   2026", "b");

        Assert.Null(error);
        Assert.Equal(new FilaManual("ISO200", "ENE-ABR 2026", "B"), fila);   // el código como en el pénsum, el período y la letra de la escala
    }

    [Fact]
    public void SinCalificacionEsUnCursoEnProgreso()
    {
        Assert.Equal("", V("ISO200", "SEP-DIC 2026", null).Fila!.Calificacion);
        Assert.Equal("", V("ISO200", "SEP-DIC 2026", "   ").Fila!.Calificacion);
    }

    [Fact]
    public void LaExentaDeUnapecEsUnaCalificacionValida() => Assert.Equal("E", V("ISO200", "SEP-DIC 2026", "e").Fila!.Calificacion);

    [Theory]
    [InlineData(null, "Escribe el código")]
    [InlineData("  ", "Escribe el código")]
    [InlineData("XYZ999", "«XYZ999» no está en tu pénsum")]
    public void ElCodigoTieneQueEstarEnElPensum(string? codigo, string mensaje) => Assert.Contains(mensaje, V(codigo, "ENE-ABR 2026").Error);

    [Fact]
    public void SinPensumSeExplicaQueHayQueElegirLaCarrera()
    {
        var (_, error) = ValidadorManual.Normalizar("ISO200", "ENE-ABR 2026", "A", new List<MateriaPensum>(), ManualEjemplo.Reglas);

        Assert.Contains("Primero elige tu carrera", error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2026")]
    [InlineData("ENE 2026")]
    [InlineData("ENE-ABR")]
    [InlineData("PRIMAVERA 2026")]
    [InlineData("ENE-ABR 26")]
    [InlineData("ENE-ABR 1900")]
    [InlineData("ENE-ABR 2500")]
    public void ElPeriodoTieneQueEntenderse(string? periodo)
    {
        var (fila, error) = V("ISO200", periodo);

        Assert.Null(fila);
        Assert.Contains("período", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ElMensajeDelPeriodoDaUnEjemploYLosPeriodosDeLaUniversidad()
    {
        var error = V("ISO200", "PRIMAVERA 2026").Error!;

        Assert.Contains("ENE-ABR", error);
        Assert.Contains("MAY-AGO", error);
        Assert.Contains("SEP-DIC", error);
    }

    [Fact]
    public void UnaCalificacionQueNoExisteListaLasDeLaEscala()
    {
        var (fila, error) = V("ISO200", "ENE-ABR 2026", "Z");

        Assert.Null(fila);
        Assert.Contains("«Z» no existe", error);
        Assert.Contains("A, B, C, D, F, E", error);
        Assert.Contains("Déjala vacía", error);
    }
}

/// <summary>Leer el CSV con las materias tomadas y armar su plantilla.</summary>
public class CsvManualTests
{
    [Fact]
    public void LeeUnCsvConEncabezado()
    {
        var r = CsvManual.Leer("codigo,periodo,calificacion\nISO200,ENE-ABR 2026,A\nMAT101,ENE-ABR 2026,\n");

        Assert.Empty(r.Errores);
        Assert.Equal(new[] { ("ISO200", "ENE-ABR 2026", "A"), ("MAT101", "ENE-ABR 2026", "") }, r.Filas.Select(f => (f.Codigo, f.Periodo, f.Calificacion)));
        Assert.Equal(new[] { 2, 3 }, r.Filas.Select(f => f.Linea));   // las líneas se cuentan como las ve la persona
    }

    [Theory]
    [InlineData(",")]
    [InlineData(";")]
    [InlineData("\t")]
    public void AceptaComaPuntoYComaYTabulador(string separador)
    {
        var csv = string.Join(separador, "codigo", "periodo", "calificacion") + "\r\n" + string.Join(separador, "ISO200", "MAY-AGO 2025", "B");

        var r = CsvManual.Leer(csv);

        Assert.Empty(r.Errores);
        Assert.Equal(("ISO200", "MAY-AGO 2025", "B"), (r.Filas[0].Codigo, r.Filas[0].Periodo, r.Filas[0].Calificacion));
    }

    [Fact]
    public void ElEncabezadoPuedeEstarEnOtroOrdenConTildesYOtrosNombres()
    {
        var r = CsvManual.Leer("Nota;Período;Código\nA;ENE-ABR 2026;ISO200");

        Assert.Empty(r.Errores);
        Assert.Equal(("ISO200", "ENE-ABR 2026", "A"), (r.Filas[0].Codigo, r.Filas[0].Periodo, r.Filas[0].Calificacion));
    }

    [Fact]
    public void SinEncabezadoSeSuponeCodigoPeriodoYCalificacion()
    {
        var r = CsvManual.Leer("ISO200,ENE-ABR 2026,A");

        Assert.Equal(("ISO200", "ENE-ABR 2026", "A"), (r.Filas.Single().Codigo, r.Filas.Single().Periodo, r.Filas.Single().Calificacion));
    }

    [Fact]
    public void LaCalificacionEsOpcional()
    {
        var r = CsvManual.Leer("codigo,periodo\nISO200,ENE-ABR 2026");

        Assert.Empty(r.Errores);
        Assert.Equal("", r.Filas.Single().Calificacion);
    }

    [Fact]
    public void IgnoraLaMarcaUtf8LasLineasVaciasYLosComentarios()
    {
        var r = CsvManual.Leer("﻿codigo,periodo,calificacion\r\n\r\n# esto es una nota\r\nISO200,ENE-ABR 2026,A\r\n   \r\n  # otra\r\n");

        Assert.Empty(r.Errores);
        Assert.Equal("ISO200", r.Filas.Single().Codigo);
    }

    [Fact]
    public void RespetaLasComillas()
    {
        var r = CsvManual.Leer("codigo,periodo,calificacion\n\"ISO200\",\"ENE-ABR 2026\",\"A\"\n\"X,Y\",\"a \"\"b\"\" c\",");

        Assert.Equal("X,Y", r.Filas[1].Codigo);
        Assert.Equal("a \"b\" c", r.Filas[1].Periodo);
    }

    [Fact]
    public void UnaLineaSinPeriodoEsUnErrorConSuNumeroDeLinea()
    {
        var r = CsvManual.Leer("codigo,periodo,calificacion\nISO200,ENE-ABR 2026,A\nMAT101\n");

        Assert.Single(r.Filas);
        Assert.Contains("Línea 3", Assert.Single(r.Errores));
    }

    [Fact]
    public void UnEncabezadoSinLasColumnasNecesariasEsUnError()
    {
        var r = CsvManual.Leer("nota,curso\nA,ISO200");

        Assert.Empty(r.Filas);
        Assert.Contains("«codigo» y «periodo»", Assert.Single(r.Errores));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("codigo,periodo,calificacion\n")]
    [InlineData("# solo comentarios\n")]
    public void UnArchivoSinMateriasEsUnError(string? texto)
    {
        var r = CsvManual.Leer(texto);

        Assert.Empty(r.Filas);
        Assert.Contains("no tiene ninguna materia", Assert.Single(r.Errores));
    }

    [Fact]
    public void HayUnMaximoDeFilas()
    {
        var csv = "codigo,periodo,calificacion\n" + string.Join("\n", Enumerable.Range(0, CsvManual.MaxFilas + 5).Select(i => $"ISO{i},ENE-ABR 2026,A"));

        var r = CsvManual.Leer(csv);

        Assert.Equal(CsvManual.MaxFilas, r.Filas.Count);
        Assert.Contains("más de 2000 filas", Assert.Single(r.Errores));
    }

    [Fact]
    public void LaPlantillaSeLeeYSusEjemplosSonValidos()
    {
        var plantilla = CsvManual.Plantilla(ManualEjemplo.Pensum.Select(m => m.Codigo), ManualEjemplo.Reglas, 2027);

        var r = CsvManual.Leer(plantilla);

        Assert.Empty(r.Errores);
        Assert.Equal(3, r.Filas.Count);
        Assert.All(r.Filas, f => Assert.Null(ValidadorManual.Normalizar(f.Codigo, f.Periodo, f.Calificacion, ManualEjemplo.Pensum, ManualEjemplo.Reglas).Error));
        Assert.Contains("codigo,periodo,calificacion", plantilla);
        Assert.Contains("# ", plantilla);   // con instrucciones
        Assert.Contains("ENE-ABR 2026", plantilla);   // el ejemplo es del año anterior al indicado
    }

    [Fact]
    public void LaPlantillaSinPensumUsaCodigosDeEjemplo()
    {
        var r = CsvManual.Leer(CsvManual.Plantilla(Array.Empty<string>(), ManualEjemplo.Reglas, 2027));

        Assert.Equal(new[] { "ABC101", "ABC102", "ABC201" }, r.Filas.Select(f => f.Codigo));
    }
}

/// <summary>Mezclar el histórico de Banner con lo escrito a mano: Banner manda en los períodos que trae.</summary>
public class HistoricoManualTests
{
    private static ResultadoFusion F(IReadOnlyList<Periodo> banner, IReadOnlyList<MateriaManual> manuales, IReadOnlyList<CursoEnProgreso>? cursos = null) =>
        HistoricoManual.Fusionar(banner, cursos ?? new List<CursoEnProgreso>(), manuales, ManualEjemplo.Pensum, ManualEjemplo.Reglas);

    [Fact]
    public void SinMateriasManualesDevuelveLoMismo()
    {
        var banner = new List<Periodo> { ManualEjemplo.Banner(1, "ENE-ABR 2025", 4, 16, 4, ("ISO200", "A", 4)) };

        var r = F(banner, new List<MateriaManual>());

        Assert.Equal(banner, r.Periodos);
        Assert.Empty(r.Cursos);
        Assert.Empty(r.Estados);
    }

    [Fact]
    public void UnaMateriaConNotaCreaUnPeriodoEnSuLugarCronologico()
    {
        var banner = new List<Periodo>
        {
            ManualEjemplo.Banner(1, "ENE-ABR 2025", 3, 9, 3, ("MAT101", "B", 3)),
            ManualEjemplo.Banner(2, "SEP-DIC 2025", 5, 17, 5, ("ING301", "A", 2)),
        };

        var r = F(banner, new[] { ManualEjemplo.M(1, "ISO200", "MAY-AGO 2025", "A") });

        Assert.Equal(new[] { "ENE-ABR 2025", "MAY-AGO 2025", "SEP-DIC 2025" }, r.Periodos.Select(p => p.Nombre));
        Assert.Equal(new[] { 1, 2, 3 }, r.Periodos.Select(p => p.Orden));
        Assert.Equal(EstadoManual.Aplicada, r.Estados[1]);
    }

    [Fact]
    public void ElPeriodoManualTieneTotalesYAcumuladoPropios()
    {
        var banner = new List<Periodo> { ManualEjemplo.Banner(1, "ENE-ABR 2025", 3, 9, 3, ("MAT101", "B", 3)) };

        var r = F(banner, new[] { ManualEjemplo.M(1, "ISO200", "MAY-AGO 2025", "A"), ManualEjemplo.M(2, "ING301", "MAY-AGO 2025", "C") });
        var p = r.Periodos.Single(x => x.Nombre == "MAY-AGO 2025");

        Assert.Equal(2, p.Materias.Count);
        Assert.Equal((6m, 6m, 6m), (p.HorasIntentadas, p.HorasAprobadas, p.HorasPga));
        Assert.Equal(20m, p.PuntosCalidad);            // 4×4 + 2×2
        Assert.Equal(3.33m, p.Pga);
        Assert.Equal((9m, 9m), (p.AcumHorasIntentadas, p.AcumHorasPga));   // 3 de Banner + 6 de este período
        Assert.Equal(29m, p.AcumPuntosCalidad);        // 9 de Banner + 20
        Assert.Equal(3.22m, p.AcumPga);
    }

    [Fact]
    public void LaMateriaCursadaTraeLosDatosDelPensum()
    {
        var r = F(new List<Periodo>(), new[] { ManualEjemplo.M(7, "iso200", "ENE-ABR 2026", "B") });

        var m = r.Periodos.Single().Materias.Single();
        Assert.Equal(("ISO200", "ISO", "200", "Programación I"), (m.Codigo, m.Materia, m.Curso, m.Titulo));
        Assert.Equal((4m, 12m, "B", "Grado"), (m.HorasCredito, m.PuntosCalidad, m.Calificacion, m.Nivel));
        Assert.Equal(r.Periodos.Single().Id, m.PeriodoId);
    }

    [Fact]
    public void SinNotaEsUnCursoEnProgreso()
    {
        var r = F(new List<Periodo>(), new[] { ManualEjemplo.M(1, "MAT101", "SEP-DIC 2026") });

        Assert.Empty(r.Periodos);
        var c = Assert.Single(r.Cursos);
        Assert.Equal(("SEP-DIC 2026", "MAT101", "Cálculo I", 3m), (c.Periodo, c.Codigo, c.Titulo, c.HorasCredito));
        Assert.Equal(EstadoManual.Aplicada, r.Estados[1]);
    }

    [Fact]
    public void BannerMandaEnLosPeriodosQueTrae()
    {
        var banner = new List<Periodo> { ManualEjemplo.Banner(1, "ENE-ABR 2025", 3, 9, 3, ("MAT101", "B", 3)) };
        var cursos = new List<CursoEnProgreso> { new() { Periodo = "SEP-DIC 2025", Codigo = "ING301", HorasCredito = 2 } };

        var r = F(banner, new[]
        {
            ManualEjemplo.M(1, "ISO200", "ENE-ABR 2025", "A"),     // Banner ya trae este período
            ManualEjemplo.M(2, "ISO200", "sep-dic 2025"),          // Banner ya tiene cursos en progreso en este período
            ManualEjemplo.M(3, "ISO200", "MAY-AGO 2025", "A"),     // este sí cuenta
        }, cursos);

        Assert.Equal(EstadoManual.IgnoradaPorBanner, r.Estados[1]);
        Assert.Equal(EstadoManual.Aplicada, r.Estados[3]);
        Assert.Equal(new[] { "ENE-ABR 2025", "MAY-AGO 2025" }, r.Periodos.Select(p => p.Nombre));
        Assert.Single(r.Periodos.First().Materias);   // el período de Banner no recibió la materia manual
        Assert.Equal(new[] { "ING301" }, r.Cursos.Select(c => c.Codigo));
    }

    [Fact]
    public void UnPeriodoDeBannerConCursosEnProgresoTambienIgnoraLasMateriasConNota()
    {
        var cursos = new List<CursoEnProgreso> { new() { Periodo = "SEP-DIC 2026", Codigo = "ING301", HorasCredito = 2 } };

        var r = F(new List<Periodo>(), new[] { ManualEjemplo.M(1, "ISO200", "SEP-DIC 2026", "A") }, cursos);

        Assert.Equal(EstadoManual.IgnoradaPorBanner, r.Estados[1]);
        Assert.Empty(r.Periodos);
    }

    [Fact]
    public void UnCodigoQueYaNoEstaEnElPensumQuedaGuardadoSinContar()
    {
        var r = F(new List<Periodo>(), new[] { ManualEjemplo.M(1, "OLD100", "ENE-ABR 2026", "A") });

        Assert.Equal(EstadoManual.SinPensum, r.Estados[1]);
        Assert.Empty(r.Periodos);
        Assert.Empty(r.Cursos);
    }

    [Fact]
    public void SoloManualSeOrdenaPorAnioYPeriodoNoPorComoSeEscribio()
    {
        var r = F(new List<Periodo>(), new[]
        {
            ManualEjemplo.M(1, "ISO200", "SEP-DIC 2024", "A"),
            ManualEjemplo.M(2, "MAT101", "ENE-ABR 2025", "B"),
            ManualEjemplo.M(3, "ING301", "MAY-AGO 2024", "C"),
        });

        Assert.Equal(new[] { "MAY-AGO 2024", "SEP-DIC 2024", "ENE-ABR 2025" }, r.Periodos.Select(p => p.Nombre));
        Assert.Equal(new[] { 1, 2, 3 }, r.Periodos.Select(p => p.Orden));
        // El acumulado corre de un período al siguiente.
        Assert.Equal(new[] { 2m, 6m, 9m }, r.Periodos.Select(p => p.AcumHorasIntentadas));
    }

    [Fact]
    public void ElIndiceSeCalculaConLaMezcla()
    {
        var r = F(new List<Periodo>(), new[] { ManualEjemplo.M(1, "ISO200", "ENE-ABR 2026", "A"), ManualEjemplo.M(2, "MAT101", "ENE-ABR 2026", "B") });

        var indice = CalculadoraIndice.Calcular(r.Periodos, ManualEjemplo.Reglas.Escala);

        Assert.Equal(3.57m, indice.Global.Indice);   // (4×4 + 3×3) / 7
        Assert.Equal(7m, indice.Global.HorasAprobadas);
    }

    [Fact]
    public void UnaExentaAprobadaNoEntraEnElIndice()
    {
        var r = F(new List<Periodo>(), new[] { ManualEjemplo.M(1, "E077", "ENE-ABR 2026", "E"), ManualEjemplo.M(2, "MAT101", "ENE-ABR 2026", "B") });
        var p = r.Periodos.Single();

        Assert.Equal((6m, 6m, 3m), (p.HorasIntentadas, p.HorasAprobadas, p.HorasPga));
        Assert.Equal(3m, p.Pga);
    }

    [Fact]
    public void SiUnNombreDeBannerNoSeEntiendeBannerQuedaComoVeniaYLoManualVaDespues()
    {
        var banner = new List<Periodo> { ManualEjemplo.Banner(1, "Semestre raro", 3, 9, 3, ("MAT101", "B", 3)) };

        var r = F(banner, new[] { ManualEjemplo.M(1, "ISO200", "ENE-ABR 2020", "A") });

        Assert.Equal(new[] { "Semestre raro", "ENE-ABR 2020" }, r.Periodos.Select(p => p.Nombre));
    }

    [Theory]
    [InlineData("ISO700", "ISO", "700")]
    [InlineData("E077", "E", "077")]
    [InlineData("TFG", "TFG", "")]
    [InlineData("DEP", "DEP", "")]
    public void SeparaElCodigoEnMateriaYCurso(string codigo, string materia, string curso) =>
        Assert.Equal((materia, curso), HistoricoManual.SepararCodigo(codigo));
}
