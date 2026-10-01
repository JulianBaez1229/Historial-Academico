using HistorialAcademico.Core.Entities;
using HistorialAcademico.Core.Pensum;
using HistorialAcademico.Core.Planificacion;
using Xunit;

namespace HistorialAcademico.Tests;

public class PeriodoAcademicoTests
{
    [Fact]
    public void LaSecuenciaEsEneAbrMayAgoSepDicYSaltaDeAnio()
    {
        var p = PeriodoAcademico.Parse("ENE-ABR 2027");
        Assert.Equal("MAY-AGO 2027", p.Siguiente().Nombre);
        Assert.Equal("SEP-DIC 2027", p.Siguiente().Siguiente().Nombre);
        Assert.Equal("ENE-ABR 2028", p.Siguiente().Siguiente().Siguiente().Nombre);
    }

    [Theory]
    [InlineData("SEP-DIC 2026", 1, "ENE-ABR 2027")]
    [InlineData("SEP-DIC 2026", 3, "SEP-DIC 2027")]
    [InlineData("ENE-ABR 2027", -1, "SEP-DIC 2026")]
    [InlineData("MAY-AGO 2027", -2, "SEP-DIC 2026")]
    [InlineData("MAY-AGO 2027", -4, "ENE-ABR 2026")]
    [InlineData("ENE-ABR 2027", 0, "ENE-ABR 2027")]
    public void AvanzarSumaORestaPeriodos(string desde, int n, string esperado) =>
        Assert.Equal(esperado, PeriodoAcademico.Parse(desde).Avanzar(n).Nombre);

    [Fact]
    public void SeCompara_YSeMideLaDistancia()
    {
        var a = PeriodoAcademico.Parse("SEP-DIC 2026");
        var b = PeriodoAcademico.Parse("ENE-ABR 2027");
        Assert.True(a < b);
        Assert.True(b > a);
        Assert.Equal(1, a.Distancia(b));
        Assert.Equal(-1, b.Distancia(a));
    }

    [Theory]
    [InlineData("sep-dic 2026", true)]
    [InlineData("  MAY-AGO 2024 ", true)]
    [InlineData("ENE-ABR 26", false)]
    [InlineData("OTRO 2026", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ReconoceSoloNombresDePeriodoValidos(string? texto, bool valido) =>
        Assert.Equal(valido, PeriodoAcademico.TryParse(texto, out _));

    [Fact]
    public void ElPrimerPeriodoPlanificableEsElSiguienteAlDelUltimoCursoEnProgreso()
    {
        Assert.Equal("ENE-ABR 2027", PeriodoAcademico.Primero("SEP-DIC 2026", "MAY-AGO 2026", asumirEnCurso: true)!.Value.Nombre);
        // Sin asumir que apruebas, el período en progreso sigue siendo planificable.
        Assert.Equal("SEP-DIC 2026", PeriodoAcademico.Primero("SEP-DIC 2026", "MAY-AGO 2026", asumirEnCurso: false)!.Value.Nombre);
        // Sin cursos en progreso: el siguiente al último cerrado.
        Assert.Equal("SEP-DIC 2026", PeriodoAcademico.Primero(null, "MAY-AGO 2026", true)!.Value.Nombre);
        Assert.Null(PeriodoAcademico.Primero(null, null, true));
    }
}

public class ConfigPlanificadorTests
{
    [Theory]
    [InlineData(3.34, 25)]
    [InlineData(3.40, 25)]   // "mayor que 3.40": con exactamente 3.40 no aplica
    [InlineData(3.41, 27)]
    [InlineData(4.00, 27)]
    [InlineData(0, 25)]
    public void ElLimiteEs25OY27SiElIndiceSuperaElUmbral(double indice, int esperado) =>
        Assert.Equal(esperado, new ConfigPlanificador().LimiteEfectivo((decimal)indice));

    [Fact]
    public void LosValoresIniciales()
    {
        var c = new ConfigPlanificador();
        Assert.Equal((25, 27, 3.40m, 0, true), (c.LimiteBase, c.LimiteAlto, c.UmbralIndice, c.Minimo, c.AsumirEnCurso));
    }

    [Fact]
    public void ExplicaCualLimiteAplica()
    {
        Assert.Contains("25", new ConfigPlanificador().ExplicarLimite(3.34m));
        Assert.Contains("no supera", new ConfigPlanificador().ExplicarLimite(3.34m));
        Assert.Contains("27", new ConfigPlanificador().ExplicarLimite(3.5m));
    }
}

/// <summary>Contexto de planificación armado con el historial sintético de Fixtures/laboratorio-sintetico.md y el CSV del pénsum.</summary>
public static class ContextoLab
{
    public static readonly PeriodoAcademico Primero = PeriodoAcademico.Parse("ENE-ABR 2027");

    public static ContextoPlan Crear(ConfigPlanificador? config = null, decimal indice = 3.34m, IEnumerable<Equivalencia>? eq = null)
    {
        var lab = DatosLab.Cargar();
        var r = MotorEstadoPensum.Calcular(DatosLab.Pensum(), lab.Cursadas, lab.EnProgreso, eq ?? Array.Empty<Equivalencia>());
        var cfg = config ?? new ConfigPlanificador();
        var primero = cfg.AsumirEnCurso ? Primero : Primero.Avanzar(-1);
        return ContextoPlan.Crear(r, cfg, indice, primero);
    }
}

public class PrioridadesTests
{
    private static readonly ContextoPlan Ctx = ContextoLab.Crear();
    private static readonly ResultadoPrioridades P = CalculadoraPrioridades.Calcular(Ctx);

    [Fact]
    public void ElNivelActualEsElCuatrimestreMasAltoConMayoriaAprobadaOEnCurso() => Assert.Equal(7, P.NivelActual);

    [Fact]
    public void DetectaLasTresRezagadasEsperadas()
    {
        var rezagadas = P.Materias.Where(m => m.Rezagada).Select(m => m.Materia.Codigo).OrderBy(c => c);
        Assert.Equal(new[] { "ING719", "ISO625", "ODEP" }, rezagadas);

        Assert.Equal(2, P.Buscar("ODEP")!.Materia.Cuatrimestre);
        Assert.Equal(5, P.Buscar("ING719")!.Materia.Cuatrimestre);
        Assert.Equal(6, P.Buscar("ISO625")!.Materia.Cuatrimestre);
        Assert.All(new[] { "ODEP", "ING719", "ISO625" }, c => Assert.Contains("Rezagada", P.Buscar(c)!.Etiquetas));
        Assert.DoesNotContain(P.Materias, m => m.Materia.Codigo == "E077" && m.Rezagada);   // cuatrimestre 7 = su nivel
    }

    [Fact]
    public void LaRutaCriticaEsLaCadenaMasLargaDeDependientes()
    {
        Assert.Equal(new[] { "ISO725", "ISO912", "ISO940" }, P.Buscar("ISO725")!.Cadena);
        Assert.Equal(new[] { "E077", "E078", "E079" }, P.Buscar("E077")!.Cadena);
        Assert.Equal(new[] { "ISO800", "ISO900" }, P.Buscar("ISO800")!.Cadena);
        Assert.Equal(new[] { "ISO937", "ISO945" }, P.Buscar("ISO937")!.Cadena);
        Assert.Single(P.Buscar("ISO936")!.Cadena);   // sin dependientes
        Assert.Contains("Ruta crítica", P.Buscar("ISO725")!.Etiquetas);
        Assert.DoesNotContain("Ruta crítica", P.Buscar("ISO800")!.Etiquetas);   // cadena de 2
    }

    [Fact]
    public void ContabilizaLasMateriasQueDesbloquea()
    {
        Assert.Equal(new[] { "ISO912" }, P.Buscar("ISO725")!.Desbloquea);
        Assert.Equal(new[] { "ISO900" }, P.Buscar("ISO800")!.Desbloquea);
        Assert.Equal(new[] { "ISO940" }, P.Buscar("ISO912")!.Desbloquea);
        Assert.Empty(P.Buscar("ISO940")!.Desbloquea);
    }

    [Fact]
    public void ElSeminarioYLasElectivasConPorcentajePendienteNoSePuedenAdelantar()
    {
        var soc281 = P.Buscar("SOC281")!;
        Assert.True(soc281.BloqueadaPorPorcentaje);
        Assert.Equal(90, soc281.PorcentajeRequerido);
        Assert.Contains("Bloqueada por %", soc281.Etiquetas);
        Assert.Contains("no se puede adelantar", soc281.Motivo);

        Assert.True(P.Buscar("E079")!.BloqueadaPorPorcentaje);         // 73 %: con 150 de 218 (68.8 %) todavía no
        Assert.False(P.Buscar("E077")!.BloqueadaPorPorcentaje);        // 59 %: ya cumplido asumiendo los cursos en curso
        Assert.False(P.Buscar("E078")!.BloqueadaPorPorcentaje);        // 67 %: ya cumplido
    }

    [Fact]
    public void ElOrdenEsPorPuntosYElMotivoExplicaCadaComponente()
    {
        Assert.Equal(P.Materias.Select(m => m.Puntos).OrderByDescending(x => x), P.Materias.Select(m => m.Puntos));
        var top = P.Materias.Take(2).Select(m => m.Materia.Codigo).OrderBy(c => c);
        Assert.Equal(new[] { "E077", "ISO725" }, top);   // 20 por la cadena + 3 por lo que desbloquean

        var iso725 = P.Buscar("ISO725")!;
        Assert.Equal(23, iso725.Puntos);
        Assert.StartsWith("Prioridad alta: Inicia la cadena ISO725 → ISO912 → ISO940", iso725.Motivo);
        Assert.Equal("alta", iso725.Nivel);

        var iso625 = P.Buscar("ISO625")!;   // rezagada sin dependientes
        Assert.Equal(15, iso625.Puntos);
        Assert.Contains("Rezagada: es del cuatrimestre 6 y ya vas en el 7", iso625.Motivo);
        Assert.Equal("media", iso625.Nivel);

        Assert.Equal("baja", P.Buscar("ISO936")!.Nivel);
        Assert.Contains("Sin dependientes", P.Buscar("ISO936")!.Motivo);
    }

    [Fact]
    public void ConservaElEstadoActualYLoQueLeFalta()
    {
        Assert.Equal(EstadoMateria.Disponible, P.Buscar("ISO625")!.Estado.Estado);
        var iso800 = P.Buscar("ISO800")!.Estado;
        Assert.Equal(EstadoMateria.Bloqueada, iso800.Estado);
        Assert.Contains("ISO735", iso800.Bloqueos.Single());
    }

    [Fact]
    public void SinAsumirLosCursosEnProgresoTambienSePlanifican()
    {
        var ctx = ContextoLab.Crear(new ConfigPlanificador(AsumirEnCurso: false));
        var p = CalculadoraPrioridades.Calcular(ctx);
        Assert.Equal(22 + 6, ctx.Pendientes.Count);
        Assert.Equal(new[] { "ISO720", "ISO725", "ISO912", "ISO940" }, p.Buscar("ISO720")!.Cadena);
    }

    [Fact]
    public void LosPeriodosDondeSeCumpleElPorcentajeSalenDelPlan()
    {
        var plan = GeneradorPlan.Generar(Ctx, P);
        var ev = EvaluadorPlan.Evaluar(Ctx, plan.Periodos);
        CalculadoraPrioridades.CompletarPorcentajes(P, Ctx, ev);

        Assert.Equal("Ya cumplido", P.Buscar("E077")!.PeriodoPorcentaje);                                    // 59 %: 130 de 218 = 59.6 % hoy
        Assert.Equal("Se cumple al aprobar los cursos en curso", P.Buscar("E078")!.PeriodoPorcentaje);     // 67 %: solo con los 20 créditos en curso
        Assert.Equal("Se estima en SEP-DIC 2027", P.Buscar("SOC281")!.PeriodoPorcentaje);   // 90 %: recién al inicio del 3.er período
        Assert.Equal("Se estima en MAY-AGO 2027", P.Buscar("E079")!.PeriodoPorcentaje);     // 73 %: al inicio del 2.º período
        Assert.Null(P.Buscar("ISO725")!.PeriodoPorcentaje);
    }
}

public class GeneradorPlanTests
{
    private static void VerificarPlan(ContextoPlan ctx, PlanGenerado plan)
    {
        var ev = EvaluadorPlan.Evaluar(ctx, plan.Periodos);

        // Nunca pasa el límite de créditos.
        Assert.All(ev.Periodos, p => Assert.True(p.Creditos <= ctx.Limite, $"{p.Periodo.Nombre}: {p.Creditos} > {ctx.Limite}"));

        // Nunca planifica una materia antes que su prerrequisito (ni en el mismo período).
        var periodoDe = plan.Periodos.SelectMany((p, i) => p.Codigos.Select(c => (c, i))).ToDictionary(x => x.c, x => x.i, StringComparer.OrdinalIgnoreCase);
        foreach (var (codigo, i) in periodoDe)
            foreach (var req in ctx.Requisitos(codigo).Where(r => r.Materia is not null))
                Assert.True(ctx.Aprobadas.Contains(req.Materia!) || (periodoDe.TryGetValue(req.Materia!, out var j) && j < i),
                    $"{codigo} en el período {i} antes que su prerrequisito {req.Materia}");

        // Ninguna materia repetida y el evaluador independiente no encuentra problemas.
        Assert.Equal(periodoDe.Count, plan.Periodos.Sum(p => p.Codigos.Count));
        Assert.True(ev.EsValido, string.Join(" | ", ev.Errores.Concat(ev.Periodos.SelectMany(p => p.Materias.SelectMany(m => m.Problemas.Select(x => $"{m.Codigo}: {x}"))))));
    }

    [Fact]
    public void ElPlanSugeridoCumpleTodasLasReglas()
    {
        var ctx = ContextoLab.Crear();
        var plan = GeneradorPlan.Generar(ctx, CalculadoraPrioridades.Calcular(ctx));

        VerificarPlan(ctx, plan);
        Assert.Empty(plan.NoPlanificadas);
        Assert.Equal(ctx.Pendientes.Count, plan.Periodos.Sum(p => p.Codigos.Count));   // las 22 materias faltantes
    }

    [Fact]
    public void LaCadenaLargaDefineElNumeroDePeriodosYLaGraduacion()
    {
        var ctx = ContextoLab.Crear();
        var plan = GeneradorPlan.Generar(ctx, CalculadoraPrioridades.Calcular(ctx));

        Assert.Equal(new[] { "ENE-ABR 2027", "MAY-AGO 2027", "SEP-DIC 2027" }, plan.Periodos.Select(p => p.Periodo.Nombre));
        var donde = plan.Periodos.SelectMany((p, i) => p.Codigos.Select(c => (c, i))).ToDictionary(x => x.c, x => x.i);
        Assert.Equal(0, donde["ISO725"]);   // la cadena ISO725 → ISO912 → ISO940 arranca de inmediato
        Assert.Equal(1, donde["ISO912"]);
        Assert.Equal(2, donde["ISO940"]);
        Assert.Equal(0, donde["E077"]);
        Assert.Equal(1, donde["E078"]);
        Assert.Equal(2, donde["E079"]);

        var ev = EvaluadorPlan.Evaluar(ctx, plan.Periodos);
        Assert.Equal("SEP-DIC 2027", ev.Graduacion!.Value.Nombre);
        Assert.False(ev.GraduacionEstimada);
        Assert.Equal(3, ev.CuatrimestresPlanificados);
        Assert.Equal(68, plan.Periodos.SelectMany(p => p.Codigos).Sum(c => ctx.PorCodigo[c].Materia.Creditos));
    }

    [Fact]
    public void ElSeminarioEsperaAlNoventaPorCiento()
    {
        var ctx = ContextoLab.Crear();
        var plan = GeneradorPlan.Generar(ctx, CalculadoraPrioridades.Calcular(ctx));
        // 90 % de 218 = 196.2 créditos: con 150 + 25 + 25 recién se llega al inicio del 3.er período.
        Assert.Contains("SOC281", plan.Periodos[2].Codigos);
        Assert.DoesNotContain("SOC281", plan.Periodos[0].Codigos.Concat(plan.Periodos[1].Codigos));
    }

    [Fact]
    public void ElTfgVaEnElUltimoPeriodoYLaPasantiaEnSuCuatrimestre()
    {
        var ctx = ContextoLab.Crear();
        var plan = GeneradorPlan.Generar(ctx, CalculadoraPrioridades.Calcular(ctx));

        Assert.Contains("TFG", plan.Periodos[^1].Codigos);
        Assert.DoesNotContain("TFG", plan.Periodos.SkipLast(1).SelectMany(p => p.Codigos));
        // Nivel actual 7: los períodos son los cuatrimestres 8, 9 y 10 → la pasantía (cuatrimestre 10) cae en el tercero.
        Assert.Contains("PAS261", plan.Periodos[2].Codigos);
        Assert.Contains("último período", plan.Razones["TFG"]);
        Assert.Contains("cuatrimestre 10", plan.Razones["PAS261"]);
    }

    [Fact]
    public void LasRezagadasSeAdelantan()
    {
        var ctx = ContextoLab.Crear();
        var plan = GeneradorPlan.Generar(ctx, CalculadoraPrioridades.Calcular(ctx));
        Assert.All(new[] { "ODEP", "ING719", "ISO625" }, c => Assert.Contains(c, plan.Periodos[0].Codigos));
    }

    [Fact]
    public void CadaMateriaTieneUnaRazonCorta()
    {
        var ctx = ContextoLab.Crear();
        var plan = GeneradorPlan.Generar(ctx, CalculadoraPrioridades.Calcular(ctx));
        Assert.Equal(ctx.Pendientes.Count, plan.Razones.Count);
        Assert.StartsWith("Prioridad alta: Inicia la cadena ISO725 → ISO912 → ISO940", plan.Razones["ISO725"]);
        Assert.Contains("Espera a cumplir ISO725", plan.Razones["ISO912"]);    // no puede ir antes que su prerrequisito
        Assert.Contains("Rezagada", plan.Razones["ISO625"]);
        Assert.Contains("Espera a cumplir 90% de créditos", plan.Razones["SOC281"]);
    }

    [Theory]
    [InlineData(25, 3.34)]
    [InlineData(27, 3.50)]   // índice alto: límite 27
    [InlineData(12, 3.34)]
    [InlineData(6, 3.34)]
    [InlineData(4, 3.34)]
    public void RespetaElLimiteQueCorresponde(int limiteBase, double indice)
    {
        var cfg = new ConfigPlanificador(LimiteBase: limiteBase, LimiteAlto: limiteBase + 2);
        var ctx = ContextoLab.Crear(cfg, (decimal)indice);
        var plan = GeneradorPlan.Generar(ctx, CalculadoraPrioridades.Calcular(ctx));

        Assert.Equal(indice > 3.40 ? limiteBase + 2 : limiteBase, ctx.Limite);
        // Con un límite de 4 el TFG (6 créditos) y algunas materias de 5 no caben: quedan sin planificar, pero nunca se pasa el límite.
        var ev = EvaluadorPlan.Evaluar(ctx, plan.Periodos);
        Assert.All(ev.Periodos, p => Assert.True(p.Creditos <= ctx.Limite || p.Materias.All(m => m.Codigo == "TFG")));
        if (limiteBase >= 6) Assert.Empty(plan.NoPlanificadas);
    }

    [Fact]
    public void ConMasCargaSeNecesitanMasPeriodos()
    {
        var pocos = ContextoLab.Crear(new ConfigPlanificador(LimiteBase: 25));
        var muchos = ContextoLab.Crear(new ConfigPlanificador(LimiteBase: 12));
        var a = GeneradorPlan.Generar(pocos, CalculadoraPrioridades.Calcular(pocos));
        var b = GeneradorPlan.Generar(muchos, CalculadoraPrioridades.Calcular(muchos));
        Assert.True(b.Periodos.Count > a.Periodos.Count);
        VerificarPlan(muchos, b);
    }

    [Fact]
    public void SinAsumirLosCursosEnProgresoEmpiezaEnElPeriodoActual()
    {
        var ctx = ContextoLab.Crear(new ConfigPlanificador(AsumirEnCurso: false));
        var plan = GeneradorPlan.Generar(ctx, CalculadoraPrioridades.Calcular(ctx));

        Assert.Equal("SEP-DIC 2026", plan.Periodos[0].Periodo.Nombre);
        VerificarPlan(ctx, plan);
        Assert.Equal(28, plan.Periodos.Sum(p => p.Codigos.Count));   // 22 faltantes + 6 en curso
        // Los cursos en progreso (ISO720…) se cursan primero; sus dependientes esperan.
        Assert.Contains("ISO720", plan.Periodos[0].Codigos);
        Assert.DoesNotContain("ISO725", plan.Periodos[0].Codigos);
    }

    [Fact]
    public void SiNoFaltaNadaElPlanEstaVacio()
    {
        var cursadas = DatosLab.Pensum().Select(m => new MateriaCursada { Codigo = m.Codigo, Calificacion = "A", Periodo = new Periodo { Nombre = "MAY-AGO 2026" } });
        var r = MotorEstadoPensum.Calcular(DatosLab.Pensum(), cursadas, Array.Empty<CursoEnProgreso>(), Array.Empty<Equivalencia>());
        var ctx = ContextoPlan.Crear(r, new ConfigPlanificador(), 3.5m, ContextoLab.Primero);

        var plan = GeneradorPlan.Generar(ctx, CalculadoraPrioridades.Calcular(ctx));
        var ev = EvaluadorPlan.Evaluar(ctx, plan.Periodos);
        Assert.Empty(plan.Periodos);
        Assert.Null(ev.Graduacion);
        Assert.True(ev.EsValido);
    }
}

public class EvaluadorPlanTests
{
    private static readonly ContextoPlan Ctx = ContextoLab.Crear();

    private static List<PlanPeriodo> Plan(params string[][] periodos) =>
        periodos.Select((codigos, i) => new PlanPeriodo { Periodo = ContextoLab.Primero.Avanzar(i), Codigos = codigos.ToList() }).ToList();

    private static List<string> Problemas(EvaluacionPlan ev, string codigo) =>
        ev.Periodos.SelectMany(p => p.Materias).Single(m => m.Codigo == codigo).Problemas;

    [Fact]
    public void UnPlanCorrectoNoTieneProblemas()
    {
        var ev = EvaluadorPlan.Evaluar(Ctx, Plan(new[] { "ISO725", "ISO625" }, new[] { "ISO912" }));
        Assert.True(ev.EsValido);
        Assert.Equal(0, ev.Problemas);
        Assert.Equal(new[] { 8, 4 }, ev.Periodos.Select(p => p.Creditos));
    }

    [Fact]
    public void SumaLosCreditosDeCadaPeriodo()
    {
        var ev = EvaluadorPlan.Evaluar(Ctx, Plan(new[] { "ISO725", "ISO625", "ODEP" }, new[] { "ISO912" }));
        Assert.Equal(8, ev.Periodos[0].Creditos);   // 4 + 4 + 0
        Assert.Equal(4, ev.Periodos[1].Creditos);
    }

    [Fact]
    public void UnPrerrequisitoEnElMismoPeriodoNoVale()
    {
        var ev = EvaluadorPlan.Evaluar(Ctx, Plan(new[] { "ISO725", "ISO912" }));
        Assert.Contains("mismo período", Assert.Single(Problemas(ev, "ISO912")));
        Assert.False(ev.EsValido);
    }

    [Fact]
    public void UnPrerrequisitoPlanificadoDespuesTampocoVale()
    {
        var ev = EvaluadorPlan.Evaluar(Ctx, Plan(new[] { "ISO912" }, new[] { "ISO725" }));
        Assert.Contains("planificada después", Assert.Single(Problemas(ev, "ISO912")));
    }

    [Fact]
    public void UnPrerrequisitoNuncaPlanificadoSeIndica()
    {
        var ev = EvaluadorPlan.Evaluar(Ctx, Plan(new[] { "ISO912" }));
        Assert.Contains("no está aprobada ni planificada", Assert.Single(Problemas(ev, "ISO912")));
    }

    [Fact]
    public void LoQueEstaEnCursoCuentaComoAprobadoSiSeAsume()
    {
        Assert.True(EvaluadorPlan.Evaluar(Ctx, Plan(new[] { "ISO725" })).EsValido);   // requiere ISO720, que está en curso

        var sinAsumir = ContextoLab.Crear(new ConfigPlanificador(AsumirEnCurso: false));
        var ev = EvaluadorPlan.Evaluar(sinAsumir, new[] { new PlanPeriodo { Periodo = sinAsumir.Primero, Codigos = { "ISO725" } } });
        Assert.Contains("ISO720", Assert.Single(Problemas(ev, "ISO725")));
    }

    [Fact]
    public void ElPorcentajeSeProyectaAlInicioDelPeriodo()
    {
        // 150 aprobados (130 + 20 en curso) de 218 = 68.8 %. SOC281 pide 90 %.
        var ev = EvaluadorPlan.Evaluar(Ctx, Plan(new[] { "SOC281" }));
        Assert.Equal(68.8m, Math.Round(ev.Periodos[0].PorcentajeAlInicio, 1));
        Assert.Contains("90%", Assert.Single(Problemas(ev, "SOC281")));
        Assert.Contains("68.8%", Assert.Single(Problemas(ev, "SOC281")));

        // Con 25 créditos en el 1.er período y 24 en el 2.º, el 3.º empieza con 199 de 218 (91.3 %) y ya vale.
        var periodo1 = new[] { "ISO725", "ISO937", "ISO800", "ISO625", "DER010", "IDI046", "ING719", "E077" };   // 4+4+3+4+3+3+1+3 = 25
        var periodo2 = new[] { "ISO912", "ISO900", "ISO945", "ISO936", "DER800", "ISC835", "INF610" };            // 4+4+3+4+3+3+3 = 24
        var ev2 = EvaluadorPlan.Evaluar(Ctx, Plan(periodo1, periodo2, new[] { "SOC281" }));
        Assert.Equal(91.3m, Math.Round(ev2.Periodos[2].PorcentajeAlInicio, 1));
        Assert.Empty(Problemas(ev2, "SOC281"));
    }

    [Fact]
    public void AlertaCuandoUnPeriodoPasaElLimite()
    {
        var ev = EvaluadorPlan.Evaluar(Ctx, Plan(new[] { "ISO725", "ISO937", "ISO936", "ISO625", "ISO800", "DER010", "IDI046", "DER800", "ISC835" }));   // 4+4+4+4+3+3+3+3+3 = 31
        Assert.True(ev.Periodos[0].SobreLimite);
        Assert.Equal(31, ev.Periodos[0].Creditos);
        Assert.Contains("Pasa el límite: 31 créditos y el máximo es 25", Assert.Single(ev.Periodos[0].Alertas));
    }

    [Fact]
    public void AlertaPorDebajoDelMinimoSalvoEnElUltimoPeriodo()
    {
        var ctx = ContextoLab.Crear(new ConfigPlanificador(Minimo: 12));
        var ev = EvaluadorPlan.Evaluar(ctx, Plan(new[] { "ISO725" }, new[] { "ISO912" }, new[] { "ISO940" }));
        Assert.True(ev.Periodos[0].BajoMinimo);
        Assert.True(ev.Periodos[1].BajoMinimo);
        Assert.False(ev.Periodos[2].BajoMinimo);   // el último período no se avisa

        // Con el mínimo en 0 (el valor por defecto) no hay ninguna alerta.
        Assert.DoesNotContain(EvaluadorPlan.Evaluar(Ctx, Plan(new[] { "ISO725" }, new[] { "ISO912" })).Periodos, p => p.BajoMinimo);
    }

    [Fact]
    public void DetectaMateriasRepetidasAprobadasEnCursoYDesconocidas()
    {
        var ev = EvaluadorPlan.Evaluar(Ctx, Plan(new[] { "ISO625", "ISO100", "ISO720", "ZZZ999" }, new[] { "ISO625" }));
        Assert.Contains("Ya está aprobada", Problemas(ev, "ISO100").Single());
        Assert.Contains("Ya la estás cursando", Problemas(ev, "ISO720").Single());
        Assert.Contains("No existe", Problemas(ev, "ZZZ999").Single());
        Assert.Contains("repetida", ev.Periodos[1].Materias.Single().Problemas.Single());
    }

    [Fact]
    public void ElTfgSoloPuedeIrEnElUltimoPeriodo()
    {
        var ev = EvaluadorPlan.Evaluar(Ctx, Plan(new[] { "TFG", "ISO725" }, new[] { "ISO912" }));
        Assert.Contains("TFG", Assert.Single(ev.Errores));
        Assert.False(ev.EsValido);
        Assert.True(EvaluadorPlan.Evaluar(Ctx, Plan(new[] { "ISO725" }, new[] { "ISO912", "TFG" })).EsValido);
    }

    [Fact]
    public void UnPeriodoAnteriorAlPrimeroPlanificableAdvierte()
    {
        var plan = new[] { new PlanPeriodo { Periodo = PeriodoAcademico.Parse("SEP-DIC 2026"), Codigos = { "ISO625" } } };
        var ev = EvaluadorPlan.Evaluar(Ctx, plan);
        Assert.Contains("ya pasó o está en curso", Assert.Single(ev.Periodos[0].Alertas));
    }

    [Fact]
    public void ElResumenCuentaLoQueFaltaYLoQueSobra()
    {
        var ev = EvaluadorPlan.Evaluar(Ctx, Plan(new[] { "ISO725", "ISO625" }));
        Assert.Equal(68, ev.CreditosFaltantes);
        Assert.Equal(60, ev.CreditosSinPlanificar);   // 68 − 8
        Assert.Equal(20, ev.SinPlanificar.Count);
        Assert.Equal(1, ev.CuatrimestresPlanificados);
        Assert.False(ev.TodoPlanificado);
    }

    [Fact]
    public void LaGraduacionSeEstimaSiFaltaPlanificar()
    {
        var vacio = EvaluadorPlan.Evaluar(Ctx, Array.Empty<PlanPeriodo>());
        Assert.True(vacio.GraduacionEstimada);
        Assert.Equal("SEP-DIC 2027", vacio.Graduacion!.Value.Nombre);   // 68 créditos ÷ 25 = 3 períodos desde ENE-ABR 2027

        var parcial = EvaluadorPlan.Evaluar(Ctx, Plan(new[] { "ISO725", "ISO625" }));
        Assert.True(parcial.GraduacionEstimada);
        Assert.Equal("ENE-ABR 2028", parcial.Graduacion!.Value.Nombre);   // termina el plan en ENE-ABR 2027 + ceil(60 ÷ 25) = 3 períodos más
    }

    [Fact]
    public void ElOrdenDeLosPeriodosNoDependeDelOrdenDeEntrada()
    {
        var plan = Plan(new[] { "ISO725" }, new[] { "ISO912" });
        plan.Reverse();
        Assert.True(EvaluadorPlan.Evaluar(Ctx, plan).EsValido);
    }
}
