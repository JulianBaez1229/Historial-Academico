using HistorialAcademico.Core.Entities;
using HistorialAcademico.Core.Pensum;
using HistorialAcademico.Core.Planificacion;
using HistorialAcademico.Core.Universidad;
using HistorialAcademico.Web.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;

namespace HistorialAcademico.Tests;

/// <summary>Un entorno de hospedaje mínimo, para probar el servicio de reglas sin levantar la aplicación.</summary>
internal sealed class EntornoFalso : IWebHostEnvironment
{
    public EntornoFalso(string raiz) => ContentRootPath = raiz;
    public string ApplicationName { get; set; } = "prueba";
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    public string WebRootPath { get; set; } = "";
    public string EnvironmentName { get; set; } = "Development";
    public string ContentRootPath { get; set; }
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}

/// <summary>De dónde salen las reglas de la universidad activa y qué pasa cuando no se pueden leer.</summary>
public class ReglasUniversidadServiceTests
{
    private static ReglasUniversidadService Crear(string raiz, params (string Clave, string Valor)[] configuracion) =>
        new(new EntornoFalso(raiz), new ConfigurationBuilder().AddInMemoryCollection(configuracion.Select(c => new KeyValuePair<string, string?>(c.Clave, c.Valor))).Build(),
            NullLogger<ReglasUniversidadService>.Instance);

    private static string CarpetaTemporal()
    {
        var dir = Path.Combine(Path.GetTempPath(), "reglas-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void LaCarpetaDePensumsSeBuscaDesdeLaAplicacionHaciaArribaYSeLeeUnapec()
    {
        var s = Crear(Path.Combine(PensumEjemplo.Raiz, "HistorialAcademico.Web"));

        Assert.Equal(ReglasUniversidad.Unapec, s.Activa);
        Assert.EndsWith("universidad.json", s.Origen);
        Assert.Empty(s.Problemas);
    }

    [Fact]
    public void LaCarpetaSePuedeIndicarPorConfiguracion()
    {
        var dir = CarpetaTemporal();
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "uni-prueba"));
            File.WriteAllText(Path.Combine(dir, "uni-prueba", "universidad.json"), UniversidadDePrueba.Json);

            var s = Crear(dir, ("Pensums:Carpeta", dir), ("Pensums:Universidad", "uni-prueba"));

            Assert.Equal("uni-prueba", s.Activa.Id);
            Assert.Equal(new[] { "A", "F", "P" }, s.Activa.Escala.Letras.Select(l => l.Letra));   // la escala de la universidad, no la de UNAPEC
            Assert.Equal("SEM1, SEM2", s.Activa.Periodos.ToString());
            Assert.Empty(s.Problemas);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void UnaUniversidadQueNoExisteCaeEnUnapecYLoDice()
    {
        var s = Crear(PensumEjemplo.Raiz, ("Pensums:Carpeta", PensumEjemplo.CarpetaPensums), ("Pensums:Universidad", "no-existe"));

        Assert.Equal(ReglasUniversidad.Unapec, s.Activa);
        Assert.Equal("valores incorporados de UNAPEC", s.Origen);
        Assert.Contains(s.Problemas, p => p.Contains("No encontré pensums/no-existe/universidad.json"));
    }

    [Fact]
    public void UnArchivoConErroresNoTumbaLaAplicacionSoloDejaElMotivo()
    {
        var dir = CarpetaTemporal();
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "unapec"));
            File.WriteAllText(Path.Combine(dir, "unapec", "universidad.json"), UniversidadDePrueba.Json.Replace("\"id\": \"uni-prueba\"", "\"id\": \"unapec\"").Replace("\"base\": 18", "\"base\": 0"));

            var s = Crear(dir, ("Pensums:Carpeta", dir));

            Assert.Equal(ReglasUniversidad.Unapec, s.Activa);
            Assert.Contains(s.Problemas, p => p.StartsWith("pensums/unapec/universidad.json: ") && p.Contains("entre 1 y 60"));
            Assert.Contains(s.Problemas, p => p.Contains("Uso las reglas incorporadas de UNAPEC"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void SinCarpetaDePensumsUsaLasReglasIncorporadas()
    {
        var vacio = CarpetaTemporal();
        try
        {
            // Una carpeta indicada que no existe: no hay catálogo y no se cae.
            var s = Crear(vacio, ("Pensums:Carpeta", Path.Combine(vacio, "no-hay")));

            Assert.Equal(ReglasUniversidad.Unapec, s.Activa);
            Assert.NotEmpty(s.Problemas);
        }
        finally { Directory.Delete(vacio, true); }
    }

    [Fact]
    public void ConReglasFijasNoLeeNingunArchivo()
    {
        var s = ReglasUniversidadService.Fijas(UniversidadDePrueba.Reglas);

        Assert.Equal(UniversidadDePrueba.Reglas, s.Activa);
        Assert.Empty(s.Problemas);
    }
}

/// <summary>Con otras reglas, la aplicación calcula el índice, el estado de las materias y el planificador de otra manera.</summary>
public class ReglasEnLaAplicacionTests
{
    private static ReglasUniversidadService Otra => ReglasUniversidadService.Fijas(UniversidadDePrueba.Reglas);

    /// <summary>Un histórico en la universidad de prueba: períodos SEM1/SEM2, letras de su escala, y SEM2 2025 en progreso.</summary>
    private static async Task<BdPrueba> BaseDeLaOtraUniversidadAsync()
    {
        var bd = new BdPrueba();
        bd.Db.MateriasPensum.AddRange(DatosLab.Pensum());
        bd.Db.Periodos.Add(new Periodo
        {
            Nombre = "SEM1 2025", Orden = 1,
            Materias = new()
            {
                new MateriaCursada { Codigo = "ESP101", Calificacion = "A", HorasCredito = 3 },
                new MateriaCursada { Codigo = "ISO100", Calificacion = "P", HorasCredito = 5 },      // «pasó»: aprueba sin entrar en el índice
                new MateriaCursada { Codigo = "SOC011", Calificacion = "E", HorasCredito = 3 },      // reprueba y baja el promedio
            },
        });
        bd.Db.CursosEnProgreso.Add(new CursoEnProgreso { Periodo = "SEM2 2025", Codigo = "MAT127", Materia = "MAT", Curso = "127", Titulo = "MATEMATICA", HorasCredito = 5 });
        await bd.Db.SaveChangesAsync();
        return bd;
    }

    [Fact]
    public async Task ElEstadoAcademicoTraeLasReglasYCalculaConSuEscala()
    {
        using var bd = await BaseDeLaOtraUniversidadAsync();
        var academico = new AcademicoService(bd.Db, Otra);

        var e = await academico.ObtenerAsync();

        Assert.Equal(UniversidadDePrueba.Reglas, e.Reglas);
        Assert.Equal(EstadoMateria.Aprobada, e.Pensum.Buscar("ESP101")!.Estado);
        Assert.Equal(EstadoMateria.Exenta, e.Pensum.Buscar("ISO100")!.Estado);        // la P es exenta en esta escala
        Assert.Equal(EstadoMateria.Disponible, e.Pensum.Buscar("SOC011")!.Estado);    // la E reprueba aquí
        // (5 × 3 + 0 × 3) / 6 horas PGA = 2.50; las 5 horas de la P no entran.
        Assert.Equal(2.50m, e.Indice.Global.Indice);
        Assert.Equal(6m, e.Indice.Global.HorasPga);
    }

    [Fact]
    public async Task SinReglasInyectadasSeUsanLasDeUnapecComoSiempre()
    {
        using var bd = await BaseDeLaOtraUniversidadAsync();

        var e = await new AcademicoService(bd.Db).ObtenerAsync();

        Assert.Equal(ReglasUniversidad.Unapec, e.Reglas);
        Assert.Equal(EstadoMateria.Disponible, e.Pensum.Buscar("ISO100")!.Estado);    // en UNAPEC la P no existe
        Assert.Equal(EstadoMateria.Exenta, e.Pensum.Buscar("SOC011")!.Estado);        // y la E es exenta
    }

    [Fact]
    public async Task LaConfiguracionInicialDelPlanificadorSaleDeLosLimitesDeLaUniversidad()
    {
        using var bd = await BaseDeLaOtraUniversidadAsync();
        var svc = new PlanificadorService(bd.Db, new AcademicoService(bd.Db, Otra));

        var config = await svc.ObtenerConfigAsync();

        Assert.Equal(18, config.LimiteBase);
        Assert.Equal(21, config.LimiteAlto);
        Assert.Equal(4.00m, config.UmbralIndice);
        Assert.Equal(new ConfigPlanificador(), await new PlanificadorService(bd.Db, new AcademicoService(bd.Db)).ObtenerConfigAsync());   // UNAPEC: 25/27/3.40
    }

    [Fact]
    public async Task LoQueLaPersonaGuardaTienePrioridadSobreLosLimitesDeLaUniversidad()
    {
        using var bd = await BaseDeLaOtraUniversidadAsync();
        var svc = new PlanificadorService(bd.Db, new AcademicoService(bd.Db, Otra));
        var mia = new ConfigPlanificador(LimiteBase: 15, LimiteAlto: 17, UmbralIndice: 3.0m, Minimo: 0, AsumirEnCurso: true);

        Assert.True((await svc.GuardarConfigAsync(mia)).Ok);

        Assert.Equal(mia, await svc.ObtenerConfigAsync());
    }

    [Theory]
    [InlineData(5.0, true)]       // el máximo de la escala de prueba
    [InlineData(5.5, false)]
    public async Task ElUmbralDelIndiceNoPuedePasarDelMaximoDeLaEscala(double umbral, bool valido)
    {
        using var bd = await BaseDeLaOtraUniversidadAsync();
        var svc = new PlanificadorService(bd.Db, new AcademicoService(bd.Db, Otra));

        var r = await svc.GuardarConfigAsync(new ConfigPlanificador(18, 21, (decimal)umbral, 0, true));

        Assert.Equal(valido, r.Ok);
        if (!valido) Assert.Contains("entre 0 y 5", r.Mensaje);
    }

    [Fact]
    public async Task ConLaEscalaDeUnapecElMaximoDelUmbralSigueSiendo4()
    {
        using var bd = new BdPrueba();
        var svc = new PlanificadorService(bd.Db, new AcademicoService(bd.Db));

        var r = await svc.GuardarConfigAsync(new ConfigPlanificador(25, 27, 4.5m, 0, true));

        Assert.False(r.Ok);
        Assert.Contains("entre 0 y 4", r.Mensaje);
    }

    [Fact]
    public async Task ElPlanificadorUsaLosPeriodosDeLaUniversidad()
    {
        using var bd = await BaseDeLaOtraUniversidadAsync();
        var svc = new PlanificadorService(bd.Db, new AcademicoService(bd.Db, Otra));

        var st = await svc.ObtenerAsync();

        Assert.NotNull(st.Contexto);
        Assert.Equal("SEM1 2026", st.Contexto!.Primero.Nombre);         // tras SEM2 2025 (en progreso) viene SEM1 del año siguiente
        Assert.Equal("SEM2 2026", st.Contexto.Primero.Siguiente().Nombre);
        Assert.Equal(new[] { "SEM1 2026", "SEM2 2026", "SEM1 2027" }, st.Columnas.Take(3).Select(c => c.Nombre));
    }

    [Fact]
    public async Task UnaMateriaSePuedeAsignarAUnPeriodoDeLaUniversidadYNoAUnoDeOtra()
    {
        using var bd = await BaseDeLaOtraUniversidadAsync();
        var svc = new PlanificadorService(bd.Db, new AcademicoService(bd.Db, Otra));
        var plan = (await svc.ObtenerAsync()).Actual!.Id;

        var bueno = await svc.AsignarAsync(plan, "ISO200", "SEM1 2026");
        var malo = await svc.AsignarAsync(plan, "ISO200", "ENE-ABR 2026");

        Assert.True(bueno.Ok, bueno.Mensaje);
        Assert.Contains("SEM1 2026", bueno.Mensaje);
        Assert.False(malo.Ok);
        Assert.Contains("Período no válido", malo.Mensaje);

        var st = await svc.ObtenerAsync(plan);
        Assert.Equal(new[] { "ISO200" }, st.Plan.Single(p => p.Periodo.Nombre == "SEM1 2026").Codigos);   // se lee de vuelta con la misma secuencia
    }

    [Fact]
    public async Task ElPlanSugeridoSeGeneraConLosLimitesYLosPeriodosDeLaUniversidad()
    {
        using var bd = await BaseDeLaOtraUniversidadAsync();
        var svc = new PlanificadorService(bd.Db, new AcademicoService(bd.Db, Otra));
        var plan = (await svc.ObtenerAsync()).Actual!.Id;

        var r = await svc.GenerarAsync(plan);
        var st = await svc.ObtenerAsync(plan);

        Assert.True(r.Ok, r.Mensaje);
        Assert.All(st.Plan, p => Assert.Matches("^SEM[12] \\d{4}$", p.Periodo.Nombre));
        Assert.NotEmpty(st.Evaluacion!.Periodos);
        Assert.All(st.Evaluacion.Periodos, p =>
        {
            Assert.Equal(18, p.Limite);                 // el índice (2.50) no supera el umbral de 4.00: límite normal de esa universidad
            Assert.False(p.SobreLimite, $"{p.Periodo}: {p.Creditos} créditos");
        });
        Assert.Contains("18", st.Config.ExplicarLimite(2.0m));                          // el texto explica el límite de esa universidad
    }
}
