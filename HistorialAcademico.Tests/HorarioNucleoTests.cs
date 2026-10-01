using HistorialAcademico.Core.Entities;
using HistorialAcademico.Core.Horarios;

namespace HistorialAcademico.Tests;

/// <summary>Choques de horario y grilla semanal: la lógica pura, sin base de datos ni pantallas.</summary>
public class HorarioNucleoTests
{
    private static BloqueBanner Bloque(DiasSemana dias, string inicio, string fin, string? desde = null, string? hasta = null) => new()
    {
        Dias = dias,
        Inicio = inicio.Length == 0 ? null : TimeOnly.Parse(inicio),
        Fin = fin.Length == 0 ? null : TimeOnly.Parse(fin),
        FechaInicio = desde is null ? null : DateOnly.Parse(desde),
        FechaFin = hasta is null ? null : DateOnly.Parse(hasta),
    };

    private static SeccionBanner Sec(string codigo, string seccion, params BloqueBanner[] bloques) => new()
    {
        Periodo = "202710", Nrc = codigo + seccion, Codigo = codigo, Seccion = seccion, Titulo = "PRUEBA " + codigo, Creditos = 3, Bloques = bloques.ToList(),
    };

    private const DiasSemana MJ = DiasSemana.Martes | DiasSemana.Jueves;

    // ── Solapes ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void DosBloquesDelMismoDiaQueSeCruzanChocanEnLaFranjaComun()
    {
        var a = Bloque(MJ, "08:00", "10:00");
        var b = Bloque(DiasSemana.Jueves | DiasSemana.Viernes, "09:00", "11:00");

        Assert.True(DetectorChoques.Solapan(a, b, out var dias, out var desde, out var hasta));
        Assert.Equal(DiasSemana.Jueves, dias);                     // solo el día que comparten
        Assert.Equal((new TimeOnly(9, 0), new TimeOnly(10, 0)), (desde, hasta));
    }

    [Fact]
    public void UnaClaseQueTerminaCuandoEmpiezaOtraNoEsUnChoque()
    {
        Assert.False(DetectorChoques.Solapan(Bloque(MJ, "08:00", "10:00"), Bloque(MJ, "10:00", "12:00"), out _, out _, out _));
    }

    [Fact]
    public void UnBloqueDentroDeOtroChocaConLaFranjaMasCorta()
    {
        Assert.True(DetectorChoques.Solapan(Bloque(MJ, "08:00", "12:00"), Bloque(MJ, "09:00", "10:00"), out _, out var d, out var h));
        Assert.Equal((new TimeOnly(9, 0), new TimeOnly(10, 0)), (d, h));
    }

    [Fact]
    public void DiasDistintosNoChocan() =>
        Assert.False(DetectorChoques.Solapan(Bloque(MJ, "08:00", "10:00"), Bloque(DiasSemana.Lunes | DiasSemana.Miercoles, "08:00", "10:00"), out _, out _, out _));

    [Fact]
    public void LaParteVirtualSinDiaFijoNuncaChoca()
    {
        var virtual_ = Bloque(DiasSemana.Ninguno, "08:00", "10:00");
        Assert.False(DetectorChoques.Solapan(virtual_, Bloque(MJ, "08:00", "10:00"), out _, out _, out _));
        Assert.False(DetectorChoques.Solapan(virtual_, virtual_, out _, out _, out _));
    }

    [Fact]
    public void UnBloqueSinHorasNoChoca() =>
        Assert.False(DetectorChoques.Solapan(Bloque(MJ, "", ""), Bloque(MJ, "08:00", "10:00"), out _, out _, out _));

    [Fact]
    public void LosBloquesConFechasQueNoSeCruzanNoChocan()
    {
        var primeraMitad = Bloque(MJ, "08:00", "10:00", "2027-01-05", "2027-02-20");
        var segundaMitad = Bloque(MJ, "08:00", "10:00", "2027-02-23", "2027-04-25");
        var todoElPeriodo = Bloque(MJ, "09:00", "11:00", "2027-01-05", "2027-04-25");

        Assert.False(DetectorChoques.Solapan(primeraMitad, segundaMitad, out _, out _, out _));
        Assert.True(DetectorChoques.Solapan(primeraMitad, todoElPeriodo, out _, out _, out _));
        Assert.True(DetectorChoques.Solapan(primeraMitad, Bloque(MJ, "08:00", "10:00"), out _, out _, out _));   // sin fechas: se asume todo el período
    }

    [Fact]
    public void EntreDevuelveUnChoquePorCadaParejaDeBloquesQueCoincide()
    {
        var a = Sec("ISO625", "1", Bloque(DiasSemana.Martes, "08:00", "10:00"), Bloque(DiasSemana.Jueves, "08:00", "10:00"));
        var b = Sec("ISO800", "2", Bloque(MJ, "09:00", "11:00"));
        var c = Sec("ADM103", "1", Bloque(DiasSemana.Lunes, "08:00", "10:00"));

        var choques = DetectorChoques.Entre(new[] { a, b, c });

        Assert.Equal(2, choques.Count);
        Assert.All(choques, x => Assert.Equal((a, b), (x.A, x.B)));
        Assert.Equal(new[] { DiasSemana.Martes, DiasSemana.Jueves }, choques.Select(x => x.Dias));
        Assert.Empty(DetectorChoques.Entre(new[] { a, c }));
        Assert.Empty(DetectorChoques.Entre(new[] { a }));
        Assert.Empty(DetectorChoques.Entre(Array.Empty<SeccionBanner>()));
    }

    // ── Horas no disponibles ──────────────────────────────────────────────────────────────

    private static BloqueNoDisponible Nd(DiasSemana dia, int desdeHora, int hastaHora) => new() { Dia = dia, DesdeMin = desdeHora * 60, HastaMin = hastaHora * 60 };

    [Fact]
    public void UnaSeccionQueSeCruzaConUnaHoraNoDisponibleChoca()
    {
        var s = Sec("ISO625", "1", Bloque(MJ, "08:00", "10:00"));

        Assert.True(DetectorChoques.ChocaConNoDisponibles(s, new[] { Nd(DiasSemana.Martes, 9, 17) }));
        Assert.Equal(new[] { DiasSemana.Martes }, DetectorChoques.QueChocanConNoDisponibles(s, new[] { Nd(DiasSemana.Martes, 9, 17), Nd(DiasSemana.Jueves, 14, 18), Nd(DiasSemana.Lunes, 8, 10) }).Select(x => x.Dia));
    }

    [Theory]
    [InlineData(DiasSemana.Martes, 10, 17)]     // la clase termina justo cuando empieza lo no disponible
    [InlineData(DiasSemana.Martes, 6, 8)]       // y al revés
    [InlineData(DiasSemana.Lunes, 8, 10)]       // otro día
    public void LoQueSoloRozaOEstaEnOtroDiaNoChoca(DiasSemana dia, int desde, int hasta) =>
        Assert.False(DetectorChoques.ChocaConNoDisponibles(Sec("ISO625", "1", Bloque(MJ, "08:00", "10:00")), new[] { Nd(dia, desde, hasta) }));

    [Fact]
    public void LaParteVirtualNoChocaConLasHorasNoDisponibles() =>
        Assert.False(DetectorChoques.ChocaConNoDisponibles(Sec("ISO625", "1", Bloque(DiasSemana.Ninguno, "08:00", "10:00")), new[] { Nd(DiasSemana.Martes, 0, 23) }));

    [Fact]
    public void SinHorasNoDisponiblesNadaChoca() =>
        Assert.False(DetectorChoques.ChocaConNoDisponibles(Sec("ISO625", "1", Bloque(MJ, "08:00", "10:00")), Array.Empty<BloqueNoDisponible>()));

    // ── Grilla semanal ────────────────────────────────────────────────────────────────────

    [Fact]
    public void LaGrillaVaDeLunesASabadoYDeLas7ALas21PorOmision()
    {
        var g = GrillaSemanal.Construir(new[] { Sec("ISO625", "1", Bloque(MJ, "08:00", "10:00")) });

        Assert.Equal(new[] { DiasSemana.Lunes, DiasSemana.Martes, DiasSemana.Miercoles, DiasSemana.Jueves, DiasSemana.Viernes, DiasSemana.Sabado }, g.Dias);
        Assert.Equal((7, 21), (g.HoraInicio, g.HoraFin));
        Assert.Equal(14 * 60, g.MinutosTotales);
    }

    [Fact]
    public void LaGrillaSeAmpliaParaLasClasesTempranasOTardias()
    {
        var g = GrillaSemanal.Construir(new[]
        {
            Sec("ISO625", "1", Bloque(DiasSemana.Lunes, "06:00", "07:00")),
            Sec("ISO800", "1", Bloque(DiasSemana.Lunes, "21:00", "22:30")),
        });

        Assert.Equal((6, 23), (g.HoraInicio, g.HoraFin));   // 22:30 obliga a llegar a las 23
    }

    [Fact]
    public void ElDomingoSoloApareceSiHayAlgoEseDia()
    {
        Assert.DoesNotContain(DiasSemana.Domingo, GrillaSemanal.Construir(new[] { Sec("ISO625", "1", Bloque(MJ, "08:00", "10:00")) }).Dias);
        var g = GrillaSemanal.Construir(new[] { Sec("ISO625", "1", Bloque(DiasSemana.Domingo, "08:00", "10:00")) });
        Assert.Equal(DiasSemana.Domingo, g.Dias[^1]);
        Assert.Equal(7, g.Dias.Count);
    }

    [Fact]
    public void UnBloqueDeVariosDiasGeneraUnaCeldaPorDia()
    {
        var g = GrillaSemanal.Construir(new[] { Sec("ISO625", "3", Bloque(MJ, "08:00", "10:00")) });

        Assert.Equal(new[] { DiasSemana.Martes, DiasSemana.Jueves }, g.Celdas.Select(c => c.Dia));
        Assert.All(g.Celdas, c =>
        {
            Assert.Equal((8 * 60, 10 * 60, TipoCelda.Clase, "ISO625-3", false), (c.InicioMin, c.FinMin, c.Tipo, c.Etiqueta, c.ConChoque));
            Assert.Equal((0, 1), (c.Carril, c.Carriles));
        });
    }

    [Fact]
    public void LasSeccionesQueChocanSeMarcanAmbasYLasOtrasNo()
    {
        var a = Sec("ISO625", "1", Bloque(MJ, "08:00", "10:00"));
        var b = Sec("ISO800", "2", Bloque(MJ, "09:00", "11:00"));
        var c = Sec("ADM103", "1", Bloque(DiasSemana.Lunes, "08:00", "10:00"));

        var g = GrillaSemanal.Construir(new[] { a, b, c });

        Assert.Single(g.Choques.Select(x => (x.A, x.B)).Distinct());
        Assert.All(g.Celdas.Where(x => x.Etiqueta is "ISO625-1" or "ISO800-2"), x => Assert.True(x.ConChoque));
        Assert.All(g.Celdas.Where(x => x.Etiqueta == "ADM103-1"), x => Assert.False(x.ConChoque));
    }

    [Fact]
    public void LasClasesQueSeCruzanSeReparteElAnchoDelDia()
    {
        var g = GrillaSemanal.Construir(new[]
        {
            Sec("A", "1", Bloque(DiasSemana.Lunes, "08:00", "10:00")),
            Sec("B", "1", Bloque(DiasSemana.Lunes, "09:00", "11:00")),
            Sec("C", "1", Bloque(DiasSemana.Lunes, "10:00", "12:00")),   // empieza cuando A termina: reutiliza su carril
            Sec("D", "1", Bloque(DiasSemana.Lunes, "14:00", "15:00")),   // solo, aparte
        });

        var por = g.Celdas.ToDictionary(c => c.Etiqueta);
        Assert.Equal((0, 2), (por["A-1"].Carril, por["A-1"].Carriles));
        Assert.Equal((1, 2), (por["B-1"].Carril, por["B-1"].Carriles));
        Assert.Equal((0, 2), (por["C-1"].Carril, por["C-1"].Carriles));
        Assert.Equal((0, 1), (por["D-1"].Carril, por["D-1"].Carriles));   // no hereda el reparto del grupo anterior
    }

    [Fact]
    public void LasHorasNoDisponiblesVanComoCeldasDeFondoSinCarriles()
    {
        var g = GrillaSemanal.Construir(new[] { Sec("ISO625", "1", Bloque(DiasSemana.Lunes, "08:00", "10:00")) }, new[] { Nd(DiasSemana.Lunes, 9, 17) });

        var nd = Assert.Single(g.Celdas, c => c.Tipo == TipoCelda.NoDisponible);
        Assert.Equal((9 * 60, 17 * 60, false, 1), (nd.InicioMin, nd.FinMin, nd.ConChoque, nd.Carriles));
        Assert.Empty(g.Choques);   // chocar con lo no disponible no es un choque entre secciones
    }

    [Fact]
    public void LaParteVirtualSeListaAparteYNoOcupaLaGrilla()
    {
        var s = Sec("ISO625", "1", Bloque(MJ, "08:00", "10:00"), Bloque(DiasSemana.Ninguno, "18:00", "20:00"));

        var g = GrillaSemanal.Construir(new[] { s });

        Assert.Equal(2, g.Celdas.Count);   // solo martes y jueves
        Assert.Equal(s, Assert.Single(g.SinHorarioFijo).Seccion);
    }

    [Fact]
    public void SinSeccionesLaGrillaEstaVaciaPeroValida()
    {
        var g = GrillaSemanal.Construir(Array.Empty<SeccionBanner>());

        Assert.Empty(g.Celdas);
        Assert.Empty(g.Choques);
        Assert.Equal(6, g.Dias.Count);
    }
}
