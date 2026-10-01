using System.Text.RegularExpressions;
using HistorialAcademico.Banner;
using HistorialAcademico.Core.Entities;
using HistorialAcademico.Web.Services;
using HistorialAcademico.Core.Horarios;
using HistorialAcademico.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Xunit;

namespace HistorialAcademico.Tests;

/// <summary>Un texto que no alcanza el contraste mínimo (clase con propiedades para que Playwright pueda deserializarla).</summary>
public class FalloContraste
{
    public string Texto { get; set; } = "";
    public string Clase { get; set; } = "";
    public string Primero { get; set; } = "";
    public string Fondo { get; set; } = "";
    public double Ratio { get; set; }
}

/// <summary>
/// Interruptor de tema y auditoría de contraste sobre las páginas reales de la aplicación (HTML, CSS y JS reales)
/// abiertas en un Chromium real, en el tema claro y en el oscuro.
/// </summary>
[Collection(ColeccionConsultas.Nombre)]   // usa el candado de consultas a Banner (falso) para mostrar la consulta en curso
public class TemaOscuroNavegadorTests : IClassFixture<AppConBannerFalsoFactory>, IAsyncLifetime
{
    private static readonly string[] Rutas =
    {
        "/", "/Estudiante/DatosPersonales", "/Estudiante/MateriasTomadas", "/Estudiante/DetalleMateriaTomada?codigo=ISO200",
        "/Estudiante/MateriasFaltantes", "/Estudiante/DetalleMateriaFaltante?codigo=ISO800", "/Estudiante/DetalleMateriaFaltante?codigo=E077",
        "/Estudiante/IndiceAcademico", "/Pensum/Mapa", "/Pensum/QueInscribir", "/Pensum", "/Equivalencias", "/Sincronizaciones",
        "/Planificador", "/Planificador?estado=vacio", "/Planificador?estado=avisos", "/Planificador/Prioridades", "/Planificador/Comparar", "/Planificador/Comparar?estado=activo",
        // Horarios de Banner en sus cuatro estados: sin elegir, sin consultar, sin secciones publicadas y con secciones.
        "/Horarios", "/Horarios?materia=ISO800&periodo=202610", "/Horarios?materia=ISO800&periodo=202620", "/Horarios?materia=ISO800&periodo=202630",
        // La consulta de todas las materias disponibles: avance en curso y resumen con una materia con error.
        "/Horarios?estado=en-curso", "/Horarios?estado=resumen",
        // Horario tentativo (grilla con choque, atenuadas, horas no disponibles), la cuadrícula de horas y la solicitud de apertura.
        "/Horarios/Tentativo", "/Horarios/NoDisponible", "/Horarios/Apertura", "/Horarios?materia=ISO725&periodo=202620",
        // Carrera y pénsum: la lista, sin resultados, en uso y la guía.
        "/Carrera", "/Carrera?q=derecho", "/Carrera?estado=en-uso", "/Carrera/Guia", "/Equivalencias?estado=del-pensum",
        "/Carrera/Importar", "/Carrera?estado=revision-valida", "/Carrera?estado=revision-problemas",
        // Mis datos y privacidad: con base fija, y con un perfil (sesión de Banner guardada) sin y con un intento de borrado fallido.
        "/MisDatos", "/MisDatos?estado=perfil", "/MisDatos?estado=error",
        // Materias a mano (modo sin Banner): la pantalla, con filas de las cuatro clases y con los problemas de un CSV.
        "/MateriasManuales", "/MateriasManuales?estado=con-filas", "/MateriasManuales?estado=errores",
        // Perfiles: crear el primero (limpio y con errores), elegir entre varios (PIN incorrecto y bloqueo) y el indicador en la barra.
        "/Perfiles/Crear", "/Perfiles/Crear?estado=errores", "/Perfiles?estado=lista", "/?estado=perfil",
        // El asistente de primer uso: bienvenida, perfil, carrera (sin elegir y elegida) y Banner (sin intentar y con un error).
        "/Asistente/Bienvenida", "/Asistente/Perfil?estado=asistente", "/Asistente/Carrera?estado=asistente", "/Asistente/Carrera?estado=elegida",
        "/Asistente/Banner?estado=asistente", "/Asistente/Banner?estado=error",
    };

    private readonly AppConBannerFalsoFactory _app;
    private ServidorEstatico _servidor = null!;
    private IPlaywright _pw = null!;
    private IBrowser _navegador = null!;
    // xUnit crea esta clase para CADA prueba, pero la aplicación (el fixture) es una sola: las páginas se capturan una vez y se reutilizan.
    // (Capturarlas en cada prueba multiplicaba por más de cien un trabajo que deja la aplicación en el mismo estado.)
    private static readonly Dictionary<string, string> _paginas = new();
    private static readonly SemaphoreSlim _candadoCaptura = new(1, 1);
    private static bool _capturado;

    public TemaOscuroNavegadorTests(AppConBannerFalsoFactory app) => _app = app;

    private static string Nombre(string ruta) => "/p/" + Regex.Replace(ruta.Trim('/'), "[^A-Za-z0-9]+", "-").Trim('-') + ".html";

    public async Task InitializeAsync()
    {
        await _candadoCaptura.WaitAsync();
        try
        {
            if (!_capturado)
            {
                await CapturarPaginasAsync();
                _capturado = true;
            }
        }
        finally { _candadoCaptura.Release(); }

        _servidor = new ServidorEstatico(_paginas);
        _pw = await Playwright.CreateAsync();
        _navegador = await _pw.Chromium.LaunchAsync(new() { Headless = true });
    }

    private async Task CapturarPaginasAsync()
    {
        using var cliente = _app.CreateClient();

        // Se genera un plan para que el planificador tenga tarjetas, columnas y créditos que auditar.
        var html = await cliente.GetStringAsync("/Planificador");
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        await cliente.PostAsync("/Planificador/Generar", new FormUrlEncodedContent(new Dictionary<string, string> { ["id"] = "1", ["__RequestVerificationToken"] = token }));
        await cliente.PostAsync("/Sincronizaciones/Actualizar", new FormUrlEncodedContent(new Dictionary<string, string>()));   // 400: sin token, no abre Banner

        await SembrarHorariosAsync();

        foreach (var ruta in Rutas.Where(r => !r.Contains("estado="))) _paginas[Nombre(ruta)] = await cliente.GetStringAsync(ruta);
        await CapturarConsultaMasivaAsync(cliente);

        // «Carrera y pénsum» con un pénsum en uso (la fila destacada y la insignia «En uso»).
        var carrera = await cliente.GetStringAsync("/Carrera");
        var tokenCarrera = Regex.Match(carrera, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        await cliente.PostAsync("/Carrera/Activar", new FormUrlEncodedContent(new Dictionary<string, string> { ["clave"] = "unapec/ingenieria-software-11.json", ["__RequestVerificationToken"] = tokenCarrera }));
        _paginas[Nombre("/Carrera?estado=en-uso")] = await cliente.GetStringAsync("/Carrera");
        _paginas[Nombre("/Equivalencias?estado=del-pensum")] = await cliente.GetStringAsync("/Equivalencias");   // con las equivalencias que declara el pénsum

        // Importar un pénsum: el formulario, la vista previa válida y la vista previa con problemas (nada de esto guarda archivos).
        _paginas[Nombre("/Carrera/Importar")] = await cliente.GetStringAsync("/Carrera/Importar");
        var tokenImportar = Regex.Match(_paginas[Nombre("/Carrera/Importar")], "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        async Task<string> Enviar(string url, Dictionary<string, string> campos)
        {
            campos["__RequestVerificationToken"] = tokenImportar;
            return await (await cliente.PostAsync(url, new FormUrlEncodedContent(campos))).Content.ReadAsStringAsync();
        }
        _paginas[Nombre("/Carrera?estado=revision-valida")] = await Enviar("/Carrera/Convertir", new()
        {
            ["texto"] = "Código\tAsignatura\tCréditos\tPrerrequisitos\nCuatrimestre 1\nAAA100\tIntroducción\t3\t\nCuatrimestre 2\nBBB200\tFundamentos\t5\tAAA100\nTotal 8",
            ["nombreCarrera"] = "Derecho", ["version"] = "2022", ["universidad"] = "unapec",
        });
        _paginas[Nombre("/Carrera?estado=revision-problemas")] = await Enviar("/Carrera/Revisar", new()
        {
            ["nombreCarrera"] = "Derecho", ["version"] = "2022", ["universidad"] = "unapec", ["accion"] = "revisar", ["usar"] = "true",
            ["filas[0].Codigo"] = "AAA100", ["filas[0].Nombre"] = "Introducción", ["filas[0].Creditos"] = "tres", ["filas[0].Cuatrimestre"] = "1",
            ["filas[1].Codigo"] = "BBB200", ["filas[1].Nombre"] = "Fundamentos", ["filas[1].Creditos"] = "5", ["filas[1].Cuatrimestre"] = "2", ["filas[1].Prerrequisitos"] = "ZZZ999",
            ["filas[1].Quitar"] = "false",
        });
        using var vacia = new AppFactory();
        _paginas["/vacia.html"] = await vacia.InicioVacioAsync();

        await CapturarTableroVacioAsync(cliente);
        await CapturarMateriasManualesAsync(cliente);
        await CapturarPerfilesAsync();
    }

    /// <summary>
    /// El tablero con la bandeja «Por planificar» llena, con sus filtros y botones, y con materias que pueden ir a unas columnas pero
    /// no a otras. (El escenario que se genera para las demás páginas ya lo tiene todo planificado y la bandeja queda vacía.) Se hace una
    /// copia de ese escenario y se le sacan las materias del primer período: quedan en la bandeja, con sus dependientes todavía en el plan.
    /// La copia se borra al terminar.
    /// </summary>
    private async Task CapturarTableroVacioAsync(HttpClient cliente)
    {
        static string Token(string html) => Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        async Task<HttpResponseMessage> Enviar(string url, Dictionary<string, string> campos)
        {
            campos["__RequestVerificationToken"] = Token(await cliente.GetStringAsync("/Planificador"));
            return await cliente.PostAsync(url, new FormUrlEncodedContent(campos));
        }

        var copia = await (await Enviar("/Planificador/GuardarComo", new() { ["id"] = "1", ["nombre"] = "Tablero de auditoría" })).Content.ReadAsStringAsync();
        var id = Regex.Match(copia, "action=\"/Planificador/Eliminar\"[\\s\\S]*?name=\"id\" value=\"(\\d+)\"").Groups[1].Value;
        if (id.Length == 0) return;   // no se pudo crear (ya existía): sin esta captura las pruebas del tablero fallan a propósito
        try
        {
            string[] codigos;
            using (var scope = _app.Services.CreateScope())
                codigos = (await scope.ServiceProvider.GetRequiredService<PlanificadorService>().ObtenerAsync(int.Parse(id))).Plan.First(p => p.Codigos.Count > 0).Codigos.ToArray();
            foreach (var codigo in codigos) await Enviar("/Planificador/Asignar", new() { ["id"] = id, ["codigo"] = codigo, ["periodo"] = "" });
            _paginas[Nombre("/Planificador?estado=vacio")] = await cliente.GetStringAsync($"/Planificador?id={id}");
            // Con dos escenarios, uno de ellos marcado como plan activo: la comparación completa (promedio, período más pesado, insignia y botones).
            _paginas[Nombre("/Planificador/Comparar?estado=activo")] = await (await Enviar("/Planificador/Activar", new() { ["id"] = id, ["desde"] = "comparar" })).Content.ReadAsStringAsync();
        }
        finally { await Enviar("/Planificador/Eliminar", new() { ["id"] = id }); }
    }

    /// <summary>
    /// «Materias a mano» con las cuatro clases de fila (cuenta, en curso, «Banner ya trae este período» y «No está en tu pénsum actual»)
    /// y con los problemas de un CSV que no se pudo importar. Va al final: agrega materias manuales a la base de esta prueba.
    /// </summary>
    private async Task CapturarMateriasManualesAsync(HttpClient cliente)
    {
        static string Token(string html) => Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        async Task Agregar(string codigo, string periodo, string nota) =>
            await cliente.PostAsync("/MateriasManuales/Agregar", new FormUrlEncodedContent(new Dictionary<string, string>
                { ["codigo"] = codigo, ["periodo"] = periodo, ["calificacion"] = nota, ["__RequestVerificationToken"] = Token(await cliente.GetStringAsync("/MateriasManuales")) }));

        // El fixture se comparte entre todas las pruebas de la clase y esto corre antes de cada una: se parte de cero y se deja como estaba.
        async Task Limpiar()
        {
            using var scope = _app.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<HistorialContext>().MateriasManuales.ExecuteDeleteAsync();
        }

        string libre, otra, periodoDeBanner;
        await Limpiar();
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HistorialContext>();
            var usados = (await db.MateriasCursadas.Select(m => m.Codigo).ToListAsync()).Concat(await db.CursosEnProgreso.Select(c => c.Codigo).ToListAsync()).ToHashSet();
            var libres = (await db.MateriasPensum.OrderBy(m => m.Cuatrimestre).ThenBy(m => m.Codigo).ToListAsync()).Where(m => !usados.Contains(m.Codigo)).ToList();
            (libre, otra, periodoDeBanner) = (libres[0].Codigo, libres[1].Codigo, (await db.Periodos.OrderBy(p => p.Orden).FirstAsync()).Nombre);
        }
        try
        {
            await Agregar(libre, "ENE-ABR 2000", "A");
            await Agregar(otra, "SEP-DIC 2099", "");
            await Agregar(libre, periodoDeBanner, "B");
            using (var scope = _app.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<HistorialContext>();
                db.MateriasManuales.Add(new HistorialAcademico.Core.Entities.MateriaManual { Codigo = "OLD100", Periodo = "MAY-AGO 2001", Calificacion = "A", Creada = DateTime.UtcNow });
                await db.SaveChangesAsync();
            }
            _paginas[Nombre("/MateriasManuales?estado=con-filas")] = await cliente.GetStringAsync("/MateriasManuales");
            // Con materias «aprobadas» a mano que el plan tenía por cursar, el planificador cuenta qué cambió («Tu plan cambió…» con su botón Entendido).
            _paginas[Nombre("/Planificador?estado=avisos")] = await cliente.GetStringAsync("/Planificador");

            var form = new MultipartFormDataContent { { new StringContent(Token(await cliente.GetStringAsync("/MateriasManuales"))), "__RequestVerificationToken" } };
            form.Add(new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes($"codigo,periodo,calificacion\n{libre},ENE-ABR 2000,A\nZZZ999,ENE-ABR 2000,B\n{otra},cuando sea,A\n{otra},ENE-ABR 2000,Q\n")), "archivo", "materias.csv");
            _paginas[Nombre("/MateriasManuales?estado=errores")] = await (await cliente.PostAsync("/MateriasManuales/Importar", form)).Content.ReadAsStringAsync();
        }
        finally { await Limpiar(); }
    }

    /// <summary>
    /// Las pantallas de perfiles, con una aplicación que sí usa perfiles: crear el primero (con el aviso de datos anteriores y con
    /// errores), elegir entre varios (uno con PIN incorrecto y otro bloqueado) y la portada con el indicador de perfil en la barra.
    /// </summary>
    private async Task CapturarPerfilesAsync()
    {
        using var app = new AppConPerfilesYBannerFalsoFactory();
        using var c = app.Cliente();
        static string Token(string html) => Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        async Task<string> Enviar(string pagina, string url, Dictionary<string, string> campos)
        {
            campos["__RequestVerificationToken"] = Token(await c.GetStringAsync(pagina));
            return await (await c.PostAsync(url, new FormUrlEncodedContent(campos))).Content.ReadAsStringAsync();
        }
        Task<string> Crear(Dictionary<string, string> campos) => Enviar("/Perfiles/Crear", "/Perfiles/Crear", campos);
        static Dictionary<string, string> Datos(string nombre, string? pin) => new()
        {
            ["Nombre"] = nombre, ["SinPin"] = pin is null ? "true" : "false", ["Pin"] = pin ?? "", ["ConfirmarPin"] = pin ?? "", ["TraerAnteriores"] = "false",
        };

        // Crear el primero, con datos de una versión anterior por traer y con errores en el formulario.
        Directory.CreateDirectory(app.CarpetaAnterior);
        await File.WriteAllTextAsync(app.BaseAnterior, "base anterior");
        _paginas[Nombre("/Perfiles/Crear")] = await c.GetStringAsync("/Perfiles/Crear");
        _paginas[Nombre("/Perfiles/Crear?estado=errores")] = await Crear(new() { ["Nombre"] = " ", ["SinPin"] = "false", ["Pin"] = "12", ["ConfirmarPin"] = "13", ["TraerAnteriores"] = "true" });
        File.Delete(app.BaseAnterior);

        // Tres perfiles: Ana (PIN), Beto (sin PIN) y Carla (PIN); se falla el PIN de Ana y se bloquea a Carla.
        await Crear(Datos("Ana", "clave-1234"));
        await Crear(Datos("Beto", null));
        await Crear(Datos("Carla", "clave-5678"));
        var perfiles = app.Gestor.Almacen.Listar();
        var ana = perfiles.Single(p => p.Nombre == "Ana").Id;
        var carla = perfiles.Single(p => p.Nombre == "Carla").Id;
        for (var i = 0; i < 5; i++) app.Gestor.Almacen.Intentar(carla, "mal");
        _paginas[Nombre("/Perfiles?estado=lista")] = await Enviar("/Perfiles", "/Perfiles/Entrar", new() { ["id"] = ana, ["pin"] = "mal" });

        // Dentro del perfil con PIN, el asistente: cada paso, con la carrera elegida y con un intento de conectar Banner que falla.
        await Enviar("/Perfiles", "/Perfiles/Entrar", new() { ["id"] = ana, ["pin"] = "clave-1234" });
        _paginas[Nombre("/Asistente/Perfil?estado=asistente")] = await c.GetStringAsync("/Asistente/Perfil");
        _paginas[Nombre("/Asistente/Carrera?estado=asistente")] = await c.GetStringAsync("/Asistente/Carrera");
        await Enviar("/Asistente/Carrera", "/Asistente/Carrera", new() { ["clave"] = "unapec/ingenieria-software-11.json" });
        _paginas[Nombre("/Asistente/Carrera?estado=elegida")] = await c.GetStringAsync("/Asistente/Carrera");
        _paginas[Nombre("/Asistente/Banner?estado=asistente")] = await c.GetStringAsync("/Asistente/Banner");
        app.Falso.Captura = () => throw new BannerException("Banner no respondió a tiempo. Vuelve a intentarlo.");
        await Enviar("/Asistente/Banner", "/Asistente/Conectar", new());
        _paginas[Nombre("/Asistente/Banner?estado=error")] = await c.GetStringAsync("/Asistente/Banner");

        // Al terminar, la portada muestra el indicador del perfil, el botón «Bloquear» y el aviso de que todo quedó listo.
        await Enviar("/Asistente/Banner", "/Asistente/Terminar", new());
        _paginas[Nombre("/?estado=perfil")] = await c.GetStringAsync("/");

        // «Mis datos»: con sesión y capturas de Banner guardadas, y con un intento de borrar sin la confirmación escrita.
        var carpeta = app.Gestor.Almacen.CarpetaDe(ana);
        Directory.CreateDirectory(Path.Combine(carpeta, ".auth"));
        await File.WriteAllTextAsync(Path.Combine(carpeta, ".auth", "banner.json"), "{}");
        Directory.CreateDirectory(Path.Combine(carpeta, "samples"));
        await File.WriteAllTextAsync(Path.Combine(carpeta, "samples", "historico.html"), "<html></html>");
        _paginas[Nombre("/MisDatos?estado=perfil")] = await c.GetStringAsync("/MisDatos");
        _paginas[Nombre("/MisDatos?estado=error")] = await Enviar("/MisDatos", "/MisDatos/BorrarTodo", new() { ["confirmacion"] = "otro", ["pin"] = "" });
    }

    /// <summary>
    /// Con un Banner falso, deja la consulta de todas las materias «en curso» (la barra de avance) y luego «terminada con un
    /// error» (el resumen), y guarda el HTML de cada estado para auditarlo. Nada de esto toca Banner de verdad.
    /// </summary>
    private async Task CapturarConsultaMasivaAsync(HttpClient cliente)
    {
        var masiva = _app.Services.GetRequiredService<ConsultaMasivaService>();
        await masiva.Tarea;
        var entro = new TaskCompletionSource();
        var seguir = new TaskCompletionSource();
        _app.Banner.Secciones = async (p, c) =>
        {
            entro.TrySetResult();
            await seguir.Task;
            // Un reparto fijo según el código: unas materias con secciones, otras sin secciones y otras con error.
            switch (c[0].Codigo.Sum(ch => ch) % 3)
            {
                case 0: throw new BannerException($"Banner respondió HTTP 500 al consultar {c[0].Codigo}.");
                case 1: return new ResultadoBusqueda();
                default:
                    return new ResultadoBusqueda { Total = 1, Secciones = new() { new SeccionBanner { Periodo = p, Nrc = "9" + c[0].Codigo, Codigo = c[0].Codigo, Seccion = "1", CupoMaximo = 30, CuposDisponibles = 5, Abierta = true } } };
            }
        };

        var html = await cliente.GetStringAsync("/Horarios");
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        await cliente.PostAsync("/Horarios/ConsultarTodas", new FormUrlEncodedContent(new Dictionary<string, string> { ["periodo"] = "202610", ["__RequestVerificationToken"] = token }));
        await entro.Task;
        _paginas[Nombre("/Horarios?estado=en-curso")] = await cliente.GetStringAsync("/Horarios?periodo=202610");
        seguir.SetResult();
        await masiva.Tarea;
        _paginas[Nombre("/Horarios?estado=resumen")] = await cliente.GetStringAsync("/Horarios?periodo=202610");
    }

    /// <summary>
    /// Secciones de ISO800 (inventadas) para que la pantalla de horarios tenga tabla, insignias y estado vacío que auditar.
    /// InitializeAsync corre por cada prueba y la base de la fábrica es compartida, así que solo siembra la primera vez.
    /// </summary>
    private async Task SembrarHorariosAsync()
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HistorialContext>();
        if (await db.ConsultasSecciones.AnyAsync()) return;

        db.ConsultasSecciones.AddRange(
            new ConsultaSecciones { Periodo = "202620", Codigo = "ISO800", Secciones = 0, Fecha = DateTime.UtcNow },
            new ConsultaSecciones { Periodo = "202630", Codigo = "ISO800", Secciones = 3, Fecha = DateTime.UtcNow });
        var bloques = System.Text.Json.JsonSerializer.Serialize(new[]
        {
            new BloqueBanner { Dias = DiasSemana.Martes | DiasSemana.Jueves, Inicio = new TimeOnly(8, 0), Fin = new TimeOnly(10, 0), Edificio = "EDIF-03", Aula = "12" },
            new BloqueBanner { Dias = DiasSemana.Ninguno, Inicio = new TimeOnly(18, 0), Fin = new TimeOnly(20, 0) },
        });
        SeccionOfertada Nueva(string nrc, string seccion, string profesor, int cupo, int inscritos, int libres, bool abierta, string titulo, string json) => new()
        {
            Periodo = "202630", Nrc = nrc, Codigo = "ISO800", Titulo = titulo, Seccion = seccion, Creditos = 3, Campus = "CAMPUS - PRUEBA", Metodo = "TEORIA",
            Profesor = profesor, CupoMaximo = cupo, Inscritos = inscritos, CuposDisponibles = libres, Abierta = abierta, BloquesJson = json, Consultada = DateTime.UtcNow,
        };
        db.SeccionesOfertadas.AddRange(
            Nueva("1", "1", "Pedro Ejemplo Prueba Uno", 30, 10, 20, true, "PRUEBA DE AUDITORIA", bloques),
            Nueva("2", "2", "", 30, 30, 0, false, "PRUEBA DE AUDITORIA", "[]"),
            Nueva("3", "TU1", "", 0, 0, 0, false, "CURSO ESPECIAL", "[]"));

        // Horario tentativo con un choque (ISO800-1 martes/jueves 8-10 y ISO625-1 martes/jueves 9-11), horas no disponibles, una
        // opción atenuada (ISO625-2, lunes 9-11), profesores previos y una materia marcada para solicitar apertura.
        SeccionOfertada Clase(string nrc, string codigo, string seccion, DiasSemana dias, string inicio, string fin) => new()
        {
            Periodo = "202630", Nrc = nrc, Codigo = codigo, Titulo = "PRUEBA DE AUDITORIA", Seccion = seccion, Creditos = 3, Campus = "CAMPUS - PRUEBA", Metodo = "TEORIA",
            Profesor = "Marta Ejemplo Prueba Dos", CupoMaximo = 30, Inscritos = 10, CuposDisponibles = 20, Abierta = true, Consultada = DateTime.UtcNow,
            BloquesJson = System.Text.Json.JsonSerializer.Serialize(new[] { new BloqueBanner { Dias = dias, Inicio = TimeOnly.Parse(inicio), Fin = TimeOnly.Parse(fin), Edificio = "EDIF-01", Aula = "4" } }),
        };
        db.SeccionesOfertadas.AddRange(
            Clase("4", "ISO625", "1", DiasSemana.Martes | DiasSemana.Jueves, "09:00", "11:00"),
            Clase("5", "ISO625", "2", DiasSemana.Lunes, "09:00", "11:00"));
        db.ConsultasSecciones.Add(new ConsultaSecciones { Periodo = "202630", Codigo = "ISO625", Secciones = 2, Fecha = DateTime.UtcNow });
        db.ConsultasSecciones.Add(new ConsultaSecciones { Periodo = "202620", Codigo = "ISO725", Secciones = 0, Fecha = DateTime.UtcNow });
        db.HorariosTentativos.Add(new HorarioTentativo
        {
            Nombre = "Horario de auditoría", Periodo = "202630", Creado = DateTime.UtcNow, Actualizado = DateTime.UtcNow,
            Secciones = { new SeccionElegida { Nrc = "1", Codigo = "ISO800", Etiqueta = "ISO800-1" }, new SeccionElegida { Nrc = "4", Codigo = "ISO625", Etiqueta = "ISO625-1" } },
        });
        db.BloquesNoDisponibles.AddRange(
            new BloqueNoDisponible { Dia = DiasSemana.Lunes, DesdeMin = 8 * 60, HastaMin = 17 * 60 },
            new BloqueNoDisponible { Dia = DiasSemana.Miercoles, DesdeMin = 18 * 60, HastaMin = 21 * 60 });
        db.AperturasSolicitadas.Add(new AperturaSolicitada { Codigo = "ISO725", Nota = "La necesito para graduarme", Marcada = DateTime.UtcNow });
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _navegador.DisposeAsync();
        _pw.Dispose();
        _servidor.Dispose();
    }

    private async Task<IPage> AbrirAsync(string ruta, ColorScheme esquema, string? antes = null, ReducedMotion movimiento = ReducedMotion.Reduce)
    {
        var contexto = await _navegador.NewContextAsync(new() { ColorScheme = esquema, ReducedMotion = movimiento, ViewportSize = new() { Width = 1400, Height = 1000 } });
        var pagina = await contexto.NewPageAsync();
        if (antes is not null) await pagina.AddInitScriptAsync(antes);
        await pagina.GotoAsync(_servidor.Direccion(ruta.StartsWith("/p/") || ruta == "/vacia.html" ? ruta : Nombre(ruta)));
        return pagina;
    }

    private record CeldaMedida(string Etiqueta, string Clase, string Dia, double Arriba, double Alto, double Izquierda, double Ancho, double AnchoDia);
    private record GrillaMedida(double AltoHora, List<CeldaMedida> Celdas);

    [Theory]
    [InlineData(ColorScheme.Light)]
    [InlineData(ColorScheme.Dark)]
    public async Task LaGrillaDelHorarioPoneCadaClaseEnSuDiaYSuHoraYRepartePorCarriles(ColorScheme esquema)
    {
        var p = await AbrirAsync("/Horarios/Tentativo", esquema);
        var json = await p.EvaluateAsync<string>("""
            () => {
              const g = document.getElementById('grilla-horario');
              const alto = g.querySelector('.horario-hora').getBoundingClientRect().height;
              const celdas = [];
              g.querySelectorAll('.horario-dia').forEach(dia => {
                const titulo = dia.querySelector('.horario-dia-titulo').textContent.trim();
                const cuerpo = dia.querySelector('.horario-cuerpo').getBoundingClientRect();
                dia.querySelectorAll('.horario-celda').forEach(c => {
                  const r = c.getBoundingClientRect();
                  celdas.push({ etiqueta: c.querySelector('.horario-celda-titulo').textContent.trim(), clase: c.className, dia: titulo,
                                arriba: r.top - cuerpo.top, alto: r.height, izquierda: r.left - cuerpo.left, ancho: r.width, anchoDia: cuerpo.width });
                });
              });
              return JSON.stringify({ altoHora: alto, celdas });
            }
            """);
        var g = System.Text.Json.JsonSerializer.Deserialize<GrillaMedida>(json, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var h = g.AltoHora;
        Assert.True(h > 20, $"la altura de una hora es {h}px");

        CeldaMedida Una(string etiqueta, string dia) => Assert.Single(g.Celdas, c => c.Etiqueta == etiqueta && c.Dia == dia);

        // La grilla arranca a las 7:00: una clase de 8:00 a 10:00 empieza a 1 hora del borde y mide 2 horas.
        var iso800 = Una("ISO800-1", "Mar");
        Assert.Equal(1 * h, iso800.Arriba, 1.5);
        Assert.Equal(2 * h, iso800.Alto, 1.5);
        Assert.Equal(1 * h, Una("ISO800-1", "Jue").Arriba, 1.5);                    // martes y jueves
        Assert.DoesNotContain(g.Celdas, c => c.Etiqueta == "ISO800-1" && c.Dia is not ("Mar" or "Jue"));

        // ISO625-1 (9:00 a 11:00) se cruza con la anterior de 9 a 10: comparten el ancho del día, cada una en su carril.
        var iso625 = Una("ISO625-1", "Mar");
        Assert.Equal(2 * h, iso625.Arriba, 1.5);
        Assert.Equal(2 * h, iso625.Alto, 1.5);
        Assert.Equal(iso800.AnchoDia / 2, iso800.Ancho, 1.5);
        Assert.Equal(iso625.AnchoDia / 2, iso625.Ancho, 1.5);
        Assert.Equal(0, iso800.Izquierda, 1.5);
        Assert.Equal(iso625.AnchoDia / 2, iso625.Izquierda, 1.5);
        Assert.Contains("horario-choque", iso800.Clase);
        Assert.Contains("horario-choque", iso625.Clase);

        // Las horas no disponibles van de fondo, a todo el ancho del día: lunes de 8:00 a 5:00 p. m.
        var lunes = Assert.Single(g.Celdas, c => c.Clase.Contains("horario-nodisp") && c.Dia == "Lun");
        Assert.Equal(1 * h, lunes.Arriba, 1.5);
        Assert.Equal(9 * h, lunes.Alto, 1.5);
        Assert.Equal(lunes.AnchoDia, lunes.Ancho, 1.5);
        Assert.Equal(11 * h, Assert.Single(g.Celdas, c => c.Clase.Contains("horario-nodisp") && c.Dia == "Mié").Arriba, 1.5);   // 6:00 p. m.

        // Nada sale de su columna ni de la grilla.
        Assert.All(g.Celdas, c => Assert.True(c.Izquierda >= -0.5 && c.Izquierda + c.Ancho <= c.AnchoDia + 0.5 && c.Arriba >= -0.5, $"{c.Etiqueta} {c.Dia} se sale de su columna"));
    }

    // ── El tablero del planificador: filtros, «Mover…» con teclado y toque, y arrastrar solo a donde se puede ─────────────

    /// <summary>El código de una tarjeta de la bandeja que puede ir a algunas columnas pero no a todas (para ver ambos casos).</summary>
    private const string ElegirTarjetaConBloqueos = """
        () => {
          const c = JSON.parse(document.getElementById('tablero').dataset.colocaciones);
          const cols = [...document.querySelectorAll('.plan-col[data-periodo]')].map(x => x.dataset.periodo);
          for (const card of document.querySelectorAll('.lista-pendientes .plan-card')) {
            const m = c[card.dataset.codigo.toUpperCase()] || {};
            const bloqueadas = cols.filter(p => m[p] && m[p].m && m[p].m.length);
            if (bloqueadas.length > 0 && bloqueadas.length < cols.length) return card.dataset.codigo;
          }
          return null;
        }
        """;

    /// <summary>Intercepta el guardado del movimiento (sin servidor): anota lo que se envía y responde que todo salió bien.</summary>
    private static async Task<List<string>> EspiarAsignacionesAsync(IPage p)
    {
        var enviadas = new List<string>();
        await p.RouteAsync("**/Planificador/Asignar", async ruta =>
        {
            lock (enviadas) enviadas.Add(ruta.Request.PostData ?? "");
            await ruta.FulfillAsync(new() { Status = 200, ContentType = "application/json", Body = "{\"ok\":true,\"mensaje\":\"ok\"}" });
        });
        return enviadas;
    }

    private static async Task<int> CuantasAsync(List<string> enviadas) { await Task.Delay(400); lock (enviadas) return enviadas.Count; }

    [Fact]
    public async Task ElFiltroDeLaBandejaMuestraSoloLoQueCorrespondeYBuscaPorCodigoONombre()
    {
        var p = await AbrirAsync("/Planificador?estado=vacio", ColorScheme.Light);
        var total = await p.Locator(".lista-pendientes .plan-card").CountAsync();
        Assert.True(total > 3, "la prueba necesita varias materias por planificar");
        var visibles = () => p.Locator(".lista-pendientes .plan-card:not([hidden])").CountAsync();

        Assert.Equal(total, await visibles());
        foreach (var filtro in new[] { "disponible", "rezagada", "ruta" })
        {
            await p.Locator($".filtro-chip[data-filtro='{filtro}']").ClickAsync();
            var esperadas = await p.Locator($".lista-pendientes .plan-card[data-etiquetas~='{filtro}']").CountAsync();
            Assert.Equal(esperadas, await visibles());
            Assert.Equal("true", await p.Locator($".filtro-chip[data-filtro='{filtro}']").GetAttributeAsync("aria-pressed"));
            Assert.Equal("false", await p.Locator(".filtro-chip[data-filtro='todas']").GetAttributeAsync("aria-pressed"));
            // El número entre paréntesis del botón es el de tarjetas que cumplen.
            Assert.Contains($"({esperadas})", await p.Locator($".filtro-chip[data-filtro='{filtro}']").InnerTextAsync());
        }

        await p.Locator(".filtro-chip[data-filtro='todas']").ClickAsync();
        Assert.Equal(total, await visibles());

        var codigo = (await p.Locator(".lista-pendientes .plan-card").First.GetAttributeAsync("data-codigo"))!;
        await p.FillAsync("#buscar-bandeja", codigo.ToLowerInvariant());
        Assert.True(await visibles() >= 1);
        Assert.True(await p.Locator(".lista-pendientes .plan-card:not([hidden])").EvaluateAllAsync<bool>("(els, c) => els.every(e => e.dataset.buscar.includes(c))", codigo.ToLowerInvariant()));
        await p.FillAsync("#buscar-bandeja", "zzz-no-existe");
        Assert.Equal(0, await visibles());
        Assert.True(await p.Locator("#sin-coincidencias").IsVisibleAsync());
        await p.FillAsync("#buscar-bandeja", "");
        Assert.Equal(total, await visibles());
    }

    [Fact]
    public async Task LaPaginaDelTableroConBandejaTraeAlertasConBotonParaResolverlas()
    {
        var p = await AbrirAsync("/Planificador?estado=vacio", ColorScheme.Light);   // esta captura es la que se audita con alertas a la vista

        Assert.Equal(1, await p.Locator("#alertas-plan").CountAsync());
        Assert.True(await p.Locator("#alertas-plan .alerta-plan").CountAsync() >= 1);
        Assert.True(await p.Locator("#alertas-plan .alerta-accion button").CountAsync() >= 1);
    }

    // ── El simulador de índice: lo que calcula el navegador es lo que calcula el núcleo ───────────────────────────────

    private record EntradaSimulador(double H0, double P0, double SinFila, List<double> Creditos);

    private static async Task<EntradaSimulador> LeerSimuladorAsync(IPage p)
    {
        var json = await p.EvaluateAsync<string>("""
            () => { const s = document.getElementById('simulador');
              return JSON.stringify({ h0: parseFloat(s.dataset.horas), p0: parseFloat(s.dataset.puntos), sinFila: parseFloat(s.dataset.sinFila),
                creditos: [...document.querySelectorAll('.sim-nota')].map(x => parseFloat(x.dataset.creditos)) }); }
            """);
        return System.Text.Json.JsonSerializer.Deserialize<EntradaSimulador>(json, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }

    /// <summary>Lo que diría el núcleo con lo que hay ahora en las listas de notas y en el objetivo.</summary>
    private static async Task VerificarSimuladorAsync(IPage p, string escenario)
    {
        var e = await LeerSimuladorAsync(p);
        var letras = await p.Locator(".sim-nota").EvaluateAllAsync<string[]>("els => els.map(x => x.value)");
        var objetivo = decimal.Parse(await p.InputValueAsync("#sim-objetivo"), System.Globalization.CultureInfo.InvariantCulture);
        var escala = HistorialAcademico.Core.Universidad.EscalaCalificaciones.Unapec;
        var filas = e.Creditos.Select((c, i) => new HistorialAcademico.Core.Planificacion.FilaSimulacion($"F{i}", (decimal)c, letras[i] == "" ? null : letras[i]));
        var esperado = HistorialAcademico.Core.Planificacion.SimuladorIndice.Calcular(
            new HistorialAcademico.Core.Indice.TotalesIndice(0, 0, (decimal)e.H0, (decimal)e.P0, 0), filas, (decimal)e.SinFila, objetivo, escala);

        var frase = HistorialAcademico.Core.Planificacion.SimuladorIndice.Describir(esperado, objetivo, "A");
        Assert.Equal(frase, (await p.InnerTextAsync("#sim-necesidad")).Trim());
        Assert.Equal(esperado.IndiceProyectado.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), (await p.InnerTextAsync("#sim-proyectado")).Trim());
        Assert.Equal($"con {esperado.CreditosConNota.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)} créditos con nota esperada", (await p.InnerTextAsync("#sim-detalle")).Trim());
        _ = escenario;
    }

    [Fact]
    public async Task AlCargarElSimuladorDelNavegadorDiceLoMismoQueYaDecíaElServidor()
    {
        var html = _paginas[Nombre("/Planificador")];
        var servidor = System.Net.WebUtility.HtmlDecode(Regex.Match(html, "id=\"sim-necesidad\"[^>]*>([^<]*)<").Groups[1].Value).Trim();
        var proyectadoServidor = Regex.Match(html, "id=\"sim-proyectado\"[^>]*>([^<]*)<").Groups[1].Value.Trim();
        Assert.NotEmpty(servidor);

        var p = await AbrirAsync("/Planificador", ColorScheme.Light);

        Assert.Equal(servidor, (await p.InnerTextAsync("#sim-necesidad")).Trim());
        Assert.Equal(proyectadoServidor, (await p.InnerTextAsync("#sim-proyectado")).Trim());
        await VerificarSimuladorAsync(p, "inicial");
    }

    [Fact]
    public async Task ElIndiceProyectadoYElPromedioNecesarioSeRecalculanAlInstanteComoEnElNucleo()
    {
        var p = await AbrirAsync("/Planificador", ColorScheme.Light);
        var cuantas = await p.Locator(".sim-nota").CountAsync();
        Assert.True(cuantas >= 3, "el simulador necesita varias materias");

        async Task PonerTodas(string letra) { for (var i = 0; i < cuantas; i++) await p.Locator(".sim-nota").Nth(i).SelectOptionAsync(letra); }

        await PonerTodas("A");
        await VerificarSimuladorAsync(p, "todas A");
        await PonerTodas("F");
        await VerificarSimuladorAsync(p, "todas F");
        await PonerTodas("E");
        await VerificarSimuladorAsync(p, "todas exentas");
        await PonerTodas("");
        await VerificarSimuladorAsync(p, "ninguna");
        var mezcla = new[] { "A", "B", "C", "D", "", "A", "F" };
        for (var i = 0; i < cuantas; i++) await p.Locator(".sim-nota").Nth(i).SelectOptionAsync(mezcla[i % mezcla.Length]);
        await VerificarSimuladorAsync(p, "mezcla");

        foreach (var objetivo in new[] { "0", "1.00", "2.50", "3.20", "3.50", "3.90", "4" })
        {
            await p.FillAsync("#sim-objetivo", objetivo);
            await VerificarSimuladorAsync(p, $"objetivo {objetivo}");
        }
        await PonerTodas("B");
        await p.FillAsync("#sim-objetivo", "3.00");
        await VerificarSimuladorAsync(p, "todo con nota");
    }

    [Fact]
    public async Task ElijoUnaNotaSeGuardaEnElServidorYElObjetivoSeRecuerda()
    {
        var p = await AbrirAsync("/Planificador", ColorScheme.Light);
        var guardadas = new List<string>();
        await p.RouteAsync("**/Planificador/NotaEsperada", async ruta =>
        {
            lock (guardadas) guardadas.Add(ruta.Request.PostData ?? "");
            await ruta.FulfillAsync(new() { Status = 200, ContentType = "application/json", Body = "{\"ok\":true,\"mensaje\":\"listo\"}" });
        });
        var select = p.Locator(".sim-nota").First;
        var codigo = (await select.GetAttributeAsync("data-codigo"))!;

        await select.SelectOptionAsync("B");
        await p.WaitForFunctionAsync("() => document.getElementById('sim-guardado').textContent.startsWith('Guardado')");

        Assert.Single(guardadas);
        Assert.Contains(codigo, guardadas[0]);
        Assert.Contains("B", guardadas[0]);

        await p.FillAsync("#sim-objetivo", "2.75");
        await p.ReloadAsync();
        Assert.Equal("2.75", await p.InputValueAsync("#sim-objetivo"));
        await VerificarSimuladorAsync(p, "objetivo recordado");
    }

    [Theory]
    [InlineData(ColorScheme.Light)]
    [InlineData(ColorScheme.Dark)]
    public async Task ElSimuladorConNotasYUnObjetivoSeLeeBien(ColorScheme esquema)
    {
        var p = await AbrirAsync("/Planificador", esquema);
        var cuantas = await p.Locator(".sim-nota").CountAsync();
        for (var i = 0; i < cuantas; i++) await p.Locator(".sim-nota").Nth(i).SelectOptionAsync(i % 2 == 0 ? "A" : "");
        await p.FillAsync("#sim-objetivo", "3.90");

        var r = await AuditarAsync(p);

        Assert.True(r.Fallos.Count == 0 && r.Blancas.Count == 0, $"simulador, tema {esquema}:\n{Resumen(r)}");
    }

    [Fact]
    public async Task LaPaginaDelPlanConCambiosTrasSincronizarTraeElAvisoConSuBotonEntendido()
    {
        var p = await AbrirAsync("/Planificador?estado=avisos", ColorScheme.Light);   // esta captura es la que se audita con el aviso a la vista

        Assert.Equal(1, await p.Locator("#avisos-plan").CountAsync());
        Assert.Contains("Salieron del plan porque ya están aprobadas o en curso", await p.Locator("#avisos-plan").InnerTextAsync());
        Assert.Equal(1, await p.Locator("#btn-entendido").CountAsync());
    }

    [Fact]
    public async Task ElFiltroElegidoSeRecuerdaAlRecargar()
    {
        var p = await AbrirAsync("/Planificador?estado=vacio", ColorScheme.Light);
        await p.Locator(".filtro-chip[data-filtro='disponible']").ClickAsync();

        await p.ReloadAsync();

        Assert.Equal("true", await p.Locator(".filtro-chip[data-filtro='disponible']").GetAttributeAsync("aria-pressed"));
    }

    [Theory]
    [InlineData(ColorScheme.Light)]
    [InlineData(ColorScheme.Dark)]
    public async Task MoverAtenuaLasColumnasDondeNoPuedeIrDiceElMotivoYSeLeeBien(ColorScheme esquema)
    {
        var p = await AbrirAsync("/Planificador?estado=vacio", esquema);
        var codigo = await p.EvaluateAsync<string?>(ElegirTarjetaConBloqueos);
        Assert.NotNull(codigo);

        await p.Locator($".lista-pendientes [data-mover][data-codigo='{codigo}']").ClickAsync();

        Assert.True(await p.Locator("#plan-modo").IsVisibleAsync());
        Assert.Contains($"Moviendo {codigo}", await p.Locator("#plan-modo-texto").InnerTextAsync());
        var bloqueadas = p.Locator(".plan-col.no-permitida");
        var permitidas = p.Locator(".plan-col:not(.no-permitida)");
        Assert.True(await bloqueadas.CountAsync() > 0 && await permitidas.CountAsync() > 0);
        // Las que no sirven dicen por qué y no ofrecen «Mover aquí»; las que sirven sí lo ofrecen.
        for (var i = 0; i < await bloqueadas.CountAsync(); i++)
        {
            var col = bloqueadas.Nth(i);
            Assert.StartsWith("No puede ir aquí:", await col.Locator("[data-motivo]").InnerTextAsync());
            Assert.False(await col.Locator("[data-destino]").IsVisibleAsync());
        }
        for (var i = 0; i < await permitidas.CountAsync(); i++)
        {
            var col = permitidas.Nth(i);
            Assert.False(await col.Locator("[data-motivo]").IsVisibleAsync());
            Assert.True(await col.Locator("[data-destino]").IsVisibleAsync());
        }

        var r = await AuditarAsync(p);
        Assert.True(r.Fallos.Count == 0 && r.Blancas.Count == 0, $"en modo mover, tema {esquema}:\n{Resumen(r)}");
    }

    [Fact]
    public async Task EscapeCancelaElMovimientoDevuelveElFocoYNoGuardaNada()
    {
        var p = await AbrirAsync("/Planificador?estado=vacio", ColorScheme.Light);
        var enviadas = await EspiarAsignacionesAsync(p);
        var codigo = (await p.EvaluateAsync<string?>(ElegirTarjetaConBloqueos))!;
        var boton = p.Locator($".lista-pendientes [data-mover][data-codigo='{codigo}']");
        await boton.ClickAsync();
        Assert.True(await p.Locator("#plan-modo").IsVisibleAsync());

        await p.Keyboard.PressAsync("Escape");

        Assert.False(await p.Locator("#plan-modo").IsVisibleAsync());
        Assert.Equal(0, await p.Locator(".plan-col.no-permitida").CountAsync());
        Assert.Equal(0, await p.Locator("[data-destino]:visible").CountAsync());
        Assert.True(await boton.EvaluateAsync<bool>("b => document.activeElement === b"));
        Assert.Equal(0, await CuantasAsync(enviadas));
    }

    [Fact]
    public async Task ConElTecladoSeEligeLaMateriaSeNavegaEntreLosPeriodosPosiblesYSeConfirma()
    {
        var p = await AbrirAsync("/Planificador?estado=vacio", ColorScheme.Light);
        var enviadas = await EspiarAsignacionesAsync(p);
        var codigo = (await p.EvaluateAsync<string?>(ElegirTarjetaConBloqueos))!;
        await p.Locator($".lista-pendientes [data-mover][data-codigo='{codigo}']").FocusAsync();

        await p.Keyboard.PressAsync("Enter");   // elegir la materia

        Assert.True(await p.EvaluateAsync<bool>("() => document.activeElement && document.activeElement.hasAttribute('data-destino')"));   // el foco ya está en el primer destino
        var visibles = await p.Locator("[data-destino]:visible").CountAsync();
        Assert.True(visibles >= 1);
        var primero = await p.EvaluateAsync<string>("() => document.activeElement.dataset.periodo");
        await p.WaitForFunctionAsync("() => document.getElementById('plan-estado').textContent.length > 0");   // el aviso a lectores de pantalla se escribe un instante después
        var anunciado = await p.Locator("#plan-estado").TextContentAsync();
        Assert.Contains($"Moviendo {codigo}", anunciado);
        Assert.Contains("Períodos posibles:", anunciado);

        string elegido = primero;
        if (visibles > 1)
        {
            await p.Keyboard.PressAsync("ArrowRight");
            elegido = await p.EvaluateAsync<string>("() => document.activeElement.dataset.periodo");
            Assert.NotEqual(primero, elegido);
            await p.Keyboard.PressAsync("ArrowLeft");
            Assert.Equal(primero, await p.EvaluateAsync<string>("() => document.activeElement.dataset.periodo"));
            await p.Keyboard.PressAsync("ArrowRight");
        }
        await p.Keyboard.PressAsync("Enter");   // confirmar

        Assert.Equal(1, await CuantasAsync(enviadas));
        var datos = enviadas[0];
        Assert.Contains(codigo, datos);
        Assert.Contains(elegido, datos);
    }

    [Fact]
    public async Task ConLaPantallaTactilSeMuevePorToquesSinArrastrar()
    {
        var contexto = await _navegador.NewContextAsync(new() { HasTouch = true, IsMobile = true, ViewportSize = new() { Width = 390, Height = 844 } });
        var p = await contexto.NewPageAsync();
        await p.GotoAsync(_servidor.Direccion(Nombre("/Planificador?estado=vacio")));
        var enviadas = await EspiarAsignacionesAsync(p);
        var codigo = (await p.EvaluateAsync<string?>(ElegirTarjetaConBloqueos))!;

        await p.Locator($".lista-pendientes [data-mover][data-codigo='{codigo}']").TapAsync();
        var destino = p.Locator("[data-destino]:visible").First;
        var periodo = await destino.GetAttributeAsync("data-periodo");
        await destino.TapAsync();

        Assert.Equal(1, await CuantasAsync(enviadas));
        Assert.Contains(codigo, enviadas[0]);
        Assert.Contains(periodo!, enviadas[0]);
    }

    [Fact]
    public async Task ArrastrandoConElRatonSoloSeSueltaEnLasColumnasDondeSePuede()
    {
        var p = await AbrirAsync("/Planificador?estado=vacio", ColorScheme.Light);
        var enviadas = await EspiarAsignacionesAsync(p);
        var codigo = (await p.EvaluateAsync<string?>(ElegirTarjetaConBloqueos))!;
        var tarjeta = $".lista-pendientes .plan-card[data-codigo='{codigo}']";
        var info = await p.EvaluateAsync<string>("""
            (codigo) => {
              const c = JSON.parse(document.getElementById('tablero').dataset.colocaciones)[codigo.toUpperCase()] || {};
              const cols = [...document.querySelectorAll('.plan-col[data-periodo]')].map(x => x.dataset.periodo);
              return JSON.stringify({ bloqueada: cols.find(p => c[p] && c[p].m && c[p].m.length), libre: cols.find(p => !(c[p] && c[p].m && c[p].m.length)) });
            }
            """, codigo);
        var (bloqueada, libre) = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(info)! is { } d ? (d["bloqueada"], d["libre"]) : ("", "");

        await p.EvaluateAsync("() => { window.__ev = []; ['dragstart', 'dragover', 'drop', 'dragend'].forEach(n => document.addEventListener(n, e => window.__ev.push(n + ':' + (e.target.className || e.target.tagName)), true)); }");
        await p.DragAndDropAsync(tarjeta, $".plan-col[data-periodo='{bloqueada}'] .plan-col-cuerpo");
        Assert.Equal(0, await CuantasAsync(enviadas));   // a una columna atenuada no se puede soltar
        var eventosBloqueada = await p.EvaluateAsync<string[]>("() => window.__ev");
        Assert.Contains(eventosBloqueada, e => e.StartsWith("dragstart"));   // sí empezó a arrastrar: si no, la prueba no prueba nada

        // Una página nueva: tras un arrastre rechazado, el navegador de pruebas no inicia otro en la misma.
        var q = await AbrirAsync("/Planificador?estado=vacio", ColorScheme.Light);
        enviadas = await EspiarAsignacionesAsync(q);
        await q.DragAndDropAsync(tarjeta, $".plan-col[data-periodo='{libre}'] .plan-col-cuerpo");
        Assert.Equal(1, await CuantasAsync(enviadas));
        Assert.Contains(codigo, enviadas[0]);
        Assert.Contains(libre, enviadas[0]);
    }

    private static Task<string> Atributo(IPage p, string nombre) => p.EvaluateAsync<string>("n => document.documentElement.getAttribute(n)", nombre);
    private static Task<string> FondoCuerpo(IPage p) => p.EvaluateAsync<string>("() => getComputedStyle(document.body).backgroundColor");

    // ── El interruptor ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ElInterruptorEstaEnLaBarraSuperiorConTresOpcionesAccesibles()
    {
        var p = await AbrirAsync("/", ColorScheme.Light);

        Assert.Equal(1, await p.Locator(".barra-superior .selector-tema").CountAsync());
        var botones = p.Locator(".selector-tema-opcion");
        Assert.Equal(3, await botones.CountAsync());
        Assert.Equal(new[] { "Tema automático, según tu sistema", "Tema claro", "Tema oscuro" },
            await botones.EvaluateAllAsync<string[]>("bs => bs.map(b => b.getAttribute('aria-label'))"));
        Assert.All(await botones.EvaluateAllAsync<bool[]>("bs => bs.map(b => b.getBoundingClientRect().width >= 28 && b.getBoundingClientRect().height >= 28)"), grande => Assert.True(grande));   // tamaño táctil razonable
        Assert.True(await p.Locator(".selector-tema").IsVisibleAsync());   // visible sin abrir ningún menú
    }

    [Fact]
    public async Task PorDefectoSigueLaPreferenciaOscuraDelSistema()
    {
        var p = await AbrirAsync("/", ColorScheme.Dark);

        Assert.Equal("oscuro", await Atributo(p, "data-tema"));
        Assert.Equal("sistema", await Atributo(p, "data-tema-preferencia"));
        Assert.Equal("rgb(14, 18, 35)", await FondoCuerpo(p));                                   // --color-fondo oscuro
        Assert.Equal("true", await p.Locator(".selector-tema-opcion[data-tema='sistema']").GetAttributeAsync("aria-pressed"));
        Assert.Equal("false", await p.Locator(".selector-tema-opcion[data-tema='oscuro']").GetAttributeAsync("aria-pressed"));
    }

    [Fact]
    public async Task PorDefectoSigueLaPreferenciaClaraDelSistema()
    {
        var p = await AbrirAsync("/", ColorScheme.Light);

        Assert.Equal("claro", await Atributo(p, "data-tema"));
        Assert.Equal("rgb(244, 246, 251)", await FondoCuerpo(p));
    }

    [Fact]
    public async Task ElUsuarioPuedeActivarElModoOscuroYQuedaGuardado()
    {
        var p = await AbrirAsync("/", ColorScheme.Light);
        await p.ClickAsync(".selector-tema-opcion[data-tema='oscuro']");

        Assert.Equal("oscuro", await Atributo(p, "data-tema"));
        Assert.Equal("rgb(14, 18, 35)", await FondoCuerpo(p));
        Assert.Equal("true", await p.Locator(".selector-tema-opcion[data-tema='oscuro']").GetAttributeAsync("aria-pressed"));
        Assert.Equal("false", await p.Locator(".selector-tema-opcion[data-tema='sistema']").GetAttributeAsync("aria-pressed"));
        Assert.Equal("oscuro", await p.EvaluateAsync<string>("() => localStorage.getItem('tema')"));

        await p.ReloadAsync();   // sigue oscuro aunque el sistema sea claro
        Assert.Equal("oscuro", await Atributo(p, "data-tema"));
        Assert.Equal("true", await p.Locator(".selector-tema-opcion[data-tema='oscuro']").GetAttributeAsync("aria-pressed"));
    }

    [Fact]
    public async Task ElUsuarioPuedeForzarElTemaClaroAunqueElSistemaSeaOscuro()
    {
        var p = await AbrirAsync("/", ColorScheme.Dark);
        await p.ClickAsync(".selector-tema-opcion[data-tema='claro']");
        await p.ReloadAsync();

        Assert.Equal("claro", await Atributo(p, "data-tema"));
        Assert.Equal("rgb(244, 246, 251)", await FondoCuerpo(p));
    }

    [Fact]
    public async Task VolverAAutomaticoVuelveASeguirAlSistema()
    {
        var p = await AbrirAsync("/", ColorScheme.Dark);
        await p.ClickAsync(".selector-tema-opcion[data-tema='claro']");
        Assert.Equal("claro", await Atributo(p, "data-tema"));

        await p.ClickAsync(".selector-tema-opcion[data-tema='sistema']");

        Assert.Equal("oscuro", await Atributo(p, "data-tema"));
        Assert.Equal("sistema", await Atributo(p, "data-tema-preferencia"));
    }

    [Fact]
    public async Task ConAutomaticoElTemaCambiaEnVivoCuandoCambiaElSistema()
    {
        var p = await AbrirAsync("/", ColorScheme.Light);
        Assert.Equal("claro", await Atributo(p, "data-tema"));

        await p.EmulateMediaAsync(new() { ColorScheme = ColorScheme.Dark });   // p. ej. anochece y el sistema pasa a oscuro
        await p.WaitForFunctionAsync("() => document.documentElement.getAttribute('data-tema') === 'oscuro'");
        await p.EmulateMediaAsync(new() { ColorScheme = ColorScheme.Light });
        await p.WaitForFunctionAsync("() => document.documentElement.getAttribute('data-tema') === 'claro'");
    }

    [Fact]
    public async Task ConUnaEleccionExplicitaElCambioDelSistemaNoLaPisa()
    {
        var p = await AbrirAsync("/", ColorScheme.Light);
        await p.ClickAsync(".selector-tema-opcion[data-tema='claro']");

        await p.EmulateMediaAsync(new() { ColorScheme = ColorScheme.Dark });
        await p.WaitForTimeoutAsync(300);

        Assert.Equal("claro", await Atributo(p, "data-tema"));
    }

    [Fact]
    public async Task SinAlmacenamientoDisponibleFuncionaConLaPreferenciaDelSistema()
    {
        const string bloquear = "Storage.prototype.getItem = () => { throw new Error('bloqueado'); }; Storage.prototype.setItem = () => { throw new Error('bloqueado'); };";
        var p = await AbrirAsync("/", ColorScheme.Dark, bloquear);
        var errores = new List<string>();
        p.PageError += (_, e) => errores.Add(e);

        Assert.Equal("oscuro", await Atributo(p, "data-tema"));
        await p.ClickAsync(".selector-tema-opcion[data-tema='claro']");              // el clic no debe romper aunque no se pueda guardar
        Assert.Equal("claro", await Atributo(p, "data-tema"));  // vale para esta visita
        Assert.Empty(errores);
    }

    [Fact]
    public void ElScriptDelTemaVaAntesDeLosEstilosParaEvitarElDestello()
    {
        var html = _paginas[Nombre("/")];
        var script = html.IndexOf("data-tema-preferencia", StringComparison.Ordinal);
        var primerEstilo = html.IndexOf("rel=\"stylesheet\"", StringComparison.Ordinal);
        Assert.InRange(script, 1, primerEstilo - 1);
        Assert.Contains("<meta name=\"color-scheme\" content=\"light dark\"", html);
    }

    [Fact]
    public async Task ElTemaOscuroCambiaTambienLosControlesDelNavegador()
    {
        var p = await AbrirAsync("/Equivalencias", ColorScheme.Dark);
        Assert.Equal("dark", await p.EvaluateAsync<string>("() => getComputedStyle(document.documentElement).colorScheme"));   // barras de desplazamiento y formularios
        var p2 = await AbrirAsync("/Equivalencias", ColorScheme.Light);
        Assert.Equal("light", await p2.EvaluateAsync<string>("() => getComputedStyle(document.documentElement).colorScheme"));
    }

    // ── Auditoría de contraste sobre las páginas reales ───────────────────────────────────

    private const string Auditoria = """
        () => {
            const analizar = c => { const m = c.match(/rgba?\(([^)]+)\)/); if (!m) return { r: 0, g: 0, b: 0, a: 0 }; const v = m[1].split(',').map(s => parseFloat(s)); return { r: v[0], g: v[1], b: v[2], a: v.length > 3 ? v[3] : 1 }; };
            const mezclar = (arriba, abajo) => ({ r: arriba.r * arriba.a + abajo.r * (1 - arriba.a), g: arriba.g * arriba.a + abajo.g * (1 - arriba.a), b: arriba.b * arriba.a + abajo.b * (1 - arriba.a), a: 1 });
            const lum = c => { const f = x => { x /= 255; return x <= 0.03928 ? x / 12.92 : Math.pow((x + 0.055) / 1.055, 2.4); }; return 0.2126 * f(c.r) + 0.7152 * f(c.g) + 0.0722 * f(c.b); };
            const ratio = (a, b) => { const x = lum(a), y = lum(b); return (Math.max(x, y) + 0.05) / (Math.min(x, y) + 0.05); };
            const rgb = c => `rgb(${Math.round(c.r)}, ${Math.round(c.g)}, ${Math.round(c.b)})`;

            // Color de fondo que realmente se ve detrás de un elemento: se apilan las capas semitransparentes hasta una opaca.
            const fondoDe = el => {
                const capas = [];
                for (let e = el; e; e = e.parentElement) {
                    const c = analizar(getComputedStyle(e).backgroundColor);
                    if (c.a > 0) capas.push(c);
                    if (c.a >= 1) break;
                }
                let base = analizar(getComputedStyle(document.documentElement).backgroundColor);
                if (base.a < 1) base = mezclar(base, { r: 255, g: 255, b: 255, a: 1 });
                return capas.reverse().reduce((abajo, arriba) => mezclar(arriba, abajo), base);
            };

            const fallos = [];
            const vistos = new Set();
            const revisar = (el, texto, colorTexto, fondo) => {
                const cs = getComputedStyle(el);
                const tam = parseFloat(cs.fontSize);
                const grande = tam >= 24 || (tam >= 18.66 && parseInt(cs.fontWeight) >= 700);
                const minimo = grande ? 3 : 4.5;
                const primero = mezclar(colorTexto, fondo);
                const r = ratio(primero, fondo);
                if (r < minimo) {
                    const clave = el.className + '|' + rgb(primero) + '|' + rgb(fondo);
                    if (vistos.has(clave)) return;
                    vistos.add(clave);
                    fallos.push({ texto: texto.trim().slice(0, 40), clase: String(el.className.baseVal ?? el.className), primero: rgb(primero), fondo: rgb(fondo), ratio: Math.round(r * 100) / 100 });
                }
            };

            const oculto = el => !!el.closest('.visually-hidden, [hidden], script, style, title, desc, .modal:not(.show), .sync-overlay[hidden], details:not([open]) > :not(summary)')
                || getComputedStyle(el).display === 'none' || getComputedStyle(el).visibility === 'hidden';

            const recorrido = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT);
            for (let n = recorrido.nextNode(); n; n = recorrido.nextNode()) {
                const texto = n.textContent;
                if (!texto.trim()) continue;
                const el = n.parentElement;
                if (!el || oculto(el) || el.closest('button:disabled, input:disabled')) continue;
                const enSvg = el.closest('svg') !== null;
                const colorTexto = analizar(enSvg ? getComputedStyle(el).fill : getComputedStyle(el).color);
                revisar(el, texto, colorTexto, fondoDe(el));
            }

            // Campos de formulario: el texto que se escribe y el marcador de posición.
            document.querySelectorAll('input:not([type=hidden]):not([type=checkbox]):not([type=radio]), select, textarea').forEach(c => {
                if (oculto(c) || c.disabled) return;
                const cs = getComputedStyle(c);
                revisar(c, '[campo] ' + (c.name || c.id), analizar(cs.color), fondoDe(c));
                if (c.placeholder) revisar(c, '[marcador] ' + c.placeholder, analizar(getComputedStyle(c, '::placeholder').color), fondoDe(c));
            });

            // Superficies blancas puras: en el tema oscuro no debería quedar ninguna.
            const blancas = [];
            if (document.documentElement.getAttribute('data-tema') === 'oscuro') {
                document.querySelectorAll('body *').forEach(e => {
                    if (e.closest('svg') || oculto(e)) return;
                    const c = analizar(getComputedStyle(e).backgroundColor);
                    if (c.a > 0.5 && c.r > 250 && c.g > 250 && c.b > 250) blancas.push(String(e.className) + ' <' + e.tagName.toLowerCase() + '>');
                });
            }
            return JSON.stringify({ fallos, blancas: [...new Set(blancas)] });
        }
        """;

    public class ResultadoAuditoria
{
    public List<FalloContraste> Fallos { get; set; } = new();
    public List<string> Blancas { get; set; } = new();
}

    private static readonly System.Text.Json.JsonSerializerOptions OpcionesJson = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Ejecuta la auditoría en la página y lee su resultado (el script lo devuelve como texto JSON).</summary>
    private static async Task<ResultadoAuditoria> AuditarAsync(IPage p) =>
        System.Text.Json.JsonSerializer.Deserialize<ResultadoAuditoria>(await p.EvaluateAsync<string>(Auditoria), OpcionesJson)!;

    private static string Resumen(ResultadoAuditoria r) =>
        string.Join("\n", r.Fallos.Take(12).Select(f => $"  «{f.Texto}» .{f.Clase}: {f.Primero} sobre {f.Fondo} = {f.Ratio}:1"))
        + (r.Blancas.Count > 0 ? "\n  Superficies blancas: " + string.Join(", ", r.Blancas.Take(8)) : "");

    public static IEnumerable<object[]> PaginasYTemas() =>
        from ruta in Rutas
        from esquema in new[] { ColorScheme.Light, ColorScheme.Dark }
        select new object[] { ruta, esquema };

    [Theory]
    [MemberData(nameof(PaginasYTemas))]
    public async Task TodoElTextoDeLaPaginaTieneContrasteSuficiente(string ruta, ColorScheme esquema)
    {
        var p = await AbrirAsync(ruta, esquema);
        var r = await AuditarAsync(p);

        Assert.True(r.Fallos.Count == 0 && r.Blancas.Count == 0,
            $"{ruta} en tema {(esquema == ColorScheme.Dark ? "oscuro" : "claro")}:\n{Resumen(r)}");
    }

    [Theory]
    [InlineData(ColorScheme.Light)]
    [InlineData(ColorScheme.Dark)]
    public async Task ElCuadroDeDetalleDelMapaTambienSeLee(ColorScheme esquema)
    {
        var p = await AbrirAsync("/Pensum/Mapa", esquema);
        await p.ClickAsync(".mapa-card[data-codigo='ISO800']");
        await p.WaitForSelectorAsync("#modalMateria.show");
        await p.WaitForTimeoutAsync(400);

        var r = await AuditarAsync(p);
        Assert.True(r.Fallos.Count == 0 && r.Blancas.Count == 0, Resumen(r));
    }

    [Theory]
    [InlineData(ColorScheme.Light)]
    [InlineData(ColorScheme.Dark)]
    public async Task ElPlanificadorConLosPanelesDeConfiguracionAbiertosSeLee(ColorScheme esquema)
    {
        var p = await AbrirAsync("/Planificador", esquema);
        await p.ClickAsync("[data-bs-target='#configPlan']");
        await p.ClickAsync("[data-bs-target='#gestionEscenarios']");
        await p.ClickAsync("[data-bs-target='#generarPlan']");   // la pregunta antes de generar el plan
        await p.WaitForTimeoutAsync(500);

        var r = await AuditarAsync(p);
        Assert.True(r.Fallos.Count == 0 && r.Blancas.Count == 0, Resumen(r));
    }

    [Theory]
    [InlineData(ColorScheme.Light)]
    [InlineData(ColorScheme.Dark)]
    public async Task ElDashboardVacioTambienSeLee(ColorScheme esquema)
    {
        var p = await AbrirAsync("/vacia.html", esquema);
        var r = await AuditarAsync(p);
        Assert.True(r.Fallos.Count == 0 && r.Blancas.Count == 0, Resumen(r));
    }

    [Fact]
    public async Task LaAuditoriaRealmenteDetectaProblemas()
    {
        // Si la auditoría nunca fallara no probaría nada: se comprueba que ve un texto casi invisible y una superficie blanca.
        var p = await AbrirAsync("/vacia.html", ColorScheme.Dark);
        Assert.Empty((await AuditarAsync(p)).Fallos);

        await p.EvaluateAsync("""
            () => document.querySelector('main').insertAdjacentHTML('beforeend',
                '<p style="color:#cccccc;background:#ffffff">Texto casi invisible</p>' +
                '<p style="color:#000000;background:#ffffff">Texto legible</p>')
            """);
        var r = await AuditarAsync(p);

        Assert.Contains(r.Fallos, f => f.Texto.StartsWith("Texto casi invisible") && f.Ratio < 2);
        Assert.DoesNotContain(r.Fallos, f => f.Texto.StartsWith("Texto legible"));
        Assert.NotEmpty(r.Blancas);   // en tema oscuro una superficie blanca pura se marca
    }

    [Fact]
    public async Task ElTemaOscuroUsaLosColoresDeMarcaAclaradosEnLosBotones()
    {
        var p = await AbrirAsync("/", ColorScheme.Dark);

        Assert.Equal("rgb(159, 176, 255)", await p.EvaluateAsync<string>("() => getComputedStyle(document.querySelector('[data-sync] button')).backgroundColor"));   // periwinkle
        Assert.Equal("rgb(14, 18, 35)", await p.EvaluateAsync<string>("() => getComputedStyle(document.querySelector('[data-sync] button')).color"));           // texto oscuro encima
        Assert.Equal("rgb(196, 162, 58)", await p.EvaluateAsync<string>("() => getComputedStyle(document.querySelector('.menu-item.activo')).backgroundColor")); // el dorado se mantiene
        Assert.Equal("rgb(10, 14, 36)", await p.EvaluateAsync<string>("() => getComputedStyle(document.querySelector('.menu-lateral')).backgroundColor"));
    }
}
