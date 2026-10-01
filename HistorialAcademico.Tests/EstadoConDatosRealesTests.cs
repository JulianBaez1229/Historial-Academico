using HistorialAcademico.Core.Entities;
using HistorialAcademico.Core.Pensum;
using HistorialAcademico.Web.Services;
using Xunit;

namespace HistorialAcademico.Tests;

/// <summary>
/// Extremo a extremo con el histórico anonimizado (tests/samples/historico-anonimizado.html → SQLite → motor) y el pénsum del CSV.
/// Documentan una diferencia entre Banner y el prompt del laboratorio:
/// en Banner la materia de 2 créditos del primer período es TEC098 (Introducción a la Ingeniería), no SOC013 (Filosofía).
/// </summary>
public class EstadoConDatosRealesTests
{
    private static async Task<(BdPrueba bd, AcademicoService svc)> PrepararAsync()
    {
        var bd = new BdPrueba();
        var r = await bd.Sync.AplicarHtmlAsync(Muestras.LeerAnonimizado());
        Assert.True(r.Exito, r.Mensaje);
        bd.Db.MateriasPensum.AddRange(DatosLab.Pensum());
        await bd.Db.SaveChangesAsync();
        return (bd, new AcademicoService(bd.Db));
    }

    [Fact]
    public async Task ElIndiceCalculadoCoincideConBannerYNoHayAdvertencias()
    {
        var (bd, svc) = await PrepararAsync();
        using var _ = bd;
        var e = await svc.ObtenerAsync();

        Assert.Equal(2.62m, e.Indice.Global.Indice);
        Assert.Equal(134m, e.Indice.Global.HorasPga);
        Assert.Equal(351m, e.Indice.Global.PuntosCalidad);
        Assert.Equal(143m, e.Indice.Global.HorasAprobadas);
        Assert.Empty(e.Advertencias);
    }

    [Fact]
    public async Task SinEquivalenciaTEC098QuedaFueraDelPensum_YFilosofiaFigura_Disponible()
    {
        var (bd, svc) = await PrepararAsync();
        using var _ = bd;
        var p = (await svc.ObtenerAsync()).Pensum;

        Assert.Equal(128, p.CreditosAprobados);
        Assert.Equal(20, p.CreditosEnCurso);
        Assert.Equal(70, p.CreditosFaltantes);
        Assert.Equal(EstadoMateria.Disponible, p.Buscar("SOC013")!.Estado);
    }

    [Fact]
    public async Task ConTEC098ComoEquivalenteDeSOC013ElResultadoCoincideConElLaboratorio()
    {
        var (bd, svc) = await PrepararAsync();
        using var _ = bd;
        bd.Db.Equivalencias.AddRange(EquivalenciasIniciales.Valores);
        bd.Db.Equivalencias.Add(new Equivalencia { CodigoBanner = "TEC098", CodigoPensum = "SOC013" });
        await bd.Db.SaveChangesAsync();

        var p = (await svc.ObtenerAsync()).Pensum;

        Assert.Equal(130, p.CreditosAprobados);
        Assert.Equal(20, p.CreditosEnCurso);
        Assert.Equal(68, p.CreditosFaltantes);
        var soc013 = p.Buscar("SOC013")!;
        Assert.Equal(EstadoMateria.Aprobada, soc013.Estado);
        Assert.Equal("TEC098", soc013.PorEquivalencia);

        var pendientes = p.Materias.Where(m => m.Estado is EstadoMateria.Disponible or EstadoMateria.Bloqueada).Select(m => m.Materia.Codigo).OrderBy(c => c);
        Assert.Equal(DatosLab.Cargar().Faltantes.OrderBy(c => c), pendientes);
    }
}
