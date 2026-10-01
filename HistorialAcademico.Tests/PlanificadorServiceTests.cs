using HistorialAcademico.Core.Entities;
using HistorialAcademico.Core.Pensum;
using HistorialAcademico.Core.Planificacion;
using HistorialAcademico.Web.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HistorialAcademico.Tests;

/// <summary>Persistencia y operaciones del planificador con SQLite en memoria (migraciones reales).</summary>
public class PlanificadorServiceTests
{
    /// <summary>Base con el histórico sintético (períodos ENE-ABR y MAY-AGO 2025, en progreso SEP-DIC 2025) y el pénsum real del CSV.</summary>
    private static async Task<(BdPrueba bd, PlanificadorService svc)> PrepararAsync(bool conDatos = true)
    {
        var bd = new BdPrueba();
        if (conDatos)
        {
            var r = await bd.Sync.AplicarHtmlAsync(Muestras.LeerSintetico());
            Assert.True(r.Exito, r.Mensaje);
            bd.Db.MateriasPensum.AddRange(DatosLab.Pensum());
            await bd.Db.SaveChangesAsync();
        }
        return (bd, new PlanificadorService(bd.Db, new AcademicoService(bd.Db)));
    }

    // ── Configuración ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LaConfiguracionInicialEs25_27_340_0_YAsumirEnCurso()
    {
        var (bd, svc) = await PrepararAsync();
        using var _ = bd;
        Assert.Equal(new ConfigPlanificador(), await svc.ObtenerConfigAsync());
    }

    [Fact]
    public async Task GuardaYRecuperaLaConfiguracion()
    {
        var (bd, svc) = await PrepararAsync();
        using var _ = bd;
        var nueva = new ConfigPlanificador(LimiteBase: 22, LimiteAlto: 26, UmbralIndice: 3.5m, Minimo: 12, AsumirEnCurso: false);

        Assert.True((await svc.GuardarConfigAsync(nueva)).Ok);
        Assert.Equal(nueva, await svc.ObtenerConfigAsync());

        // Guardar otra vez actualiza la misma fila.
        Assert.True((await svc.GuardarConfigAsync(new ConfigPlanificador())).Ok);
        Assert.Equal(1, await bd.Db.ConfiguracionPlanificador.CountAsync());
    }

    [Theory]
    [InlineData(0, 27, 3.4, 0)]      // límite fuera de rango
    [InlineData(61, 61, 3.4, 0)]
    [InlineData(25, 24, 3.4, 0)]     // el alto no puede ser menor que el normal
    [InlineData(25, 27, 4.5, 0)]     // umbral fuera de rango
    [InlineData(25, 27, 3.4, 26)]    // mínimo mayor que el límite
    [InlineData(25, 27, 3.4, -1)]
    public async Task RechazaConfiguracionesInvalidas(int b, int a, double umbral, int min)
    {
        var (bd, svc) = await PrepararAsync();
        using var _ = bd;
        var r = await svc.GuardarConfigAsync(new ConfigPlanificador(b, a, (decimal)umbral, min));
        Assert.False(r.Ok);
        Assert.Equal(0, await bd.Db.ConfiguracionPlanificador.CountAsync());
    }

    // ── Estado y escenarios ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task SinHistoricoOSinPenumExplicaQueFalta()
    {
        var (bd, svc) = await PrepararAsync(conDatos: false);
        using var _ = bd;
        var st = await svc.ObtenerAsync();
        Assert.Null(st.Contexto);
        Assert.Contains("sincroniza tu histórico", st.NoPuedePlanificar);
        Assert.Equal("Plan 1", st.Actual!.Nombre);   // el primer plan se crea solo

        await bd.Sync.AplicarHtmlAsync(Muestras.LeerSintetico());
        Assert.Contains("pénsum", (await svc.ObtenerAsync()).NoPuedePlanificar);
    }

    [Fact]
    public async Task ElPrimerPeriodoEsElSiguienteAlDeLosCursosEnProgreso()
    {
        var (bd, svc) = await PrepararAsync();
        using var _ = bd;
        var st = await svc.ObtenerAsync();
        Assert.Equal("ENE-ABR 2026", st.Contexto!.Primero.Nombre);   // en progreso: SEP-DIC 2025
        Assert.Equal(new[] { "ENE-ABR 2026", "MAY-AGO 2026", "SEP-DIC 2026" }, st.Columnas.Select(c => c.Nombre));

        await svc.GuardarConfigAsync(new ConfigPlanificador(AsumirEnCurso: false));
        Assert.Equal("SEP-DIC 2025", (await svc.ObtenerAsync()).Contexto!.Primero.Nombre);
    }

    [Fact]
    public async Task CreaEscenariosConNombreUnico()
    {
        var (bd, svc) = await PrepararAsync();
        using var _ = bd;
        await svc.ObtenerAsync();   // crea "Plan 1"

        Assert.True((await svc.CrearPlanAsync("Carga normal")).Ok);
        Assert.False((await svc.CrearPlanAsync("carga NORMAL")).Ok);     // mismo nombre sin importar mayúsculas
        Assert.False((await svc.CrearPlanAsync("   ")).Ok);
        Assert.False((await svc.CrearPlanAsync(new string('x', 61))).Ok);
        Assert.Equal(new[] { "Plan 1", "Carga normal" }, (await svc.ObtenerAsync()).Planes.Select(p => p.Nombre));
    }

    [Fact]
    public async Task RenombraSinChocarConOtros()
    {
        var (bd, svc) = await PrepararAsync();
        using var _ = bd;
        var a = (await svc.CrearPlanAsync("A")).Id!.Value;
        await svc.CrearPlanAsync("B");

        Assert.False((await svc.RenombrarAsync(a, "b")).Ok);
        Assert.True((await svc.RenombrarAsync(a, "A")).Ok);      // el mismo nombre no cuenta como duplicado
        Assert.True((await svc.RenombrarAsync(a, "Carga pesada")).Ok);
        Assert.Contains("Carga pesada", (await svc.ObtenerAsync()).Planes.Select(p => p.Nombre));
    }

    [Fact]
    public async Task ElimiaUnPlanConSusPeriodosYMaterias_PeroNuncaElUltimo()
    {
        var (bd, svc) = await PrepararAsync();
        using var _ = bd;
        var st = await svc.ObtenerAsync();
        var otro = (await svc.CrearPlanAsync("Otro")).Id!.Value;
        await svc.AsignarAsync(otro, "ISO100", "ENE-ABR 2026");

        Assert.True((await svc.EliminarAsync(otro)).Ok);
        Assert.Equal(0, await bd.Db.PeriodosPlanificados.CountAsync());   // cascada
        Assert.Equal(0, await bd.Db.MateriasPlanificadas.CountAsync());

        Assert.False((await svc.EliminarAsync(st.Actual!.Id)).Ok);        // es el último
        Assert.Equal(1, await bd.Db.PlanesEstudio.CountAsync());
    }

    // ── Edición ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AsignaMueveYQuitaMaterias()
    {
        var (bd, svc) = await PrepararAsync();
        using var _ = bd;
        var id = (await svc.ObtenerAsync()).Actual!.Id;

        Assert.True((await svc.AsignarAsync(id, "iso100", "ENE-ABR 2026")).Ok);          // el código se acepta en minúsculas
        await svc.AsignarAsync(id, "SOC013", "ENE-ABR 2026");
        var st = await svc.ObtenerAsync(id);
        Assert.Equal(new[] { "ISO100", "SOC013" }, st.Plan.Single().Codigos.OrderBy(c => c));
        Assert.DoesNotContain(st.PorPlanificar, m => m.Materia.Codigo == "ISO100");

        await svc.AsignarAsync(id, "ISO100", "MAY-AGO 2026");                              // mover
        st = await svc.ObtenerAsync(id);
        Assert.Equal(new[] { "SOC013" }, st.Plan.Single(p => p.Periodo.Nombre == "ENE-ABR 2026").Codigos);
        Assert.Equal(new[] { "ISO100" }, st.Plan.Single(p => p.Periodo.Nombre == "MAY-AGO 2026").Codigos);

        await svc.AsignarAsync(id, "ISO100", null);                                        // quitar
        st = await svc.ObtenerAsync(id);
        Assert.Single(st.Plan);                                                            // el período que quedó vacío se elimina
        Assert.Contains(st.PorPlanificar, m => m.Materia.Codigo == "ISO100");
    }

    [Fact]
    public async Task RechazaMateriasOPeriodosQueNoExisten()
    {
        var (bd, svc) = await PrepararAsync();
        using var _ = bd;
        var id = (await svc.ObtenerAsync()).Actual!.Id;

        Assert.False((await svc.AsignarAsync(id, "ZZZ999", "ENE-ABR 2026")).Ok);
        Assert.False((await svc.AsignarAsync(id, "", "ENE-ABR 2026")).Ok);
        Assert.False((await svc.AsignarAsync(id, "ISO100", "OTRO 2026")).Ok);
        Assert.False((await svc.AsignarAsync(9999, "ISO100", "ENE-ABR 2026")).Ok);
        Assert.Equal(0, await bd.Db.MateriasPlanificadas.CountAsync());
    }

    [Fact]
    public async Task UnaMateriaSoloPuedeEstarUnaVezEnUnPlan()
    {
        var (bd, svc) = await PrepararAsync();
        using var _ = bd;
        var id = (await svc.ObtenerAsync()).Actual!.Id;
        await svc.AsignarAsync(id, "ISO100", "ENE-ABR 2026");
        await svc.AsignarAsync(id, "ISO100", "ENE-ABR 2026");
        await svc.AsignarAsync(id, "ISO100", "MAY-AGO 2026");
        Assert.Equal(1, await bd.Db.MateriasPlanificadas.CountAsync(m => m.Codigo == "ISO100"));
    }

    [Fact]
    public async Task ValidaElPlanEnTiempoReal()
    {
        var (bd, svc) = await PrepararAsync();
        using var _ = bd;
        var id = (await svc.ObtenerAsync()).Actual!.Id;
        await svc.AsignarAsync(id, "ISO615", "ENE-ABR 2026");   // requiere INF165 (no aprobada)
        var ev = (await svc.ObtenerAsync(id)).Evaluacion!;
        Assert.False(ev.EsValido);
        Assert.Contains("INF165", ev.Periodos[0].Materias[0].Problemas.Single());

        await svc.AsignarAsync(id, "INF165", "ENE-ABR 2026");   // el mismo período no sirve
        Assert.False((await svc.ObtenerAsync(id)).Evaluacion!.EsValido);

        await svc.AsignarAsync(id, "INF165", "ENE-ABR 2026");
        await svc.AsignarAsync(id, "ISO615", "MAY-AGO 2026");    // en el siguiente sí
        Assert.True((await svc.ObtenerAsync(id)).Evaluacion!.Periodos.All(p => p.Materias.All(m => m.Problemas.Count == 0 || m.Codigo != "ISO615")));
    }

    // ── Plan sugerido y escenarios ────────────────────────────────────────────────────────

    [Fact]
    public async Task GeneraUnPlanSugeridoValidoConRazonesYLoGuarda()
    {
        var (bd, svc) = await PrepararAsync();
        using var _ = bd;
        var id = (await svc.ObtenerAsync()).Actual!.Id;

        var r = await svc.GenerarAsync(id);
        Assert.True(r.Ok, r.Mensaje);
        Assert.Contains("Graduación estimada", r.Mensaje);

        var st = await svc.ObtenerAsync(id);
        Assert.True(st.Evaluacion!.EsValido);
        Assert.True(st.Evaluacion.TodoPlanificado);
        Assert.Empty(st.PorPlanificar);
        Assert.Equal(st.Contexto!.Pendientes.Count, st.Plan.Sum(p => p.Codigos.Count));
        Assert.Equal(st.Contexto.Pendientes.Count, st.Razones.Count);                        // cada materia guarda su razón
        Assert.All(st.Evaluacion.Periodos, p => Assert.True(p.Creditos <= st.Contexto.Limite));
        Assert.Contains("TFG", st.Plan[^1].Codigos);
    }

    [Fact]
    public async Task UnaMateriaMovidaAManoPierdeLaRazonDelPlanSugerido()
    {
        var (bd, svc) = await PrepararAsync();
        using var _ = bd;
        var id = (await svc.ObtenerAsync()).Actual!.Id;
        await svc.GenerarAsync(id);
        var conRazon = (await svc.ObtenerAsync(id)).Razones.Keys.First(c => c != "TFG");

        await svc.AsignarAsync(id, conRazon, "SEP-DIC 2030");
        Assert.DoesNotContain(conRazon, (await svc.ObtenerAsync(id)).Razones.Keys, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GenerarReemplazaLoQueHabiaYNoTocaOtrosPlanes()
    {
        var (bd, svc) = await PrepararAsync();
        using var _ = bd;
        var a = (await svc.ObtenerAsync()).Actual!.Id;
        var b = (await svc.CrearPlanAsync("B")).Id!.Value;
        await svc.AsignarAsync(a, "SOC013", "SEP-DIC 2031");
        await svc.AsignarAsync(b, "SOC013", "SEP-DIC 2031");

        await svc.GenerarAsync(a);

        var stA = await svc.ObtenerAsync(a);
        Assert.DoesNotContain(stA.Plan, p => p.Periodo.Nombre == "SEP-DIC 2031");
        Assert.Equal("SEP-DIC 2031", (await svc.ObtenerAsync(b)).Plan.Single().Periodo.Nombre);
    }

    [Fact]
    public async Task GuardarComoEscenarioCopiaElPlanYLuegoSonIndependientes()
    {
        var (bd, svc) = await PrepararAsync();
        using var _ = bd;
        var origen = (await svc.ObtenerAsync()).Actual!.Id;
        await svc.GenerarAsync(origen);

        var copia = await svc.CrearPlanAsync("Carga normal", copiarDe: origen);
        Assert.True(copia.Ok);
        var stOrigen = await svc.ObtenerAsync(origen);
        var stCopia = await svc.ObtenerAsync(copia.Id);
        Assert.Equal(stOrigen.Plan.Select(p => string.Join(",", p.Codigos.OrderBy(c => c))), stCopia.Plan.Select(p => string.Join(",", p.Codigos.OrderBy(c => c))));
        Assert.Equal(stOrigen.Razones.Count, stCopia.Razones.Count);

        await svc.LimpiarAsync(copia.Id!.Value);
        Assert.Empty((await svc.ObtenerAsync(copia.Id)).Plan);
        Assert.NotEmpty((await svc.ObtenerAsync(origen)).Plan);    // el original no cambió
    }

    [Fact]
    public async Task CompararEvaluaTodosLosEscenariosConLasMismasReglas()
    {
        var (bd, svc) = await PrepararAsync();
        using var _ = bd;
        var a = (await svc.ObtenerAsync()).Actual!.Id;
        var b = (await svc.CrearPlanAsync("Vacío")).Id!.Value;
        await svc.GenerarAsync(a);

        var (_, escenarios) = await svc.CompararAsync();
        Assert.Equal(2, escenarios.Count);
        var generado = escenarios.Single(e => e.Plan.Id == a).Evaluacion;
        var vacio = escenarios.Single(e => e.Plan.Id == b).Evaluacion;
        Assert.True(generado.TodoPlanificado);
        Assert.False(generado.GraduacionEstimada);
        Assert.False(vacio.TodoPlanificado);
        Assert.True(vacio.GraduacionEstimada);
        Assert.Equal(0, vacio.CuatrimestresPlanificados);
    }

    [Fact]
    public async Task ConTusDatosRealesLasRezagadasSonLasEsperadasYElPlanEsValido()
    {
        using var bd = new BdPrueba();
        var r = await bd.Sync.AplicarHtmlAsync(Muestras.LeerAnonimizado());
        Assert.True(r.Exito, r.Mensaje);
        bd.Db.MateriasPensum.AddRange(DatosLab.Pensum());
        bd.Db.Equivalencias.AddRange(EquivalenciasIniciales.Valores);   // ING701, ESP102 y MAT126
        await bd.Db.SaveChangesAsync();
        var svc = new PlanificadorService(bd.Db, new AcademicoService(bd.Db));

        var st = await svc.ObtenerAsync();
        Assert.Equal("ENE-ABR 2027", st.Contexto!.Primero.Nombre);
        Assert.Equal(25, st.Contexto.Limite);          // índice 2.62: no supera 3.40
        Assert.Equal(7, st.Prioridades!.NivelActual);

        // ODEP (cuat. 2), ING719 (cuat. 5) e ISO625 (cuat. 6) son las esperadas; SOC013 Filosofía (cuat. 4) también,
        // porque en este histórico no se ha tomado.
        var rezagadas = st.Prioridades.Materias.Where(m => m.Rezagada).Select(m => m.Materia.Codigo).OrderBy(c => c);
        Assert.Equal(new[] { "ING719", "ISO625", "ODEP", "SOC013" }, rezagadas);

        var r2 = await svc.GenerarAsync(st.Actual!.Id);
        Assert.True(r2.Ok, r2.Mensaje);
        var ev = (await svc.ObtenerAsync(st.Actual.Id)).Evaluacion!;
        Assert.True(ev.EsValido);
        Assert.True(ev.TodoPlanificado);
        Assert.All(ev.Periodos, p => Assert.True(p.Creditos <= 25));

        // 70 créditos por planificar caben en 3 períodos de 25 solo si los dos primeros suman ≥ 49 (para llegar al 90 % del Seminario).
        Assert.Equal(3, ev.CuatrimestresPlanificados);
        Assert.Equal("SEP-DIC 2027", ev.Graduacion!.Value.Nombre);
    }
}
