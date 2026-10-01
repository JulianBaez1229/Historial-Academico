using System.Text.Json;
using HistorialAcademico.Core.Entities;
using HistorialAcademico.Core.Pensum;
using HistorialAcademico.Core.Planificacion;
using HistorialAcademico.Web.Services;
using Xunit;

namespace HistorialAcademico.Tests;

/// <summary>Dónde puede y dónde no puede ir una materia en el tablero del planificador, y por qué.</summary>
public class ColocacionTests
{
    private static readonly PeriodoAcademico P0 = new(2027, 0);   // ENE-ABR 2027: el primero planificable
    private static readonly PeriodoAcademico P1 = P0.Siguiente();
    private static readonly PeriodoAcademico P2 = P1.Siguiente();

    private static MateriaPensum M(string codigo, int cr, int cuat, string? pre = null, string? nombre = null) =>
        new() { Codigo = codigo, Nombre = nombre ?? $"Materia {codigo}", Creditos = cr, Cuatrimestre = cuat, Prerrequisitos = pre };

    /// <summary>AAA100 → BBB200 → CCC300, más DDD400 (pide el 50 % de los créditos) y el TFG; nada aprobado.</summary>
    private static ContextoPlan Contexto(int limite = 25)
    {
        var pensum = new List<MateriaPensum>
        {
            M("AAA100", 3, 1), M("BBB200", 3, 2, "AAA100"), M("CCC300", 3, 3, "BBB200"), M("EEE100", 3, 1),
            M("DDD400", 3, 4, "50%"), M("FFF100", 3, 1), M("TFG", 3, 5),
        };
        var r = MotorEstadoPensum.Calcular(pensum, Array.Empty<MateriaCursada>(), Array.Empty<CursoEnProgreso>(), Array.Empty<Equivalencia>());
        return ContextoPlan.Crear(r, new ConfigPlanificador(limite, limite), 0m, P0);
    }

    private static PlanPeriodo Per(PeriodoAcademico p, params string[] codigos) => new() { Periodo = p, Codigos = codigos.ToList() };

    private static ColocacionInfo E(ContextoPlan ctx, IEnumerable<PlanPeriodo> plan, string codigo, PeriodoAcademico periodo) =>
        ValidadorColocacion.Evaluar(ctx, plan, codigo, periodo);

    [Fact]
    public void UnaMateriaSinRequisitosPuedeIrACualquierPeriodoPlanificable()
    {
        var ctx = Contexto();

        Assert.All(new[] { P0, P1, P2 }, p => Assert.True(E(ctx, Array.Empty<PlanPeriodo>(), "AAA100", p).Permitida));
    }

    [Fact]
    public void NingunaMateriaPuedeIrAUnPeriodoPasado()
    {
        var ctx = Contexto();

        var info = E(ctx, Array.Empty<PlanPeriodo>(), "AAA100", P0.Avanzar(-1));

        Assert.False(info.Permitida);
        Assert.Contains("ya pasó o está en curso", Assert.Single(info.Motivos));
        Assert.Contains("ENE-ABR 2027", info.Motivos[0]);
    }

    [Fact]
    public void ConPrerrequisitoQueNoEstaPlanificadoNoPuedeIrEnNingunLado()
    {
        var ctx = Contexto();

        foreach (var p in new[] { P0, P1, P2 })
        {
            var info = E(ctx, Array.Empty<PlanPeriodo>(), "BBB200", p);
            Assert.False(info.Permitida);
            Assert.Equal("Falta AAA100: no está aprobada ni planificada antes.", Assert.Single(info.Motivos));
        }
    }

    [Fact]
    public void PuedeIrEnUnPeriodoPosteriorAlDeSuPrerrequisitoPeroNoEnElMismoNiAntes()
    {
        var ctx = Contexto();
        var plan = new[] { Per(P1, "AAA100") };

        var antes = E(ctx, plan, "BBB200", P0);
        Assert.False(antes.Permitida);
        Assert.Equal($"AAA100 está planificada después ({P1.Nombre}); debe ir antes.", Assert.Single(antes.Motivos));

        var mismo = E(ctx, plan, "BBB200", P1);
        Assert.Equal("AAA100 está planificada en este mismo período y debe ir en uno anterior.", Assert.Single(mismo.Motivos));

        Assert.True(E(ctx, plan, "BBB200", P2).Permitida);
    }

    [Fact]
    public void LaMateriaQueYaEstaEnElPlanNoSeEstorbaASiMisma()
    {
        var ctx = Contexto();
        var plan = new[] { Per(P0, "AAA100"), Per(P1, "BBB200") };

        Assert.True(E(ctx, plan, "AAA100", P0).Permitida);   // donde ya está
        Assert.True(E(ctx, plan, "BBB200", P1).Permitida);
        Assert.True(E(ctx, plan, "BBB200", P2).Permitida);   // y moverla más tarde también
    }

    [Fact]
    public void UnaMateriaNoPuedeQuedarAntesNiJuntoAUnaQueLaNecesita()
    {
        var ctx = Contexto();
        var plan = new[] { Per(P1, "BBB200") };

        Assert.True(E(ctx, plan, "AAA100", P0).Permitida);

        var junto = E(ctx, plan, "AAA100", P1);
        Assert.Equal("BBB200 está planificada en este período y la necesita: debe ir después de AAA100.", Assert.Single(junto.Motivos));

        var despues = E(ctx, plan, "AAA100", P2);
        Assert.Equal($"BBB200 ({P1.Nombre}) la necesita y quedaría antes que AAA100.", Assert.Single(despues.Motivos));
    }

    [Fact]
    public void LaCadenaCompletaSeCuidaEnLosDosSentidos()
    {
        var ctx = Contexto();
        var plan = new[] { Per(P0, "AAA100"), Per(P1, "BBB200"), Per(P2, "CCC300") };

        Assert.False(E(ctx, plan, "AAA100", P1).Permitida);   // BBB200 quedaría antes o junto
        Assert.False(E(ctx, plan, "CCC300", P1).Permitida);   // junto a BBB200
        Assert.True(E(ctx, plan, "CCC300", P2.Siguiente()).Permitida);
        Assert.False(E(ctx, plan, "BBB200", P0).Permitida);   // junto a AAA100
    }

    [Fact]
    public void ElPorcentajeSeCuentaConLoPlanificadoAntesYElMotivoDiceCuantoLlevarias()
    {
        var ctx = Contexto();   // 21 créditos en total: el 50 % son 10.5 créditos (4 materias de 3)

        var solo = E(ctx, Array.Empty<PlanPeriodo>(), "DDD400", P1);
        Assert.False(solo.Permitida);
        Assert.Equal("Requiere 50% de los créditos; al inicio de MAY-AGO 2027 llevarás 0.0%.", Assert.Single(solo.Motivos));

        var plan = new[] { Per(P0, "AAA100", "EEE100", "FFF100", "BBB200") };   // 12 créditos planificados = 57 %
        Assert.True(E(ctx, plan, "DDD400", P1).Permitida);
        Assert.False(E(ctx, plan, "DDD400", P0).Permitida);   // en el mismo período no cuentan
    }

    [Fact]
    public void PasarElLimiteDeCreditosNoImpideNadaPeroAvisa()
    {
        var ctx = Contexto(limite: 5);
        var plan = new[] { Per(P0, "AAA100") };

        var info = E(ctx, plan, "EEE100", P0);

        Assert.True(info.Permitida);
        Assert.Equal("Pasaría el límite: 6 créditos y el máximo es 5.", info.Aviso);
        Assert.Null(E(ctx, plan, "EEE100", P1).Aviso);
        Assert.Null(E(ctx, plan, "AAA100", P0).Aviso);   // sola en su período: 3 de 5
    }

    [Fact]
    public void ElTfgNoPuedeIrAntesDeOtrasMateriasDelPlan()
    {
        var ctx = Contexto();
        var plan = new[] { Per(P0, "AAA100"), Per(P2, "EEE100") };

        Assert.Equal("El TFG debe ir en el último período del plan.", Assert.Single(E(ctx, plan, "TFG", P1).Motivos.Where(m => m.StartsWith("El TFG"))));
        Assert.DoesNotContain(E(ctx, plan, "TFG", P2.Siguiente()).Motivos, m => m.StartsWith("El TFG"));
    }

    [Fact]
    public void LaMatrizSoloGuardaLoQueNoEsLibreYSusClavesSonElCodigoYElPeriodo()
    {
        var ctx = Contexto();
        var plan = new[] { Per(P0, "AAA100"), Per(P1, "BBB200") };
        var columnas = new List<PeriodoAcademico> { P0, P1, P2 };

        var matriz = PlanificadorService.CalcularColocaciones(ctx, plan, columnas);

        Assert.DoesNotContain("EEE100", matriz.Keys);                                            // libre en todas las columnas: no se guarda
        Assert.Equal(new[] { P0.Nombre }, matriz["BBB200"].Keys.ToArray());                       // solo el período de AAA100 le está vedado
        Assert.Equal(new[] { P1.Nombre, P2.Nombre }, matriz["AAA100"].Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());   // quedaría junto o después de BBB200
        Assert.All(matriz.Values.SelectMany(v => v.Values), i => Assert.True(!i.Permitida || i.Aviso is not null));   // solo trae lo que no es libre
    }

    [Fact]
    public void ElJsonDelTableroEsCompactoYSeLeeEnElNavegador()
    {
        var ctx = Contexto();
        var estado = new EstadoPlanificador
        {
            Estado = null!, Config = ctx.Config, Planes = new(),
            Colocaciones = PlanificadorService.CalcularColocaciones(ctx, new[] { Per(P1, "BBB200") }, new List<PeriodoAcademico> { P0, P1 }),
        };

        using var doc = JsonDocument.Parse(estado.ColocacionesJson);

        var aaaEnP1 = doc.RootElement.GetProperty("AAA100").GetProperty(P1.Nombre);
        Assert.Contains("BBB200", aaaEnP1.GetProperty("m")[0].GetString());
        Assert.False(aaaEnP1.TryGetProperty("a", out _));   // sin aviso no se escribe (null se omite)
        Assert.DoesNotContain("\\u", estado.ColocacionesJson);   // las tildes van sin escapar
    }
}
