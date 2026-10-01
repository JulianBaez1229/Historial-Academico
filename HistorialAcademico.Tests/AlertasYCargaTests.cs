using HistorialAcademico.Core.Entities;
using HistorialAcademico.Core.Pensum;
using HistorialAcademico.Core.Planificacion;
using Xunit;

namespace HistorialAcademico.Tests;

/// <summary>El plan sugerido según la carga que se quiere llevar y los períodos en que no se estudia.</summary>
public class CargaYOmitidosTests
{
    private static readonly PeriodoAcademico P0 = new(2027, 0);   // ENE-ABR 2027
    private static readonly PeriodoAcademico P1 = P0.Siguiente();
    private static readonly PeriodoAcademico P2 = P1.Siguiente();

    private static MateriaPensum M(string codigo, int cr, int cuat, string? pre = null) =>
        new() { Codigo = codigo, Nombre = $"Materia {codigo}", Creditos = cr, Cuatrimestre = cuat, Prerrequisitos = pre };

    /// <summary>Quince materias de 3 créditos sin prerrequisitos y el TFG: cabe todo en pocos períodos según la carga.</summary>
    private static (ContextoPlan Ctx, ResultadoPrioridades Prioridades) Contexto(int limite = 25, bool conTfg = false)
    {
        var pensum = Enumerable.Range(1, 15).Select(i => M($"MAT{i:000}", 3, 1 + i / 5)).ToList();
        if (conTfg) pensum.Add(M("TFG", 3, 5));
        var r = MotorEstadoPensum.Calcular(pensum, Array.Empty<MateriaCursada>(), Array.Empty<CursoEnProgreso>(), Array.Empty<Equivalencia>());
        var ctx = ContextoPlan.Crear(r, new ConfigPlanificador(limite, limite), 0m, P0);
        return (ctx, CalculadoraPrioridades.Calcular(ctx));
    }

    private static int Creditos(ContextoPlan ctx, PlanPeriodo p) => p.Codigos.Sum(c => ctx.PorCodigo[c].Materia.Creditos);

    [Theory]
    [InlineData(CargaDeseada.Ligera, 25, 15)]
    [InlineData(CargaDeseada.Normal, 25, 20)]
    [InlineData(CargaDeseada.Pesada, 25, 25)]
    [InlineData(CargaDeseada.Ligera, 27, 16)]   // 60 % de 27 = 16.2
    [InlineData(CargaDeseada.Normal, 27, 22)]   // 80 % de 27 = 21.6
    public void LaCargaEsUnaFraccionDelLimite(CargaDeseada carga, int limite, int esperado) =>
        Assert.Equal(esperado, OpcionesGeneracion.LimiteDeCarga(carga, limite, creditosMateriaMayor: 3));

    [Fact]
    public void LaCargaNuncaBajaDeLoQuePesaLaMateriaMasGrandeNiPasaDelLimite()
    {
        Assert.Equal(20, OpcionesGeneracion.LimiteDeCarga(CargaDeseada.Ligera, 25, creditosMateriaMayor: 20));   // si no, esa materia no cabría nunca
        Assert.Equal(25, OpcionesGeneracion.LimiteDeCarga(CargaDeseada.Ligera, 25, creditosMateriaMayor: 40));   // pero el límite manda
    }

    [Fact]
    public void ConMenosCargaHaceFaltaMasPeriodosYNingunoPasaDeLoElegido()
    {
        var (ctx, prioridades) = Contexto();

        var planes = Enum.GetValues<CargaDeseada>().ToDictionary(c => c, c => GeneradorPlan.Generar(ctx, prioridades, new OpcionesGeneracion(c)));

        foreach (var (carga, tope) in new[] { (CargaDeseada.Pesada, 25), (CargaDeseada.Normal, 20), (CargaDeseada.Ligera, 15) })
            Assert.All(planes[carga].Periodos, p => Assert.True(Creditos(ctx, p) <= tope, $"{carga}: {p.Periodo.Nombre} tiene {Creditos(ctx, p)} créditos y el tope es {tope}"));
        Assert.True(planes[CargaDeseada.Ligera].Periodos.Count >= planes[CargaDeseada.Normal].Periodos.Count);
        Assert.True(planes[CargaDeseada.Normal].Periodos.Count >= planes[CargaDeseada.Pesada].Periodos.Count);
        Assert.True(planes[CargaDeseada.Ligera].Periodos.Count > planes[CargaDeseada.Pesada].Periodos.Count);
        Assert.All(planes.Values, p => Assert.Empty(p.NoPlanificadas));
    }

    [Fact]
    public void SinOpcionesElPlanEsElDeSiempreHastaElLimite()
    {
        var (ctx, prioridades) = Contexto();

        var conOpciones = GeneradorPlan.Generar(ctx, prioridades, new OpcionesGeneracion());
        var sinOpciones = GeneradorPlan.Generar(ctx, prioridades);

        Assert.Equal(sinOpciones.Periodos.Select(p => (p.Periodo, string.Join(",", p.Codigos))), conOpciones.Periodos.Select(p => (p.Periodo, string.Join(",", p.Codigos))));
        Assert.Equal(8, Creditos(ctx, sinOpciones.Periodos[0]) / 3);   // 25 créditos: ocho materias de tres
    }

    [Fact]
    public void LosPeriodosOmitidosQuedanVaciosYElPlanSigueEnElSiguiente()
    {
        var (ctx, prioridades) = Contexto();
        var sin = GeneradorPlan.Generar(ctx, prioridades, OpcionesGeneracion.Ninguna);

        var plan = GeneradorPlan.Generar(ctx, prioridades, new OpcionesGeneracion(CargaDeseada.Pesada, new HashSet<PeriodoAcademico> { P1 }));

        Assert.DoesNotContain(plan.Periodos, p => p.Periodo == P1);
        Assert.Equal(new[] { P0, P2 }, plan.Periodos.Select(p => p.Periodo).ToArray());   // ENE-ABR y SEP-DIC: se salta MAY-AGO
        Assert.Equal(sin.Periodos.Sum(p => p.Codigos.Count), plan.Periodos.Sum(p => p.Codigos.Count));   // igual se planifica todo
        Assert.Empty(plan.NoPlanificadas);
    }

    [Fact]
    public void SiElPrimerPeriodoSeOmiteElPlanEmpiezaDespues()
    {
        var (ctx, prioridades) = Contexto();

        var plan = GeneradorPlan.Generar(ctx, prioridades, new OpcionesGeneracion(CargaDeseada.Pesada, new HashSet<PeriodoAcademico> { P0, P1 }));

        Assert.Equal(P2, plan.Periodos[0].Periodo);
        Assert.Empty(plan.NoPlanificadas);
    }

    [Fact]
    public void UnPeriodoOmitidoNoHaceQueLasMateriasFigurenComoPospuestasPorElLimite()
    {
        var (ctx, prioridades) = Contexto();

        var plan = GeneradorPlan.Generar(ctx, prioridades, new OpcionesGeneracion(CargaDeseada.Pesada, new HashSet<PeriodoAcademico> { P0 }));

        // Las materias del primer período en que sí se estudia no se «pospusieron»: el período omitido no cuenta como espera.
        var primeras = plan.Periodos[0].Codigos.Where(c => plan.Razones.ContainsKey(c)).Select(c => plan.Razones[c]).ToList();
        Assert.NotEmpty(primeras);
        Assert.DoesNotContain(primeras, r => r.Contains("Pospuesta"));
    }

    [Fact]
    public void ElTfgSigueSiendoLoUltimoAunqueSuPeriodoSeOmita()
    {
        var (ctx, prioridades) = Contexto(conTfg: true);
        var normal = GeneradorPlan.Generar(ctx, prioridades);
        var ultimo = normal.Periodos[^1].Periodo;

        var plan = GeneradorPlan.Generar(ctx, prioridades, new OpcionesGeneracion(CargaDeseada.Pesada, new HashSet<PeriodoAcademico> { ultimo }));

        var conTfg = plan.Periodos.Single(p => p.Codigos.Contains("TFG"));
        Assert.Equal(plan.Periodos[^1], conTfg);
        Assert.NotEqual(ultimo, conTfg.Periodo);   // no cae en un período omitido
        Assert.DoesNotContain(plan.Periodos, p => p.Periodo == ultimo);
    }

    [Fact]
    public void ElPlanGeneradoConCargaLigeraSigueSiendoValidoParaElEvaluador()
    {
        var (ctx, prioridades) = Contexto();

        var plan = GeneradorPlan.Generar(ctx, prioridades, new OpcionesGeneracion(CargaDeseada.Ligera, new HashSet<PeriodoAcademico> { P1 }));
        var ev = EvaluadorPlan.Evaluar(ctx, plan.Periodos);

        Assert.True(ev.EsValido, string.Join(" | ", ev.Errores.Concat(ev.Periodos.SelectMany(p => p.Alertas))));
        Assert.True(ev.TodoPlanificado);
    }
}

/// <summary>Alertas del plan: qué avisan, qué botón ofrecen y que resolver una nunca crea otra.</summary>
public class AlertasPlanTests
{
    private static readonly PeriodoAcademico P0 = new(2027, 0);
    private static readonly PeriodoAcademico P1 = P0.Siguiente();
    private static readonly PeriodoAcademico P2 = P1.Siguiente();
    private static readonly PeriodoAcademico P3 = P2.Siguiente();

    private static MateriaPensum M(string codigo, int cr, int cuat, string? pre = null, bool electiva = false) =>
        new() { Codigo = codigo, Nombre = $"Materia {codigo}", Creditos = cr, Cuatrimestre = cuat, Prerrequisitos = pre, EsElectiva = electiva };

    private static MateriaCursada Cursada(string codigo, string nota = "A") => new() { Codigo = codigo, Calificacion = nota, HorasCredito = 3 };

    /// <summary>
    /// AAA100 → BBB200 → CCC300 (ruta crítica desde AAA100), cuatro materias sueltas del cuatrimestre 1, R100 (rezagada si se aprueba el cuatrimestre 2),
    /// una electiva que pide el 50 % y dos materias del cuatrimestre 2.
    /// </summary>
    private static ContextoPlan Contexto(int limite = 25, params MateriaCursada[] aprobadas)
    {
        var pensum = new List<MateriaPensum>
        {
            M("AAA100", 3, 1), M("BBB200", 3, 2, "AAA100"), M("CCC300", 3, 3, "BBB200"),
            M("EEE100", 3, 1), M("FFF100", 3, 1), M("GGG100", 3, 1), M("R100", 3, 1),
            M("ELE001", 3, 4, "50%", electiva: true), M("H200", 3, 2), M("H201", 3, 2),
        };
        var r = MotorEstadoPensum.Calcular(pensum, aprobadas, Array.Empty<CursoEnProgreso>(), Array.Empty<Equivalencia>());
        return ContextoPlan.Crear(r, new ConfigPlanificador(limite, limite), 0m, P0);
    }

    private static PlanPeriodo Per(PeriodoAcademico p, params string[] codigos) => new() { Periodo = p, Codigos = codigos.ToList() };

    private static List<AlertaPlan> Alertas(ContextoPlan ctx, params PlanPeriodo[] plan) =>
        AlertasPlan.Calcular(ctx, plan, EvaluadorPlan.Evaluar(ctx, plan), CalculadoraPrioridades.Calcular(ctx));

    /// <summary>Aplica el botón de una alerta al plan, como lo haría la pantalla, y devuelve el plan resultante.</summary>
    private static PlanPeriodo[] Aplicar(IEnumerable<PlanPeriodo> plan, AccionAlerta accion)
    {
        var periodos = plan.Select(p => Per(p.Periodo, p.Codigos.Where(c => c != accion.Codigo).ToArray())).ToList();
        if (accion.Periodo is not null)
        {
            var destino = PeriodoAcademico.Parse(accion.Periodo);
            var existente = periodos.FirstOrDefault(p => p.Periodo == destino);
            if (existente is null) periodos.Add(existente = Per(destino));
            existente.Codigos.Add(accion.Codigo);
        }
        return periodos.Where(p => p.Codigos.Count > 0).ToArray();
    }

    [Fact]
    public void SinPlanNoHayAlertas() => Assert.Empty(Alertas(Contexto()));

    [Fact]
    public void UnaMateriaDeRutaCriticaQueYaSePuedeCursarPeroNoEstaEnElPlanAvisaYOfreceAgregarla()
    {
        var alertas = Alertas(Contexto(), Per(P0, "EEE100"));

        var a = Assert.Single(alertas, x => x.Tipo == TipoAlerta.RutaCritica);
        Assert.True(a.Grave);
        Assert.Contains("AAA100", a.Mensaje);
        Assert.Contains("ruta crítica", a.Mensaje);
        Assert.Contains("no está en el plan", a.Mensaje);
        Assert.Equal(new AccionAlerta("Agregar a ENE-ABR 2027", "AAA100", "ENE-ABR 2027"), a.Accion);
    }

    [Fact]
    public void SiLaRutaCriticaEstaPlanificadaMasTardeYCabeAntesSeOfreceMoverla()
    {
        var alertas = Alertas(Contexto(), Per(P0, "EEE100"), Per(P1, "AAA100"), Per(P2, "BBB200"), Per(P3, "CCC300"));

        var a = Assert.Single(alertas, x => x.Tipo == TipoAlerta.RutaCritica);
        Assert.Contains("está en MAY-AGO 2027", a.Mensaje);
        Assert.Equal(new AccionAlerta("Mover a ENE-ABR 2027", "AAA100", "ENE-ABR 2027"), a.Accion);
    }

    [Fact]
    public void SiLaRutaCriticaYaEstaEnElPrimerPeriodoNoHayAlerta()
    {
        var alertas = Alertas(Contexto(), Per(P0, "AAA100"), Per(P1, "BBB200"), Per(P2, "CCC300"));

        Assert.DoesNotContain(alertas, a => a.Tipo == TipoAlerta.RutaCritica);
    }

    [Fact]
    public void SiElPrimerPeriodoNoLeDejaLugarNoSePideMoverLaRutaCriticaAhi()
    {
        // Límite de 6: el primer período ya está lleno con dos materias y AAA100, planificada después, no cabe ahí.
        var alertas = Alertas(Contexto(limite: 6), Per(P0, "EEE100", "FFF100"), Per(P1, "AAA100"), Per(P2, "BBB200"), Per(P3, "CCC300"));

        Assert.DoesNotContain(alertas, a => a.Tipo == TipoAlerta.RutaCritica);
    }

    [Fact]
    public void UnaRezagadaSinPlanificarAvisaYOfreceElPrimerPeriodoDondePuedeIr()
    {
        // Con el cuatrimestre 2 aprobado, las materias del 1 que faltan (R100…) quedan rezagadas.
        var ctx = Contexto(25, Cursada("H200"), Cursada("H201"), Cursada("AAA100"), Cursada("BBB200"));

        var alertas = Alertas(ctx, Per(P0, "CCC300"));

        var a = alertas.First(x => x.Tipo == TipoAlerta.Rezagada && x.Mensaje.StartsWith("R100"));
        Assert.False(a.Grave);
        Assert.Contains("rezagada", a.Mensaje);
        Assert.Equal(new AccionAlerta("Agregar a ENE-ABR 2027", "R100", "ENE-ABR 2027"), a.Accion);
    }

    [Fact]
    public void UnPeriodoPorEncimaDelLimiteSugiereMoverLaMateriaMenosPrioritaria()
    {
        var alertas = Alertas(Contexto(limite: 5), Per(P0, "AAA100", "EEE100"));

        var a = Assert.Single(alertas, x => x.Tipo == TipoAlerta.SobreLimite);
        Assert.True(a.Grave);
        Assert.Contains("ENE-ABR 2027 pasa el límite: 6 créditos y el máximo es 5 (sobran 1)", a.Mensaje);
        Assert.Equal("EEE100", a.Accion!.Codigo);           // AAA100 es de ruta crítica: no se toca
        Assert.Equal("MAY-AGO 2027", a.Accion.Periodo);
        Assert.Equal("Mover EEE100 a MAY-AGO 2027", a.Accion.Texto);
    }

    [Fact]
    public void UnaElectivaQueNoAlcanzaElPorcentajeSugiereElPrimerPeriodoDondeYaLoAlcanza()
    {
        // 30 créditos en total: el 50 % son 15, o sea cinco materias antes de la electiva.
        var alertas = Alertas(Contexto(), Per(P0, "AAA100", "EEE100", "FFF100", "GGG100", "R100", "ELE001"));

        var a = Assert.Single(alertas, x => x.Tipo == TipoAlerta.ElectivaPorcentaje);
        Assert.False(a.Grave);
        Assert.Contains("ELE001 (electiva)", a.Mensaje);
        Assert.Contains("Requiere 50%", a.Mensaje);
        Assert.Equal(new AccionAlerta("Mover a MAY-AGO 2027", "ELE001", "MAY-AGO 2027"), a.Accion);
    }

    [Fact]
    public void UnaMateriaQueNoPuedeCursarseDondeEstaSugiereElPeriodoSiguienteDondeSi()
    {
        var alertas = Alertas(Contexto(), Per(P0, "AAA100", "BBB200"));

        var a = Assert.Single(alertas, x => x.Tipo == TipoAlerta.ConProblema);
        Assert.True(a.Grave);
        Assert.Contains("BBB200 no puede cursarse en ENE-ABR 2027", a.Mensaje);
        Assert.Contains("AAA100 está en este mismo período", a.Mensaje);
        Assert.Equal(new AccionAlerta("Mover a MAY-AGO 2027", "BBB200", "MAY-AGO 2027"), a.Accion);
    }

    [Fact]
    public void UnaMateriaYaAprobadaEnElPlanSePideQuitarla()
    {
        var alertas = Alertas(Contexto(25, Cursada("EEE100")), Per(P0, "AAA100", "EEE100"));

        var a = Assert.Single(alertas, x => x.Tipo == TipoAlerta.ConProblema);
        Assert.Contains("Ya está aprobada", a.Mensaje);
        Assert.Equal(new AccionAlerta("Quitar del plan", "EEE100", null), a.Accion);
    }

    [Fact]
    public void LasGravesVanPrimero()
    {
        var alertas = Alertas(Contexto(limite: 5), Per(P0, "AAA100", "EEE100", "ELE001"));

        var vistas = alertas.Select(a => a.Grave).ToList();
        Assert.Equal(vistas.OrderByDescending(g => g).ToList(), vistas);
    }

    [Theory]
    [InlineData(TipoAlerta.RutaCritica)]
    [InlineData(TipoAlerta.SobreLimite)]
    [InlineData(TipoAlerta.ElectivaPorcentaje)]
    [InlineData(TipoAlerta.ConProblema)]
    public void ResolverUnaAlertaConSuBotonNoCreaOtraNiDejaLaMisma(TipoAlerta tipo)
    {
        var ctx = Contexto(limite: tipo == TipoAlerta.SobreLimite ? 5 : 25);
        var plan = tipo switch
        {
            TipoAlerta.RutaCritica => new[] { Per(P0, "EEE100") },
            TipoAlerta.SobreLimite => new[] { Per(P0, "AAA100", "EEE100") },
            TipoAlerta.ElectivaPorcentaje => new[] { Per(P0, "AAA100", "EEE100", "FFF100", "GGG100", "R100", "ELE001") },
            _ => new[] { Per(P0, "AAA100", "BBB200") },
        };
        var antes = Alertas(ctx, plan);
        var alerta = antes.First(a => a.Tipo == tipo);
        Assert.NotNull(alerta.Accion);

        var despues = Alertas(ctx, Aplicar(plan, alerta.Accion!));

        var resumen = $"antes: {string.Join(" / ", antes.Select(a => a.Mensaje))}; después: {string.Join(" / ", despues.Select(a => a.Mensaje))}";
        Assert.DoesNotContain(despues, a => a.Mensaje == alerta.Mensaje);                                        // la alerta ya no está
        Assert.True(despues.Count < antes.Count, resumen);                                                       // y no apareció otra en su lugar
        Assert.True(despues.Count(a => a.Grave) <= antes.Count(a => a.Grave), resumen);
        // La materia movida queda válida donde la puso el botón.
        var evaluacion = EvaluadorPlan.Evaluar(ctx, Aplicar(plan, alerta.Accion!));
        Assert.All(evaluacion.Periodos.SelectMany(p => p.Materias).Where(m => m.Codigo == alerta.Accion!.Codigo), m => Assert.True(m.EsValida, resumen));
    }
}
