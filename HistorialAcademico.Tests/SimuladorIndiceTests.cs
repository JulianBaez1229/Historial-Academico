using HistorialAcademico.Core.Entities;
using HistorialAcademico.Core.Indice;
using HistorialAcademico.Core.Pensum;
using HistorialAcademico.Core.Planificacion;
using HistorialAcademico.Core.Universidad;
using Xunit;

namespace HistorialAcademico.Tests;

/// <summary>El simulador de índice: qué índice quedaría con las notas que se esperan y qué promedio hace falta en el resto.</summary>
public class SimuladorIndiceTests
{
    private static readonly EscalaCalificaciones Escala = EscalaCalificaciones.Unapec;

    /// <summary>100 créditos que cuentan y 300 puntos de calidad: índice 3.00.</summary>
    private static readonly TotalesIndice Hoy = new(100, 100, 100, 300, 3.00m);

    private static ResultadoSimulacion S(IEnumerable<FilaSimulacion>? filas = null, decimal sinFila = 0, decimal objetivo = 3.50m, TotalesIndice? actual = null) =>
        SimuladorIndice.Calcular(actual ?? Hoy, filas ?? Array.Empty<FilaSimulacion>(), sinFila, objetivo, Escala);

    private static FilaSimulacion F(string codigo, decimal cr, string? letra) => new(codigo, cr, letra);

    [Fact]
    public void SinNadaQueSimularElProyectadoEsElActualYNoHayNadaPorDefinir()
    {
        var r = S();

        Assert.Equal((3.00m, 3.00m, 0m, 0m), (r.IndiceActual, r.IndiceProyectado, r.CreditosConNota, r.CreditosPorDefinir));
        Assert.Equal(EstadoObjetivo.TodoConNota, r.Estado);
    }

    [Fact]
    public void LaNotaEsperadaMueveElIndiceComoLaCalculadoraReal()
    {
        // 300 + 4×3 = 312 puntos en 103 créditos = 3.029… → 3.03
        var r = S(new[] { F("AAA100", 3, "A") });

        Assert.Equal(3.03m, r.IndiceProyectado);
        Assert.Equal(3m, r.CreditosConNota);
        Assert.Equal(EstadoObjetivo.TodoConNota, r.Estado);
    }

    [Fact]
    public void UnaFEnLaEscalaDeUnapecBajaElIndiceAunqueNoAprueba()
    {
        var r = S(new[] { F("AAA100", 4, "F") });   // 300 / 104 = 2.88

        Assert.Equal(2.88m, r.IndiceProyectado);
    }

    [Fact]
    public void LaExentaNoMueveElIndiceNiCuentaComoConNota()
    {
        var r = S(new[] { F("AAA100", 3, "E") });

        Assert.Equal(3.00m, r.IndiceProyectado);
        Assert.Equal(0m, r.CreditosConNota);
        Assert.Equal(0m, r.CreditosPorDefinir);   // tiene nota: no falta definirla
    }

    [Fact]
    public void LaLetraEnMinusculaSeEntiendeYUnaQueNoExisteEsDeLasQueFaltanPorDefinir()
    {
        var r = S(new[] { F("AAA100", 3, "b"), F("BBB200", 3, "Z"), F("CCC300", 3, "") });

        Assert.Equal(3m, r.CreditosConNota);
        Assert.Equal(6m, r.CreditosPorDefinir);
    }

    [Fact]
    public void LasMateriasSinCreditosNoCuentan()
    {
        var r = S(new[] { F("PAS100", 0, null), F("PAS101", 0, "A") });

        Assert.Equal(0m, r.CreditosPorDefinir);
        Assert.Equal(3.00m, r.IndiceProyectado);
    }

    [Fact]
    public void LoQueFaltaPorDefinirIncluyeLasMateriasSinPlanificar()
    {
        var r = S(new[] { F("AAA100", 3, null) }, sinFila: 30);

        Assert.Equal(33m, r.CreditosPorDefinir);
    }

    [Fact]
    public void ElPromedioNecesarioSeCalculaSobreLoQueFalta()
    {
        // 100 créditos y 300 puntos; faltan 100 créditos; objetivo 3.25 → (3.25×200 − 300) / 100 = 3.50
        var r = S(sinFila: 100, objetivo: 3.25m);

        Assert.Equal(EstadoObjetivo.Alcanzable, r.Estado);
        Assert.Equal(3.50m, r.PromedioNecesario);
        Assert.Equal("A", r.LetraMinima);   // con B (3) no alcanza: hace falta A
        Assert.Equal(3.50m, r.MaximoPosible);
    }

    [Fact]
    public void ConNotasYaPuestasElPromedioNecesarioSoloMiraLoQueQuedaSinNota()
    {
        // Ya se esperan A en 20 créditos: 380 puntos en 120; faltan 80 créditos; objetivo 3.4 → (3.4×200 − 380) / 80 = 3.75
        var r = S(new[] { F("AAA100", 20, "A") }, sinFila: 80, objetivo: 3.40m);

        Assert.Equal(EstadoObjetivo.Alcanzable, r.Estado);
        Assert.Equal(3.75m, r.PromedioNecesario);
        Assert.Equal("A", r.LetraMinima);
        Assert.Equal(80m, r.CreditosPorDefinir);
    }

    [Fact]
    public void SiElPromedioNecesarioEsBajoSeDiceQueLetraBasta()
    {
        // 100 créditos y 300 puntos (3.00); faltan 100; objetivo 3.00 → basta un 3.00 en lo que falta: con B se llega justo.
        var r = S(sinFila: 100, objetivo: 3.00m);

        Assert.Equal(EstadoObjetivo.Alcanzable, r.Estado);
        Assert.Equal(3.00m, r.PromedioNecesario);
        Assert.Equal("B", r.LetraMinima);
    }

    [Fact]
    public void SiNiConLaNotaMasAltaSeLlegaEsImposible()
    {
        var r = S(sinFila: 20, objetivo: 3.50m);   // máximo: (300 + 80) / 120 = 3.17

        Assert.Equal(EstadoObjetivo.Imposible, r.Estado);
        Assert.Equal(3.17m, r.MaximoPosible);
        Assert.Equal(6m, r.PromedioNecesario);   // (3.5×120 − 300) / 20: más de 4, por eso no se puede
    }

    [Fact]
    public void SiElObjetivoYaEstaAseguradoAunqueSaqueFEnTodoSeDice()
    {
        // 100 créditos y 380 puntos (3.80); faltan 5; con F en todo: 380 / 105 = 3.62 ≥ 3.50
        var r = S(sinFila: 5, objetivo: 3.50m, actual: new TotalesIndice(100, 100, 100, 380, 3.80m));

        Assert.Equal(EstadoObjetivo.Asegurado, r.Estado);
        Assert.Null(r.PromedioNecesario);
    }

    [Fact]
    public void LaComparacionUsaElIndiceRedondeadoQueVeLaPersona()
    {
        // 699 puntos en 200 créditos = 3.495 → se muestra 3.50 y cumple el objetivo de 3.50.
        var r = S(objetivo: 3.50m, actual: new TotalesIndice(200, 200, 200, 699, 3.50m));

        Assert.Equal(3.50m, r.IndiceProyectado);
        Assert.Contains("que alcanza", SimuladorIndice.Describir(r, 3.50m, "A"));
    }

    [Fact]
    public void SinHorasQueContarElIndiceEsCero()
    {
        var r = S(new[] { F("AAA100", 3, "E") }, actual: new TotalesIndice(0, 0, 0, 0, 0));

        Assert.Equal(0m, r.IndiceProyectado);
    }

    // ── La frase que se le dice a la persona ──────────────────────────────────────────────

    [Fact]
    public void LaFraseDeCadaEstado()
    {
        Assert.Equal("Le pusiste nota a todo lo que falta: tu índice quedaría en 3.03, que no alcanza el objetivo de 3.50.",
            SimuladorIndice.Describir(S(new[] { F("AAA100", 3, "A") }), 3.50m, "A"));
        Assert.Equal("Para llegar a 3.25 necesitas un promedio de 3.50 en los 100 créditos que faltan por definir (por ejemplo, A en todo).",
            SimuladorIndice.Describir(S(sinFila: 100, objetivo: 3.25m), 3.25m, "A"));
        Assert.Equal("El objetivo de 3.50 no se alcanza: ni con A en todo lo que falta (20 créditos) llegarías a más de 3.17.",
            SimuladorIndice.Describir(S(sinFila: 20), 3.50m, "A"));
        Assert.Equal("El objetivo de 3.50 ya lo tienes asegurado: se alcanza aunque lo que falta (5 créditos) salga con la nota más baja.",
            SimuladorIndice.Describir(S(sinFila: 5, actual: new TotalesIndice(100, 100, 100, 380, 3.80m)), 3.50m, "A"));
    }

    [Fact]
    public void LosCreditosSeEscribenSinCerosDeSobra() =>
        Assert.Contains("en los 12.5 créditos", SimuladorIndice.Describir(S(sinFila: 12.5m, objetivo: 3.05m), 3.05m, "A"));
}

/// <summary>Promedio de créditos y período más pesado de un plan (para comparar escenarios).</summary>
public class ResumenDePlanTests
{
    private static readonly PeriodoAcademico P0 = new(2027, 0);

    private static ContextoPlan Contexto()
    {
        var pensum = Enumerable.Range(1, 9).Select(i => new MateriaPensum { Codigo = $"M{i:00}", Nombre = $"Materia {i}", Creditos = i <= 6 ? 3 : 4, Cuatrimestre = 1 }).ToList();
        var r = MotorEstadoPensum.Calcular(pensum, Array.Empty<MateriaCursada>(), Array.Empty<CursoEnProgreso>(), Array.Empty<Equivalencia>());
        return ContextoPlan.Crear(r, new ConfigPlanificador(25, 25), 0m, P0);
    }

    private static PlanPeriodo Per(int k, params string[] codigos) => new() { Periodo = P0.Avanzar(k), Codigos = codigos.ToList() };

    [Fact]
    public void ElPromedioSoloCuentaLosPeriodosConMaterias()
    {
        var ctx = Contexto();

        // 6 créditos, un período vacío en medio (no cuenta) y 11 créditos: promedio (6 + 11) / 2 = 8.5
        var ev = EvaluadorPlan.Evaluar(ctx, new[] { Per(0, "M01", "M02"), Per(1), Per(2, "M03", "M07", "M08") });

        Assert.Equal(8.5m, ev.PromedioCreditos);
        Assert.Equal(P0.Avanzar(2), ev.PeriodoMasPesado!.Periodo);
        Assert.Equal(11, ev.PeriodoMasPesado.Creditos);
    }

    [Fact]
    public void ConEmpateElPesadoEsElPrimeroEnElTiempo()
    {
        var ev = EvaluadorPlan.Evaluar(Contexto(), new[] { Per(0, "M01", "M02"), Per(1, "M03", "M04") });

        Assert.Equal(P0, ev.PeriodoMasPesado!.Periodo);
    }

    [Fact]
    public void ConUnPlanVacioNoHayPromedioNiPeriodoPesado()
    {
        var ev = EvaluadorPlan.Evaluar(Contexto(), Array.Empty<PlanPeriodo>());

        Assert.Equal(0m, ev.PromedioCreditos);
        Assert.Null(ev.PeriodoMasPesado);
    }

    [Fact]
    public void ElPromedioSeRedondeaAUnDecimal()
    {
        // 3 + 3 + 4 = 10 créditos en 3 períodos = 3.33…
        var ev = EvaluadorPlan.Evaluar(Contexto(), new[] { Per(0, "M01"), Per(1, "M02"), Per(2, "M07") });

        Assert.Equal(3.3m, ev.PromedioCreditos);
    }
}
