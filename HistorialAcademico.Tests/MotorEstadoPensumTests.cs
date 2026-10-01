using HistorialAcademico.Core.Entities;
using HistorialAcademico.Core.Pensum;
using Xunit;

namespace HistorialAcademico.Tests;

/// <summary>Motor de estado del pénsum con el historial sintético de Fixtures/laboratorio-sintetico.md y docs/pensum_iso_unapec.csv.</summary>
public class MotorEstadoPensumTests
{
    private static readonly DatosLab.Lab Lab = DatosLab.Cargar();
    private static readonly List<MateriaPensum> Pensum = DatosLab.Pensum();

    private static ResultadoPensum Calcular(IEnumerable<Equivalencia>? eq = null) =>
        MotorEstadoPensum.Calcular(Pensum, Lab.Cursadas, Lab.EnProgreso, eq ?? Array.Empty<Equivalencia>());

    [Fact]
    public void LosDatosDelLaboratorioSeLeyeronCompletos()
    {
        Assert.Equal(7, Lab.Periodos.Count);
        Assert.Equal(50, Lab.Cursadas.Count);
        Assert.Equal(6, Lab.EnProgreso.Count);
        Assert.Equal(22, Lab.Faltantes.Count);
    }

    [Fact]
    public void LasMateriasFaltantesSonExactamenteLasDelLaboratorio()
    {
        var r = Calcular();
        var noCursadas = r.Materias
            .Where(e => e.Estado is EstadoMateria.Disponible or EstadoMateria.Bloqueada)
            .Select(e => e.Materia.Codigo)
            .OrderBy(c => c);
        Assert.Equal(Lab.Faltantes.OrderBy(c => c), noCursadas);
    }

    [Fact]
    public void LosCreditosCuadranConElResumenDelLaboratorio()
    {
        var r = Calcular();
        Assert.Equal(20, r.CreditosEnCurso);                 // laboratorio: 20 en curso
        Assert.Equal(68, r.CreditosFaltantes);               // laboratorio: 68 faltantes
        // El laboratorio dice 221 créditos en total, pero el CSV suma 218; por eso aprobados = 130 y no 133.
        Assert.Equal(218, r.CreditosTotales);
        Assert.Equal(130, r.CreditosAprobados);
        Assert.Equal(75, r.Materias.Count);
    }

    [Theory]
    [InlineData("ISO100", EstadoMateria.Aprobada, "B")]
    [InlineData("MAT131", EstadoMateria.Aprobada, "B")]
    [InlineData("ISO700", EstadoMateria.Aprobada, "A")]
    [InlineData("ESP106", EstadoMateria.Exenta, "E")]       // exentas: cuentan como aprobadas
    [InlineData("ING716", EstadoMateria.Exenta, "E")]
    [InlineData("ENG001", EstadoMateria.Exenta, "E")]
    [InlineData("ENG008", EstadoMateria.Aprobada, "B")]
    public void MarcaAprobadasYExentas(string codigo, EstadoMateria estado, string letra)
    {
        var e = Calcular().Buscar(codigo)!;
        Assert.Equal(estado, e.Estado);
        Assert.Equal(letra, e.Calificacion);
        Assert.True(e.CuentaComoAprobada);
    }

    [Theory]
    [InlineData("ADM535"), InlineData("INF900"), InlineData("ISO720"), InlineData("ISO735"), InlineData("ISO931"), InlineData("ISO934")]
    public void MarcaLosCursosEnProgresoComoEnCurso(string codigo)
    {
        var e = Calcular().Buscar(codigo)!;
        Assert.Equal(EstadoMateria.EnCurso, e.Estado);
        Assert.Equal("SEP-DIC 2026", e.Periodo);
        Assert.False(e.CuentaComoAprobada);
    }

    [Theory]
    [InlineData("ODEP")]     // sin prerrequisitos
    [InlineData("ING719")]   // ING717 exenta
    [InlineData("ISO625")]   // ISO515 aprobada
    [InlineData("IDI046")]   // IDI045 aprobada
    [InlineData("DER010"), InlineData("DER800"), InlineData("ISC835"), InlineData("INF610"), InlineData("PAS261"), InlineData("TFG")]
    [InlineData("E077")]     // 59 %: 130 de 218 créditos = 59.6 %
    public void MarcaComoDisponibles(string codigo)
    {
        var e = Calcular().Buscar(codigo)!;
        Assert.Equal(EstadoMateria.Disponible, e.Estado);
        Assert.Empty(e.Bloqueos);
    }

    [Theory]
    [InlineData("ISO800", "ISO735")]
    [InlineData("ISO725", "ISO720")]
    [InlineData("ISO912", "ISO725")]
    [InlineData("ISO900", "ISO800")]
    [InlineData("ISO937", "ISO931")]
    [InlineData("ISO936", "ISO934")]
    [InlineData("ISO945", "ISO937")]
    [InlineData("ISO940", "ISO912")]
    public void MarcaComoBloqueadasIndicandoQueFalta(string codigo, string falta)
    {
        var e = Calcular().Buscar(codigo)!;
        Assert.Equal(EstadoMateria.Bloqueada, e.Estado);
        Assert.Contains(e.Bloqueos, b => b.Contains(falta));
    }

    [Fact]
    public void UnPrerrequisitoEnCursoSeIndicaPeroNoDesbloquea()
    {
        var e = Calcular().Buscar("ISO800")!;    // requiere ISO735, que está en curso
        Assert.Equal(EstadoMateria.Bloqueada, e.Estado);
        Assert.Contains("en curso", e.Bloqueos.Single());
    }

    [Fact]
    public void LasElectivasYElSeminarioDependenDeSusPorcentajesYPrerrequisitos()
    {
        var r = Calcular();

        // Electiva II: falta la Electiva I y falta el 67 % (130/218 = 59.6 %)
        var e078 = r.Buscar("E078")!;
        Assert.Equal(EstadoMateria.Bloqueada, e078.Estado);
        Assert.Contains(e078.Bloqueos, b => b.Contains("E077"));
        Assert.Contains(e078.Bloqueos, b => b.Contains("67%"));

        // Seminario de Grado: SOC253 aprobada, pero falta el 90 %
        var soc281 = r.Buscar("SOC281")!;
        Assert.Equal(EstadoMateria.Bloqueada, soc281.Estado);
        Assert.DoesNotContain(soc281.Bloqueos, b => b.Contains("SOC253"));
        Assert.Contains(soc281.Bloqueos, b => b.Contains("90%"));

        Assert.Equal(EstadoMateria.Bloqueada, r.Buscar("E079")!.Estado);
    }

    [Fact]
    public void ElPorcentajeSeCumpleAlLlegarAlMinimo()
    {
        // 59 % de 218 créditos = 128.6 → con 129 créditos aprobados se cumple; con 128 no.
        MateriaCursada Cursada(string cod, string letra) => new() { Codigo = cod, Calificacion = letra };
        var pensum = new List<MateriaPensum>
        {
            new() { Codigo = "AAA100", Nombre = "Base", Creditos = 129, Cuatrimestre = 1 },
            new() { Codigo = "BBB100", Nombre = "Resto", Creditos = 89, Cuatrimestre = 1 },
            new() { Codigo = "E077", Nombre = "Electiva I", Creditos = 0, Cuatrimestre = 2, Prerrequisitos = "59% créditos aprobados", EsElectiva = true },
        };
        var conAprobada = MotorEstadoPensum.Calcular(pensum, new[] { Cursada("AAA100", "B") }, Array.Empty<CursoEnProgreso>(), Array.Empty<Equivalencia>());
        Assert.Equal(EstadoMateria.Disponible, conAprobada.Buscar("E077")!.Estado);   // 129/218 = 59.17 %

        var sinAprobada = MotorEstadoPensum.Calcular(pensum, Array.Empty<MateriaCursada>(), Array.Empty<CursoEnProgreso>(), Array.Empty<Equivalencia>());
        Assert.Equal(EstadoMateria.Bloqueada, sinAprobada.Buscar("E077")!.Estado);
    }

    // ── Equivalencias ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void UnaEquivalenciaAprueba_LasMateriasDelPensum()
    {
        // Solo cursó ING701 (plan anterior, A). Con ING701 → ING716 + ING717 quedan aprobadas las dos.
        var pensum = Pensum.Where(m => m.Codigo is "ING716" or "ING717" or "ING719" or "MAT131").ToList();
        var cursadas = new[] { new MateriaCursada { Codigo = "ING701", Calificacion = "A", Periodo = new Periodo { Nombre = "SEP-DIC 2024" } } };

        var sin = MotorEstadoPensum.Calcular(pensum, cursadas, Array.Empty<CursoEnProgreso>(), Array.Empty<Equivalencia>());
        Assert.NotEqual(EstadoMateria.Aprobada, sin.Buscar("ING716")!.Estado);

        var con = MotorEstadoPensum.Calcular(pensum, cursadas, Array.Empty<CursoEnProgreso>(), EquivalenciasIniciales.Valores);
        foreach (var codigo in new[] { "ING716", "ING717" })
        {
            var e = con.Buscar(codigo)!;
            Assert.Equal(EstadoMateria.Aprobada, e.Estado);
            Assert.Equal("A", e.Calificacion);
            Assert.Equal("ING701", e.PorEquivalencia);
            Assert.Equal("SEP-DIC 2024", e.Periodo);
        }
        // ...y ING719 (prerrequisito ING717) pasa a Disponible gracias a la equivalencia.
        Assert.Equal(EstadoMateria.Disponible, con.Buscar("ING719")!.Estado);
    }

    [Fact]
    public void LaAprobacionDirectaTienePrioridadSobreLaEquivalencia()
    {
        // En los datos reales ING716 y ING717 están directas como E, y ING701 (A) también las cubre.
        var r = Calcular(EquivalenciasIniciales.Valores);
        var e = r.Buscar("ING716")!;
        Assert.Equal(EstadoMateria.Exenta, e.Estado);
        Assert.Equal("E", e.Calificacion);
        Assert.Null(e.PorEquivalencia);
    }

    [Fact]
    public void LasEquivalenciasSinDestinoNoCambianNada()
    {
        var con = Calcular(EquivalenciasIniciales.Valores);
        var sin = Calcular();
        Assert.Equal(sin.CreditosAprobados, con.CreditosAprobados);
        Assert.Equal(sin.Materias.Select(m => m.Estado), con.Materias.Select(m => m.Estado));
    }

    [Fact]
    public void UnCursoEnProgresoPuedeCubrirseConUnaEquivalencia()
    {
        var pensum = new List<MateriaPensum> { new() { Codigo = "NEW200", Nombre = "Nueva", Creditos = 3, Cuatrimestre = 2 } };
        var eq = new[] { new Equivalencia { CodigoBanner = "OLD200", CodigoPensum = "NEW200" } };
        var r = MotorEstadoPensum.Calcular(pensum, Array.Empty<MateriaCursada>(),
            new[] { new CursoEnProgreso { Codigo = "OLD200", Periodo = "SEP-DIC 2026" } }, eq);
        var e = r.Buscar("NEW200")!;
        Assert.Equal(EstadoMateria.EnCurso, e.Estado);
        Assert.Equal("OLD200", e.PorEquivalencia);
    }

    // ── Reintentos y F ────────────────────────────────────────────────────────────────────

    [Fact]
    public void UnaMateriaReprobadaConFNoCuentaComoAprobada()
    {
        var pensum = new List<MateriaPensum>
        {
            new() { Codigo = "MAT100", Nombre = "Base", Creditos = 3, Cuatrimestre = 1 },
            new() { Codigo = "MAT200", Nombre = "Siguiente", Creditos = 3, Cuatrimestre = 2, Prerrequisitos = "MAT100" },
        };
        var r = MotorEstadoPensum.Calcular(pensum, new[] { new MateriaCursada { Codigo = "MAT100", Calificacion = "F" } },
            Array.Empty<CursoEnProgreso>(), Array.Empty<Equivalencia>());
        Assert.Equal(EstadoMateria.Disponible, r.Buscar("MAT100")!.Estado);   // puede repetirla
        Assert.Equal(EstadoMateria.Bloqueada, r.Buscar("MAT200")!.Estado);
    }

    [Fact]
    public void SiRepitioUnaMateriaUsaLaMejorCalificacion()
    {
        var pensum = new List<MateriaPensum> { new() { Codigo = "MAT100", Nombre = "Base", Creditos = 3, Cuatrimestre = 1 } };
        var r = MotorEstadoPensum.Calcular(pensum,
            new[] { new MateriaCursada { Codigo = "MAT100", Calificacion = "F" }, new MateriaCursada { Codigo = "MAT100", Calificacion = "B" } },
            Array.Empty<CursoEnProgreso>(), Array.Empty<Equivalencia>());
        Assert.Equal(EstadoMateria.Aprobada, r.Buscar("MAT100")!.Estado);
        Assert.Equal("B", r.Buscar("MAT100")!.Calificacion);
    }
}
