using System.Net;
using HistorialAcademico.Core.Pensum;
using HistorialAcademico.Web.Data;
using HistorialAcademico.Web.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HistorialAcademico.Tests;

/// <summary>Arranca la aplicación completa con una base de datos temporal (nunca toca historial.db ni samples/).</summary>
public class AppFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "historial-test-" + Guid.NewGuid().ToString("N"));

    /// <summary>Si es true, la base arranca con el histórico sintético, el pénsum real del CSV y las equivalencias iniciales.</summary>
    protected virtual bool ConDatos => false;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(_dir);
        // Una sola base fija, sin perfiles ni PIN, y todo en una carpeta temporal (nunca la del usuario ni la del repositorio).
        builder.UseSetting("Perfiles:BaseFija", Path.Combine(_dir, "test.db"));
        builder.UseSetting("Perfiles:Carpeta", Path.Combine(_dir, "perfiles"));
        builder.UseSetting("Perfiles:BaseAnterior", Path.Combine(_dir, "no-existe.db"));
        builder.UseSetting("Perfiles:CarpetaAnterior", Path.Combine(_dir, "anterior"));
        builder.UseSetting("Banner:CarpetaDatos", _dir);
        builder.UseSetting("Banner:BaseUrl", "https://alumnos.invalid/");
        if (CarpetaPensums is not null) builder.UseSetting("Pensums:Carpeta", CarpetaPensums);
    }

    /// <summary>Una carpeta de pénsums propia (para pruebas que guardan o borran archivos); si es null se usa la del repositorio, que no se debe modificar.</summary>
    protected virtual string? CarpetaPensums => null;

    public async Task InitializeAsync()
    {
        _ = Server;   // fuerza el arranque (crea y migra la base)
        if (!ConDatos) return;

        using var scope = Services.CreateScope();
        var sync = scope.ServiceProvider.GetRequiredService<SincronizacionService>();
        var r = await sync.AplicarHtmlAsync(Muestras.LeerSintetico());
        Assert.True(r.Exito, r.Mensaje);

        var db = scope.ServiceProvider.GetRequiredService<HistorialContext>();
        db.MateriasPensum.AddRange(DatosLab.Pensum());
        db.Equivalencias.AddRange(EquivalenciasIniciales.Valores);
        await db.SaveChangesAsync();
    }

    Task IAsyncLifetime.DisposeAsync() => Task.CompletedTask;

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { /* Windows aún puede tener el archivo abierto */ }
    }

    /// <summary>HTML de la portada de una aplicación recién creada, sin histórico ni pénsum.</summary>
    public async Task<string> InicioVacioAsync()
    {
        await InitializeAsync();
        using var cliente = CreateClient();
        return await cliente.GetStringAsync("/");
    }

    public async Task<(HttpStatusCode Estado, string Html)> GetAsync(string url)
    {
        using var cliente = CreateClient();
        var r = await cliente.GetAsync(url);
        // Razor codifica las tildes (&#xE9;): se decodifica para comparar texto legible.
        return (r.StatusCode, WebUtility.HtmlDecode(await r.Content.ReadAsStringAsync()));
    }
}

public class AppConDatosFactory : AppFactory { protected override bool ConDatos => true; }

/// <summary>Recorre todas las pantallas con datos: cada una debe responder 200 y mostrar lo esperado, en español.</summary>
public class PantallasConDatosTests : IClassFixture<AppConDatosFactory>
{
    private readonly AppConDatosFactory _app;

    public PantallasConDatosTests(AppConDatosFactory app) => _app = app;

    [Theory]
    [InlineData("/")]
    [InlineData("/Estudiante/DatosPersonales")]
    [InlineData("/Estudiante/MateriasTomadas")]
    [InlineData("/Estudiante/MateriasFaltantes")]
    [InlineData("/Estudiante/IndiceAcademico")]
    [InlineData("/Pensum/Mapa")]
    [InlineData("/Pensum/QueInscribir")]
    [InlineData("/Pensum")]
    [InlineData("/Planificador")]
    [InlineData("/Horarios")]
    [InlineData("/Carrera")]
    [InlineData("/Sincronizaciones")]
    [InlineData("/Equivalencias")]
    [InlineData("/Estudiante/DetalleMateriaTomada?codigo=ISO200")]
    [InlineData("/Estudiante/DetalleMateriaFaltante?codigo=ISO800")]
    public async Task CadaPantallaResponde200ConLaBarraDeSincronizacion(string url)
    {
        var (estado, html) = await _app.GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, estado);
        Assert.Contains("Actualizar desde Banner", html);
        Assert.Contains("Última sincronización", html);
        Assert.Contains("Datos Personales", html);   // el menú del laboratorio está en todas
        Assert.Contains("Índice Académico", html);
        Assert.Contains("Planificador", html);
        Assert.DoesNotContain("Welcome", html);
        Assert.DoesNotContain("Privacy", html);
    }

    [Fact]
    public async Task InicioMuestraElNombreYLasTarjetas()
    {
        var (_, html) = await _app.GetAsync("/");
        Assert.Contains("Historial Académico – Estudiante de Prueba", html);
        Assert.Contains("Mapa del pénsum", html);
        Assert.Contains("Qué puedo inscribir", html);
        Assert.Contains("Índice acumulado", html);   // HA-E02-04: el dashboard muestra el índice acumulado
    }

    [Fact]
    public async Task InicioMuestraElProgramaPeroNoElNombreDeNingunaUniversidad()
    {
        var (_, html) = await _app.GetAsync("/");

        Assert.Contains("id=\"programa-inicio\">Administracion de Prueba</p>", html);
        Assert.DoesNotContain("UNAPEC", html);
        Assert.DoesNotContain("Universidad APEC", html);
    }

    [Fact]
    public async Task InicioSinHistoricoNoLlevaSubtituloNiNombreDeUniversidad()
    {
        using var vacia = new AppFactory();
        var html = await vacia.InicioVacioAsync();

        Assert.DoesNotContain("programa-inicio", html);
        Assert.DoesNotContain("UNAPEC", html);
        Assert.Contains("Historial Académico", html);
    }

    [Fact]
    public async Task DatosPersonalesMuestraLosDatosDelAlumno()
    {
        var (_, html) = await _app.GetAsync("/Estudiante/DatosPersonales");
        Assert.Contains("Estudiante de Prueba", html);
        Assert.Contains("5 de marzo de 2001", html);
        Assert.Contains("id=\"universidad-elegida\">—</dd>", html);   // esta app no eligió ninguna universidad: no se le inventa una
        Assert.Contains("Administracion de Prueba", html);
        Assert.Contains("En Observacion", html);
        Assert.Contains("ENE-ABR 2025", html);   // período de ingreso = primer período del histórico
    }

    [Fact]
    public async Task MateriasTomadasAgrupaPorPeriodoYEtiquetaPlanAnteriorYExentas()
    {
        var (_, html) = await _app.GetAsync("/Estudiante/MateriasTomadas");
        Assert.Contains("ENE-ABR 2025", html);
        Assert.Contains("MAY-AGO 2025", html);
        Assert.Contains("Plan anterior", html);      // MAT101 no está en el pénsum
        Assert.Contains("Exenta", html);             // ENG001 con E
        Assert.Contains("Cursos en progreso (SEP-DIC 2025)", html);
        Assert.Contains("Ver detalle", html);
        Assert.Contains("DetalleMateriaTomada?codigo=MAT101", html);
    }

    [Fact]
    public async Task DetalleDeMateriaTomada()
    {
        var (estado, html) = await _app.GetAsync("/Estudiante/DetalleMateriaTomada?codigo=MAT101");
        Assert.Equal(HttpStatusCode.OK, estado);
        Assert.Contains("MAT101", html);
        Assert.Contains("Puntos por letra", html);
        Assert.Contains("Puntos de calidad", html);
        Assert.Contains("Plan anterior", html);

        var (_, exenta) = await _app.GetAsync("/Estudiante/DetalleMateriaTomada?codigo=ENG001");
        Assert.Contains("no entra en el índice", exenta);
        Assert.Contains("Cuatrimestre", exenta);   // ENG001 sí está en el pénsum: muestra su cuatrimestre

        // El código se acepta en minúsculas.
        Assert.Equal(HttpStatusCode.OK, (await _app.GetAsync("/Estudiante/DetalleMateriaTomada?codigo=mat101")).Estado);
    }

    [Fact]
    public async Task MateriasFaltantesMuestraElResumenYSoloLoPendiente()
    {
        var (_, html) = await _app.GetAsync("/Estudiante/MateriasFaltantes");
        Assert.Contains("Avance de la carrera", html);
        Assert.Contains("Créditos faltantes", html);
        Assert.Contains("Cuatrimestre 12", html);                       // TFG
        Assert.Contains("aprobar un deporte", html);                    // requisitos de graduación
        Assert.Contains("DetalleMateriaFaltante?codigo=ISO100", html);  // ISO100 no se ha tomado
        Assert.DoesNotContain("DetalleMateriaFaltante?codigo=ISO200", html);   // ISO200 (B) ya está aprobada
        Assert.DoesNotContain("DetalleMateriaFaltante?codigo=ISO400", html);   // ISO400 está en curso
    }

    [Fact]
    public async Task DetalleDeMateriaFaltante()
    {
        var (estado, html) = await _app.GetAsync("/Estudiante/DetalleMateriaFaltante?codigo=ISO800");
        Assert.Equal(HttpStatusCode.OK, estado);
        Assert.Contains("Gestión de Calidad de Software", html);
        Assert.Contains("ISO735", html);
        Assert.Contains("Pendiente", html);

        var (_, electiva) = await _app.GetAsync("/Estudiante/DetalleMateriaFaltante?codigo=E077");
        Assert.Contains("ADM103", electiva);
        Assert.Contains("Fundamentos de Administración", electiva);
        Assert.Contains("Dirección de Proyectos", electiva);
    }

    [Fact]
    public async Task IndiceAcademicoMuestraElIndiceGlobalConDosDecimales()
    {
        var (_, html) = await _app.GetAsync("/Estudiante/IndiceAcademico");
        Assert.Contains("Índice global", html);
        Assert.Contains("2.31", html);
        Assert.Contains("3.43", html);   // PGA del primer período
        Assert.Contains("1.00", html);   // PGA del segundo período
        Assert.Contains("exentas (E)", html);
        Assert.Contains("coincide con los totales que publica Banner", html);
    }

    [Fact]
    public async Task MapaDelPensumTiene12ColumnasY75TarjetasColoreadasPorEstado()
    {
        var (_, html) = await _app.GetAsync("/Pensum/Mapa");
        Assert.Equal(12, System.Text.RegularExpressions.Regex.Matches(html, "class=\"mapa-columna\"").Count);
        Assert.Equal(75, System.Text.RegularExpressions.Regex.Matches(html, "class=\"mapa-card ").Count);
        Assert.Contains("estado-exenta", html);      // ENG001
        Assert.Contains("estado-aprobada", html);    // ISO200
        Assert.Contains("estado-encurso", html);     // ISO400
        Assert.Contains("estado-disponible", html);
        Assert.Contains("estado-bloqueada", html);
        Assert.Contains("id=\"modalMateria\"", html);
        Assert.Contains("data-codigo=\"ISO200\"", html);
    }

    [Fact]
    public async Task QueInscribirListaSoloLasDisponiblesOrdenadasPorCuatrimestre()
    {
        var (_, html) = await _app.GetAsync("/Pensum/QueInscribir");
        Assert.Contains("DetalleMateriaFaltante?codigo=ISO100", html);        // disponible
        Assert.DoesNotContain("DetalleMateriaFaltante?codigo=ISO200", html);  // aprobada
        Assert.DoesNotContain("DetalleMateriaFaltante?codigo=ISO400", html);  // en curso
        Assert.DoesNotContain("DetalleMateriaFaltante?codigo=ISO800", html);  // bloqueada

        var cuatrimestres = System.Text.RegularExpressions.Regex
            .Matches(html, @"text-center"">(\d+)</td>").Select(m => int.Parse(m.Groups[1].Value)).ToList();
        Assert.NotEmpty(cuatrimestres);
        Assert.Equal(cuatrimestres.OrderBy(c => c), cuatrimestres);
    }

    [Fact]
    public async Task SincronizacionesMuestraElHistorialDeIntentos()
    {
        var (_, html) = await _app.GetAsync("/Sincronizaciones");
        Assert.Contains("Éxito", html);
        Assert.Contains("Sincronizado: 2 períodos", html);
        Assert.Contains("no hay tareas en segundo plano", html);
    }

    [Fact]
    public async Task EquivalenciasMuestraLasCargadas()
    {
        var (_, html) = await _app.GetAsync("/Equivalencias");
        Assert.Contains("ING701", html);
        Assert.Contains("ESP102", html);
        Assert.Contains("MAT126", html);
    }

    [Theory]
    [InlineData("/Estudiante/DetalleMateriaTomada?codigo=NOEXISTE")]
    [InlineData("/Estudiante/DetalleMateriaTomada")]
    [InlineData("/Estudiante/DetalleMateriaFaltante?codigo=NOEXISTE")]
    [InlineData("/Estudiante/Nada")]
    public async Task UnCodigoOUnaRutaQueNoExistenDan404(string url) =>
        Assert.Equal(HttpStatusCode.NotFound, (await _app.GetAsync(url)).Estado);

    [Fact]
    public async Task ElBotonActualizarExigeElTokenAntifalsificacion()
    {
        using var cliente = _app.CreateClient();
        var r = await cliente.PostAsync("/Sincronizaciones/Actualizar", new FormUrlEncodedContent(new Dictionary<string, string>()));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);   // sin token no se abre Banner ni se sincroniza nada
    }
}

/// <summary>Sin datos ni pénsum: las pantallas no deben fallar y deben explicar qué hacer.</summary>
public class PantallasVaciasTests : IClassFixture<AppFactory>
{
    private readonly AppFactory _app;

    public PantallasVaciasTests(AppFactory app) => _app = app;

    [Theory]
    [InlineData("/", "Todavía no hay datos")]
    [InlineData("/Estudiante/DatosPersonales", "Aún no hay datos")]
    [InlineData("/Estudiante/MateriasTomadas", "Aún no hay histórico")]
    [InlineData("/Estudiante/MateriasFaltantes", "Falta cargar el pénsum")]
    [InlineData("/Estudiante/IndiceAcademico", "Aún no hay histórico")]
    [InlineData("/Pensum/Mapa", "Falta cargar el pénsum")]
    [InlineData("/Pensum/QueInscribir", "Falta cargar el pénsum")]
    [InlineData("/Pensum", "Todavía no hay un pénsum cargado")]
    [InlineData("/Sincronizaciones", "Todavía no hay sincronizaciones")]
    [InlineData("/Equivalencias", "No hay equivalencias registradas")]
    public async Task ExplicanQueHacerCuandoNoHayDatos(string url, string texto)
    {
        var (estado, html) = await _app.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, estado);
        Assert.Contains(texto, html);
        Assert.Contains("Aún no se ha sincronizado con Banner", html);
    }
}
