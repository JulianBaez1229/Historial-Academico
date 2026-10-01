using System.Net;
using System.Text.RegularExpressions;
using HistorialAcademico.Core.Entities;
using HistorialAcademico.Core.Pensum;
using HistorialAcademico.Web.Data;
using HistorialAcademico.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HistorialAcademico.Tests;

/// <summary>Guardar y borrar pénsums personales (los que se crean pegando texto), con un catálogo temporal.</summary>
public class PensumPersonalServiceTests : IDisposable
{
    private readonly CatalogoTemporal _catalogo = new();
    private readonly BdPrueba _bd = new();
    private readonly ReglasUniversidadService _reglas;
    private readonly CarreraService _s;

    public PensumPersonalServiceTests()
    {
        _reglas = _catalogo.Reglas();
        _s = new CarreraService(_bd.Db, _reglas, new AcademicoService(_bd.Db, _reglas));
    }

    public void Dispose() { _bd.Dispose(); _catalogo.Dispose(); }

    private static List<FilaEditable> Filas() => new()
    {
        new() { Codigo = "AAA100", Nombre = "Introducción", Creditos = "3", Cuatrimestre = "1" },
        new() { Codigo = "BBB200", Nombre = "Fundamentos", Creditos = "5", Cuatrimestre = "2", Prerrequisitos = "AAA100" },
    };

    private string Archivo(string universidad, string archivo) => Path.Combine(_catalogo.Carpeta, universidad, archivo);

    // ── Los datos de la carrera ───────────────────────────────────────────────────────────

    [Fact]
    public void LosMetadatosSalenDeLoQueEscribioLaPersona()
    {
        var (meta, error) = _s.MetadatosPersonales("uni-prueba", "  Ingeniería de Software  ", "2022-b");

        Assert.Null(error);
        Assert.Equal(new MetadatosPensum("uni-prueba", "personal-ingenieria-de-software", "Ingeniería de Software", "2022-b"), meta);
    }

    [Theory]
    [InlineData("uni-prueba", "", "2022", "Escribe el nombre de la carrera")]
    [InlineData("uni-prueba", "   ", "2022", "Escribe el nombre de la carrera")]
    [InlineData("uni-prueba", "???", "2022", "debe tener letras o números")]
    [InlineData("uni-prueba", "Derecho", "", "Escribe la versión del plan")]
    [InlineData("uni-prueba", "Derecho", "../x", "Escribe la versión del plan")]
    [InlineData("uni-prueba", "Derecho", "2022 nuevo", "Escribe la versión del plan")]
    [InlineData("no-existe", "Derecho", "2022", "Elige la universidad")]
    [InlineData("", "Derecho", "2022", "Elige la universidad")]
    [InlineData(null, "Derecho", "2022", "Elige la universidad")]
    [InlineData("../unapec", "Derecho", "2022", "Elige la universidad")]
    public void LosDatosInvalidosSeRechazanConUnMotivo(string? universidad, string? nombre, string? version, string motivo)
    {
        var (meta, error) = _s.MetadatosPersonales(universidad, nombre, version);

        Assert.Null(meta);
        Assert.Contains(motivo, error);
    }

    [Fact]
    public void ElNombreDeLaCarreraTieneUnLimiteYElIdentificadorSeAcorta()
    {
        Assert.Contains("máximo 100", _s.MetadatosPersonales("uni-prueba", new string('x', 101), "1").Error);

        var (meta, _) = _s.MetadatosPersonales("uni-prueba", new string('a', 100), "1");
        Assert.Equal(PensumDefinicion.PrefijoPersonal.Length + 40, meta!.Carrera.Length);   // «personal-» + 40 caracteres
    }

    [Fact]
    public void UnNombreConRutasNoPuedeEscaparDeLaCarpeta()
    {
        var (meta, _) = _s.MetadatosPersonales("uni-prueba", "../../../Windows/x", "1");

        Assert.Equal("personal-windows-x", meta!.Carrera);
        Assert.DoesNotContain("..", meta.Carrera);
        Assert.DoesNotContain("/", meta.Carrera);
    }

    // ── Guardar ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GuardaUnArchivoValidoEnLaCarpetaDeLaUniversidadYApareceEnLaLista()
    {
        var r = await _s.GuardarPersonalAsync(Filas(), "uni-prueba", "Derecho", "2022", reemplazar: false, usar: false);

        Assert.True(r.Ok, r.Mensaje);
        Assert.Contains("Pénsum personal guardado: Derecho (plan 2022), 2 materias, 8 créditos.", r.Mensaje);
        var ruta = Archivo("uni-prueba", "personal-derecho-2022.json");
        Assert.True(File.Exists(ruta));
        var leido = PensumJson.Leer(File.ReadAllText(ruta), ruta);
        Assert.True(leido.EsValido, string.Join(" | ", leido.Errores));
        Assert.True(leido.Definicion!.EsPersonal);

        var lista = (await _s.ObtenerAsync("uni-prueba", "derecho")).Pensums;
        var personal = lista.Single(p => p.EsPersonal);
        Assert.Equal("uni-prueba/personal-derecho-2022.json", personal.Definicion.Clave);
        Assert.False(personal.EsActivo);
        Assert.Empty(await _bd.Db.PensumActivo.ToListAsync());                  // no se puso en uso
    }

    [Fact]
    public async Task ConUsarLoPoneEnUsoConLasReglasDeSuUniversidad()
    {
        var r = await _s.GuardarPersonalAsync(Filas(), "uni-prueba", "Derecho", "2022", reemplazar: false, usar: true);

        Assert.True(r.Ok, r.Mensaje);
        Assert.Contains("Pénsum activo: Derecho (plan 2022)", r.Mensaje);
        Assert.Equal("uni-prueba/personal-derecho-2022.json", (await _bd.Db.PensumActivo.SingleAsync()).Clave);
        Assert.Equal(new[] { "AAA100", "BBB200" }, (await _bd.Db.MateriasPensum.OrderBy(m => m.Codigo).ToListAsync()).Select(m => m.Codigo));
        Assert.Equal("uni-prueba", _reglas.Activa.Id);
    }

    [Fact]
    public async Task UnPensumConProblemasNoSeGuarda()
    {
        var filas = Filas();
        filas[1].Prerrequisitos = "ZZZ999";

        var r = await _s.GuardarPersonalAsync(filas, "uni-prueba", "Derecho", "2022", false, false);

        Assert.False(r.Ok);
        Assert.Contains("tiene problemas", r.Mensaje);
        Assert.False(File.Exists(Archivo("uni-prueba", "personal-derecho-2022.json")));
    }

    [Fact]
    public async Task NoPisaUnPensumPersonalQueYaExisteSalvoQueSePida()
    {
        await _s.GuardarPersonalAsync(Filas(), "uni-prueba", "Derecho", "2022", false, false);
        var ruta = Archivo("uni-prueba", "personal-derecho-2022.json");
        File.WriteAllText(ruta, "editado a mano");

        var sin = await _s.GuardarPersonalAsync(Filas(), "uni-prueba", "Derecho", "2022", reemplazar: false, usar: false);
        Assert.False(sin.Ok);
        Assert.Contains("Ya tienes un pénsum personal «Derecho» con la versión 2022", sin.Mensaje);
        Assert.Equal("editado a mano", File.ReadAllText(ruta));

        var con = await _s.GuardarPersonalAsync(Filas(), "uni-prueba", "Derecho", "2022", reemplazar: true, usar: false);
        Assert.True(con.Ok);
        Assert.StartsWith("{", File.ReadAllText(ruta));
    }

    [Fact]
    public async Task UnaVersionDistintaEsOtroArchivo()
    {
        await _s.GuardarPersonalAsync(Filas(), "uni-prueba", "Derecho", "2022", false, false);

        var r = await _s.GuardarPersonalAsync(Filas(), "uni-prueba", "Derecho", "2023", false, false);

        Assert.True(r.Ok);
        Assert.Equal(2, (await _s.ObtenerAsync("uni-prueba", "derecho")).Pensums.Count(p => p.EsPersonal));
    }

    [Fact]
    public async Task ConDatosInvalidosNoEscribeNada()
    {
        var antes = Directory.GetFiles(_catalogo.Carpeta, "*", SearchOption.AllDirectories).Length;

        var r = await _s.GuardarPersonalAsync(Filas(), "no-existe", "Derecho", "2022", false, false);

        Assert.False(r.Ok);
        Assert.Equal(antes, Directory.GetFiles(_catalogo.Carpeta, "*", SearchOption.AllDirectories).Length);
    }

    [Fact]
    public async Task SinCarpetaDePensumsNoPuedeGuardar()
    {
        var sinCarpeta = ReglasUniversidadService.Fijas(UniversidadDePrueba.Reglas);
        var s = new CarreraService(_bd.Db, sinCarpeta, new AcademicoService(_bd.Db, sinCarpeta));

        var r = await s.GuardarPersonalAsync(Filas(), "uni-prueba", "Derecho", "2022", false, false);

        Assert.False(r.Ok);
    }

    [Fact]
    public async Task ElPensumPersonalConvalidaConLasEquivalenciasQueNoTiene_YSeCalculaComoCualquierOtro()
    {
        _bd.Db.Periodos.Add(new Periodo { Nombre = "SEM1 2024", Orden = 1, Materias = new() { new MateriaCursada { Codigo = "AAA100", Calificacion = "A", HorasCredito = 3 } } });
        await _bd.Db.SaveChangesAsync();

        var r = await _s.GuardarPersonalAsync(Filas(), "uni-prueba", "Derecho", "2022", false, usar: true);
        var e = await new AcademicoService(_bd.Db, _reglas).ObtenerAsync();

        Assert.True(r.Ok, r.Mensaje);
        Assert.Contains("llevas 3 de 8 créditos aprobados", r.Mensaje);
        Assert.Equal(EstadoMateria.Aprobada, e.Pensum.Buscar("AAA100")!.Estado);
        Assert.Equal(EstadoMateria.Disponible, e.Pensum.Buscar("BBB200")!.Estado);
    }

    // ── Borrar ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task BorraUnPensumPersonalQueNoEstaEnUso()
    {
        await _s.GuardarPersonalAsync(Filas(), "uni-prueba", "Derecho", "2022", false, false);

        var r = await _s.EliminarPersonalAsync("uni-prueba/personal-derecho-2022.json");

        Assert.True(r.Ok, r.Mensaje);
        Assert.Contains("borrado", r.Mensaje);
        Assert.False(File.Exists(Archivo("uni-prueba", "personal-derecho-2022.json")));
        Assert.DoesNotContain((await _s.ObtenerAsync(null, null)).Pensums, p => p.EsPersonal);
    }

    [Fact]
    public async Task NoBorraElPensumEnUso()
    {
        await _s.GuardarPersonalAsync(Filas(), "uni-prueba", "Derecho", "2022", false, usar: true);

        var r = await _s.EliminarPersonalAsync("uni-prueba/personal-derecho-2022.json");

        Assert.False(r.Ok);
        Assert.Contains("está en uso", r.Mensaje);
        Assert.True(File.Exists(Archivo("uni-prueba", "personal-derecho-2022.json")));
    }

    [Fact]
    public async Task NoBorraLosPensumsDelCatalogoCompartidoNiLosQueNoExisten()
    {
        var compartido = await _s.EliminarPersonalAsync("unapec/ingenieria-software-11.json");
        var inexistente = await _s.EliminarPersonalAsync("uni-prueba/personal-nada-1.json");
        var raro = await _s.EliminarPersonalAsync("../../Windows/system.ini");

        Assert.False(compartido.Ok);
        Assert.Contains("Solo se pueden borrar los pénsums personales", compartido.Mensaje);
        Assert.True(File.Exists(Archivo("unapec", "ingenieria-software-11.json")));
        Assert.False(inexistente.Ok);
        Assert.False(raro.Ok);
    }

    // ── Git ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void LosPensumsPersonalesQuedanFueraDeGit()
    {
        var gitignore = File.ReadAllText(Path.Combine(PensumEjemplo.Raiz, ".gitignore"));

        Assert.Contains("pensums/*/personal-*.json", gitignore);
    }
}

/// <summary>Una aplicación con su propio catálogo de pénsums en una carpeta temporal: aquí sí se puede guardar y borrar archivos.</summary>
public class AppConCatalogoTemporalFactory : AppConDatosFactory
{
    private readonly CatalogoTemporal _catalogo = new();

    protected override string? CarpetaPensums => _catalogo.Carpeta;

    public string Carpeta => _catalogo.Carpeta;

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _catalogo.Dispose();
    }
}

/// <summary>Importar un pénsum pegando texto: de la pantalla de pegado a la vista previa editable y al guardado, como lo haría el navegador.</summary>
public class ImportarPensumPantallaTests
{
    private const string TextoPlan = "Código\tAsignatura\tCréditos\tPrerrequisitos\nCuatrimestre 1\nAAA100\tIntroducción al Derecho\t3\t\nBBB100\tLógica Jurídica\t3\t\n" +
                                     "Cuatrimestre 2\nCCC200\tDerecho Civil\t5\tAAA100; BBB100\nTotal de créditos: 11";

    private static async Task<AppConCatalogoTemporalFactory> NuevaAsync()
    {
        var app = new AppConCatalogoTemporalFactory();
        await app.InitializeAsync();
        return app;
    }

    private static async Task<(HttpResponseMessage Respuesta, string Html)> PostAsync(AppFactory app, string url, IEnumerable<KeyValuePair<string, string>> campos)
    {
        using var c = app.CreateClient();
        var pagina = await c.GetStringAsync("/Carrera/Importar");
        var token = Regex.Match(pagina, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        var r = await c.PostAsync(url, new FormUrlEncodedContent(campos.Append(new("__RequestVerificationToken", token))));
        return (r, WebUtility.HtmlDecode(await r.Content.ReadAsStringAsync()));
    }

    private static Task<(HttpResponseMessage Respuesta, string Html)> Convertir(AppFactory app, string texto, string nombre = "Derecho", string version = "2022", string universidad = "unapec") =>
        PostAsync(app, "/Carrera/Convertir", new KeyValuePair<string, string>[] { new("texto", texto), new("nombreCarrera", nombre), new("version", version), new("universidad", universidad) });

    /// <summary>Los campos del formulario de la vista previa para unas filas (código, nombre, créditos, cuatrimestre, prerrequisitos).</summary>
    private static List<KeyValuePair<string, string>> Formulario(IEnumerable<(string Codigo, string Nombre, string Creditos, string Cuat, string Prer)> filas,
        string accion, string nombre = "Derecho", string version = "2022", string universidad = "unapec", bool usar = true, bool reemplazar = false)
    {
        var campos = new List<KeyValuePair<string, string>>
        {
            new("nombreCarrera", nombre), new("version", version), new("universidad", universidad), new("accion", accion),
            new("usar", usar ? "true" : "false"), new("reemplazar", reemplazar ? "true" : "false"),
        };
        var i = 0;
        foreach (var f in filas)
        {
            campos.Add(new($"filas[{i}].Linea", "0")); campos.Add(new($"filas[{i}].Codigo", f.Codigo)); campos.Add(new($"filas[{i}].Nombre", f.Nombre));
            campos.Add(new($"filas[{i}].Creditos", f.Creditos)); campos.Add(new($"filas[{i}].Cuatrimestre", f.Cuat)); campos.Add(new($"filas[{i}].Prerrequisitos", f.Prer));
            campos.Add(new($"filas[{i}].Electiva", "false")); campos.Add(new($"filas[{i}].Quitar", "false"));
            i++;
        }
        return campos;
    }

    private static readonly (string, string, string, string, string)[] Buenas =
    {
        ("AAA100", "Introducción al Derecho", "3", "1", ""), ("BBB100", "Lógica Jurídica", "3", "1", ""), ("CCC200", "Derecho Civil", "5", "2", "AAA100; BBB100"),
    };

    // ── El formulario ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LaPantallaDePegadoTieneElFormularioYExplicaElFormato()
    {
        using var app = await NuevaAsync();

        var (estado, html) = await app.GetAsync("/Carrera/Importar");

        Assert.Equal(HttpStatusCode.OK, estado);
        Assert.Contains("Importar un pénsum desde texto", html);
        Assert.Contains("action=\"/Carrera/Convertir\"", html);
        Assert.Contains("name=\"texto\"", html);
        Assert.Contains("name=\"nombreCarrera\"", html);
        Assert.Contains("name=\"version\"", html);
        Assert.Contains("<option value=\"unapec\" selected", html);            // las reglas de la universidad activa
        Assert.Contains("value=\"uni-prueba\"", html);
        Assert.Contains("¿Qué formato entiende?", html);
        Assert.Contains("Cuatrimestre 1", html);                               // el ejemplo
        Assert.Contains("pénsum personal", html);
    }

    [Fact]
    public async Task LaListaDeCarrerasOfreceImportar()
    {
        using var app = await NuevaAsync();

        var (_, html) = await app.GetAsync("/Carrera");

        Assert.Contains("id=\"importar-texto\"", html);
        Assert.Contains("href=\"/Carrera/Importar\"", html);
    }

    // ── Vista previa ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ElTextoPegadoSeConvierteEnUnaTablaEditableConTresFilasEnBlanco()
    {
        using var app = await NuevaAsync();

        var (r, html) = await Convertir(app, TextoPlan);

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Contains("Revisa el pénsum", html);
        Assert.Contains("id=\"tabla-revision\"", html);
        Assert.Contains("name=\"filas[0].Codigo\" value=\"AAA100\"", html);
        Assert.Contains("value=\"Introducción al Derecho\"", html);
        Assert.Contains("name=\"filas[2].Prerrequisitos\" value=\"AAA100; BBB100\"", html);
        Assert.Contains("name=\"filas[3].Codigo\" value=\"\"", html);           // filas en blanco para agregar a mano
        Assert.Contains("name=\"filas[5].Codigo\" value=\"\"", html);
        Assert.DoesNotContain("name=\"filas[6].Codigo\"", html);
        Assert.Contains("id=\"pensum-valido\"", html);
        Assert.Contains("3 materias", html);
        Assert.Contains("11 créditos", html);
        Assert.Contains("2 cuatrimestres", html);
        Assert.Contains("Línea 7: no la entendí como una materia, la salté: «Total de créditos: 11».", html);
        Assert.Matches("value=\"Derecho\"", html);
    }

    [Fact]
    public async Task SinTextoOSinMateriasVuelveAlFormularioConUnMensaje()
    {
        using var app = await NuevaAsync();

        var (_, vacio) = await Convertir(app, "   ");
        var (_, basura) = await Convertir(app, "Hola mundo, esto no es un plan");

        Assert.Contains("No pegaste ningún texto.", vacio);
        Assert.Contains("id=\"form-importar\"", vacio);
        Assert.Contains("No encontré ninguna materia", basura);
        Assert.Contains("Hola mundo, esto no es un plan", basura);              // el texto pegado no se pierde
    }

    [Fact]
    public async Task UnaFilaConProblemasSeMarcaYElBotonDeGuardarQuedaBloqueado()
    {
        using var app = await NuevaAsync();
        var filas = Buenas.Concat(new[] { ("DDD300", "Rara", "tres", "2", "ZZZ999") });

        var (_, html) = await PostAsync(app, "/Carrera/Revisar", Formulario(filas, "revisar"));

        Assert.Contains("id=\"pensum-con-problemas\"", html);
        Assert.Contains("class=\"fila-con-problema\" data-fila=\"3\"", html);
        Assert.Contains("Los créditos «tres» no son un número entero.", html);
        Assert.DoesNotContain("id=\"pensum-valido\"", html);
        Assert.Matches("<button type=\"submit\" name=\"accion\" value=\"guardar\" class=\"btn btn-primary\" disabled", html);
        Assert.Contains("id=\"guardar-bloqueado\"", html);
    }

    [Fact]
    public async Task UnPrerrequisitoQueNoExisteSeSeñalaEnSuFila()
    {
        using var app = await NuevaAsync();
        var filas = Buenas.Concat(new[] { ("DDD300", "Otra", "3", "3", "ZZZ999") });

        var (_, html) = await PostAsync(app, "/Carrera/Revisar", Formulario(filas, "revisar"));

        Assert.Contains("class=\"fila-problema\" data-problema-de=\"3\"", html);
        Assert.Contains("ZZZ999 no existe en el pénsum", html);
    }

    [Fact]
    public async Task AgregarFilasDejaTresMasEnBlancoYConservaLoEditado()
    {
        using var app = await NuevaAsync();

        var (_, html) = await PostAsync(app, "/Carrera/Revisar", Formulario(Buenas, "agregar"));

        Assert.Contains("name=\"filas[5].Codigo\" value=\"\"", html);
        Assert.Contains("name=\"filas[0].Codigo\" value=\"AAA100\"", html);
    }

    [Fact]
    public async Task LaFilaQuitadaNoCuentaEnElPensum()
    {
        using var app = await NuevaAsync();
        var campos = Formulario(Buenas, "revisar");
        var iq = campos.FindIndex(c => c.Key == "filas[1].Quitar");
        campos[iq] = new("filas[1].Quitar", "true");                        // el navegador envía «true» (casilla marcada) y luego el «false» oculto
        campos.Insert(iq + 1, new("filas[1].Quitar", "false"));
        campos.RemoveAll(c => c.Key == "filas[2].Prerrequisitos"); campos.Add(new("filas[2].Prerrequisitos", "AAA100"));

        var (_, html) = await PostAsync(app, "/Carrera/Revisar", campos);

        Assert.Contains("class=\"fila-quitada\" data-fila=\"1\"", html);
        Assert.Contains("id=\"pensum-valido\"", html);
        Assert.Contains("2 materias", html);
    }

    [Fact]
    public async Task UnPensumGrandeCabeEnElFormulario()
    {
        using var app = await NuevaAsync();
        var filas = Enumerable.Range(1, 250).Select(i => ($"M{i:000}", $"Materia {i}", "1", "1", ""));

        var (r, html) = await PostAsync(app, "/Carrera/Revisar", Formulario(filas, "revisar"));

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);                          // 250 filas × 8 campos superan los 1024 valores por omisión
        Assert.Contains("250 materias", html);
        Assert.Contains("id=\"pensum-valido\"", html);
    }

    // ── Guardar ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GuardarCreaElArchivoLoPoneEnUsoYApareceComoPersonal()
    {
        using var app = await NuevaAsync();

        var (respuesta, html) = await PostAsync(app, "/Carrera/Revisar", Formulario(Buenas, "guardar"));

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);                  // tras la redirección a Carrera y pénsum
        Assert.Contains("Pénsum personal guardado: Derecho (plan 2022), 3 materias, 11 créditos.", html);
        Assert.Contains("Pénsum activo: Derecho (plan 2022)", html);
        Assert.Contains("<span class=\"badge bg-secondary ms-2\"", html);
        Assert.Contains(">Personal</span>", html);
        Assert.Contains("<span class=\"badge bg-success\">En uso</span>", html);

        var ruta = Path.Combine(app.Carpeta, "unapec", "personal-derecho-2022.json");
        Assert.True(File.Exists(ruta));
        var leido = PensumJson.Leer(File.ReadAllText(ruta), ruta);
        Assert.True(leido.EsValido, string.Join(" | ", leido.Errores));
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HistorialContext>();
        Assert.Equal("unapec/personal-derecho-2022.json", (await db.PensumActivo.AsNoTracking().SingleAsync()).Clave);
        Assert.Equal(3, await db.MateriasPensum.CountAsync());
    }

    [Fact]
    public async Task ElPensumGuardadoSinUsarSoloApareceEnLaLista()
    {
        using var app = await NuevaAsync();

        var (_, html) = await PostAsync(app, "/Carrera/Revisar", Formulario(Buenas, "guardar", usar: false));

        Assert.Contains("Ya está en la lista de «Carrera y pénsum».", html);
        Assert.DoesNotContain("<span class=\"badge bg-success\">En uso</span>", html);
        Assert.Contains(">Personal</span>", html);
        Assert.Contains("action=\"/Carrera/EliminarPersonal\"", html);          // y se puede borrar
    }

    [Fact]
    public async Task GuardarConProblemasVuelveALaVistaPreviaConElMensaje()
    {
        using var app = await NuevaAsync();
        var filas = Buenas.Concat(new[] { ("", "", "", "", "x") });

        var (_, html) = await PostAsync(app, "/Carrera/Revisar", Formulario(filas, "guardar"));

        Assert.Contains("id=\"error-guardar\"", html);
        Assert.Contains("El pénsum tiene problemas", html);
        Assert.False(File.Exists(Path.Combine(app.Carpeta, "unapec", "personal-derecho-2022.json")));
    }

    [Fact]
    public async Task UnPensumPersonalQueYaExisteExigeReemplazarlo()
    {
        using var app = await NuevaAsync();
        await PostAsync(app, "/Carrera/Revisar", Formulario(Buenas, "guardar", usar: false));

        var (_, sin) = await PostAsync(app, "/Carrera/Revisar", Formulario(Buenas, "guardar", usar: false));
        var (_, con) = await PostAsync(app, "/Carrera/Revisar", Formulario(Buenas, "guardar", usar: false, reemplazar: true));

        Assert.Contains("Ya tienes un pénsum personal «Derecho» con la versión 2022", sin);
        Assert.Contains("id=\"ya-existe\"", sin);
        Assert.Contains("Pénsum personal guardado", con);
    }

    [Fact]
    public async Task UnNombreConRutasNoEscribeFueraDeLaCarpeta()
    {
        using var app = await NuevaAsync();
        var fuera = Path.Combine(Path.GetDirectoryName(app.Carpeta)!, "escapado.json");

        await PostAsync(app, "/Carrera/Revisar", Formulario(Buenas, "guardar", nombre: "../escapado", usar: false));

        Assert.False(File.Exists(fuera));
        Assert.True(File.Exists(Path.Combine(app.Carpeta, "unapec", "personal-escapado-2022.json")));
    }

    [Fact]
    public async Task UnaUniversidadInventadaNoGuardaNada()
    {
        using var app = await NuevaAsync();

        var (_, html) = await PostAsync(app, "/Carrera/Revisar", Formulario(Buenas, "guardar", universidad: "../otra"));

        Assert.Contains("Elige la universidad", html);
        Assert.Empty(Directory.GetFiles(app.Carpeta, "personal-*", SearchOption.AllDirectories));
    }

    // ── Borrar ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task BorrarUnPensumPersonalQuitaElArchivoYLaFila()
    {
        using var app = await NuevaAsync();
        await PostAsync(app, "/Carrera/Revisar", Formulario(Buenas, "guardar", usar: false));

        var (_, html) = await PostAsync(app, "/Carrera/EliminarPersonal", new KeyValuePair<string, string>[] { new("clave", "unapec/personal-derecho-2022.json") });

        Assert.Contains("Pénsum personal «Derecho» (plan 2022) borrado.", html);
        Assert.False(File.Exists(Path.Combine(app.Carpeta, "unapec", "personal-derecho-2022.json")));
        Assert.DoesNotContain(">Personal</span>", html);
    }

    [Fact]
    public async Task BorrarUnPensumDelCatalogoCompartidoSeRechaza()
    {
        using var app = await NuevaAsync();

        var (_, html) = await PostAsync(app, "/Carrera/EliminarPersonal", new KeyValuePair<string, string>[] { new("clave", "unapec/ingenieria-software-11.json") });

        Assert.Contains("Solo se pueden borrar los pénsums personales", html);
        Assert.True(File.Exists(Path.Combine(app.Carpeta, "unapec", "ingenieria-software-11.json")));
    }

    // ── Seguridad ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("/Carrera/Convertir")]
    [InlineData("/Carrera/Revisar")]
    [InlineData("/Carrera/EliminarPersonal")]
    public async Task LasAccionesQueCambianDatosExigenElTokenAntifalsificacion(string url)
    {
        using var app = await NuevaAsync();
        using var c = app.CreateClient();

        var r = await c.PostAsync(url, new FormUrlEncodedContent(new Dictionary<string, string> { ["texto"] = TextoPlan, ["clave"] = "x" }));

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task ElTextoYLosNombresSeEscapanEnLaVistaPrevia()
    {
        using var app = await NuevaAsync();
        var texto = "Código\tAsignatura\tCréditos\nAAA100\t<script>alert(1)</script> \"comillas\"\t3";

        using var c = app.CreateClient();
        var pagina = await c.GetStringAsync("/Carrera/Importar");
        var token = Regex.Match(pagina, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        var r = await c.PostAsync("/Carrera/Convertir", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["texto"] = texto, ["nombreCarrera"] = "<img src=x onerror=alert(1)>", ["version"] = "1", ["universidad"] = "unapec", ["__RequestVerificationToken"] = token,
        }));
        var crudo = await r.Content.ReadAsStringAsync();

        Assert.DoesNotContain("<script>alert(1)", crudo);
        Assert.DoesNotContain("<img src=x", crudo);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", crudo);
        Assert.Contains("&lt;img src=x onerror=alert(1)&gt;", crudo);
    }
}
