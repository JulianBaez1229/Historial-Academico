using HistorialAcademico.Core.Pensum;
using HistorialAcademico.Validador;

namespace HistorialAcademico.Tests;

/// <summary>La herramienta validador-pensums: la misma comprobación que hace la GitHub Action y que puede correr quien agrega un pénsum.</summary>
public class ValidadorPensumsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "validador-" + Guid.NewGuid().ToString("N"));
    private readonly StringWriter _salida = new();
    private readonly StringWriter _error = new();

    public ValidadorPensumsTests() => Directory.CreateDirectory(_dir);

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private int Ejecutar(params string[] args) => Comandos.Ejecutar(args, _salida, _error, _dir);

    private string Salida => _salida.ToString();
    private string Errores => _error.ToString();

    private void Copiar(string universidad, string archivo, string? contenido = null)
    {
        Directory.CreateDirectory(Path.Combine(_dir, "pensums", universidad));
        File.WriteAllText(Path.Combine(_dir, "pensums", universidad, archivo), contenido ?? File.ReadAllText(Path.Combine(PensumEjemplo.CarpetaPensums, universidad, archivo)));
    }

    private const string CsvMinimo = "codigo,nombre,creditos,cuatrimestre,prerrequisitos,es_electiva\nAAA100,Primera,3,1,,false\nBBB200,Segunda,5,2,AAA100,false\n";

    // ── Uso ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SinArgumentosMuestraLaAyudaYAvisaDelUsoIncorrecto()
    {
        Assert.Equal(Comandos.UsoIncorrecto, Ejecutar());
        Assert.Contains("validador-pensums validar", Errores);
        Assert.Contains("validador-pensums convertir", Errores);
    }

    [Theory]
    [InlineData("-h")]
    [InlineData("--help")]
    [InlineData("ayuda")]
    public void PedirAyudaLaMuestraYSaleBien(string pedido)
    {
        Assert.Equal(Comandos.Bien, Ejecutar(pedido));
        Assert.Contains("Códigos de salida", Salida);
        Assert.Empty(Errores);
    }

    [Fact]
    public void UnComandoDesconocidoEsUsoIncorrecto()
    {
        Assert.Equal(Comandos.UsoIncorrecto, Ejecutar("borrar"));
        Assert.Contains("No conozco el comando «borrar»", Errores);
    }

    // ── validar ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ValidarLaCarpetaDelRepositorioSaleBienYResumeLoQueEncontro()
    {
        var codigo = Ejecutar("validar", PensumEjemplo.CarpetaPensums);

        Assert.Equal(Comandos.Bien, codigo);
        Assert.Contains("OK      unapec/universidad.json  (UNAPEC – Universidad APEC)", Salida);
        Assert.Contains("OK      unapec/ingenieria-software-11.json  (Ingeniería de Software: 75 materias, 218 créditos, 12 cuatrimestres)", Salida);
        Assert.Contains("Todo en orden: 1 pénsum y 1 universidad válidos.", Salida);
        Assert.Empty(Errores);
    }

    [Fact]
    public void PorOmisionValidaLaCarpetaPensumsDelDirectorioActual()
    {
        Copiar("unapec", "universidad.json");
        Copiar("unapec", "ingenieria-software-11.json");

        Assert.Equal(Comandos.Bien, Ejecutar("validar"));
        Assert.Contains("Todo en orden", Salida);
    }

    [Fact]
    public void UnArchivoConErroresHaceQueSalgaConUno_ConElMensajeYElArchivo()
    {
        Copiar("unapec", "universidad.json");
        Copiar("unapec", "ingenieria-software-11.json");
        Copiar("unapec", "derecho-1.json", CatalogoTemporal.Pensum("derecho", "Derecho", "1", ("AAA100", 3, 1, "ZZZ999")).Replace("uni-prueba", "unapec"));

        var codigo = Ejecutar("validar");

        Assert.Equal(Comandos.ConErrores, codigo);
        Assert.Contains("ERROR   unapec/derecho-1.json", Salida);
        Assert.Contains("  - AAA100: su prerrequisito ZZZ999 no existe en el pénsum.", Salida);
        Assert.Contains("OK      unapec/ingenieria-software-11.json", Salida);       // los buenos también se listan
        Assert.Contains("1 archivos con errores (2 válidos). Corrige los errores marcados y vuelve a validar.", Salida);
    }

    [Fact]
    public void UnJsonRotoYUnaUniversidadSinReglasSeInformanSinCaerse()
    {
        Copiar("unapec", "ingenieria-software-11.json");                              // no hay universidad.json
        Copiar("otra", "x-1.json", "{ no es json");

        var codigo = Ejecutar("validar");

        Assert.Equal(Comandos.ConErrores, codigo);
        Assert.Contains("ERROR   otra/x-1.json", Salida);
        Assert.Contains("no es un JSON válido", Salida);
        Assert.Contains("La universidad «unapec» no tiene un universidad.json válido", Salida);
    }

    [Fact]
    public void LosAvisosSeMuestranPeroNoHacenFallar()
    {
        Copiar("unapec", "universidad.json");
        Copiar("unapec", "adelantada-1.json", """{ "formato": 1, "universidad": "unapec", "carrera": "adelantada", "nombreCarrera": "A", "version": "1", "totalCreditos": 2, "cuatrimestres": 2, "materias": [ { "codigo": "AA1", "nombre": "A", "creditos": 1, "cuatrimestre": 1, "prerrequisitos": ["BB2"] }, { "codigo": "BB2", "nombre": "B", "creditos": 1, "cuatrimestre": 2 } ] }""");

        Assert.Equal(Comandos.Bien, Ejecutar("validar"));
        Assert.Contains("AVISO", Salida);
        Assert.Contains("cuatrimestre posterior", Salida);
    }

    [Fact]
    public void UnaCarpetaQueNoExisteOVaciaSaleConUno()
    {
        Assert.Equal(Comandos.ConErrores, Ejecutar("validar", "no-existe"));
        Assert.Contains("No existe la carpeta", Errores);

        Directory.CreateDirectory(Path.Combine(_dir, "vacia"));
        var otra = new StringWriter();
        Assert.Equal(Comandos.ConErrores, Comandos.Ejecutar(new[] { "validar", "vacia" }, _salida, otra, _dir));
        Assert.Contains("No encontré ningún pénsum ni universidad", otra.ToString());
    }

    [Fact]
    public void ValidarConDemasiadosArgumentosEsUsoIncorrecto() => Assert.Equal(Comandos.UsoIncorrecto, Ejecutar("validar", "a", "b"));

    // ── convertir ─────────────────────────────────────────────────────────────────────────

    private string Csv() { var ruta = Path.Combine(_dir, "plan.csv"); File.WriteAllText(ruta, CsvMinimo); return ruta; }

    private string[] Opciones(params string[] extra) =>
        new[] { "convertir", Csv(), "--universidad", "uni-prueba", "--carrera", "derecho", "--nombre", "Derecho", "--version", "2022" }.Concat(extra).ToArray();

    [Fact]
    public void ConvertirCreaElArchivoEnLaCarpetaDeLaUniversidadYSeVeValido()
    {
        Copiar("uni-prueba", "universidad.json", UniversidadDePrueba.Json);

        var codigo = Ejecutar(Opciones());

        Assert.Equal(Comandos.Bien, codigo);
        var ruta = Path.Combine(_dir, "pensums", "uni-prueba", "derecho-2022.json");
        Assert.True(File.Exists(ruta));
        Assert.Contains("2 materias, 8 créditos, 2 cuatrimestres.", Salida);
        Assert.Contains("agrega a mano lo que el CSV no trae", Salida);
        Assert.DoesNotContain("Falta uni-prueba/universidad.json", Salida);
        var leido = PensumJson.Leer(File.ReadAllText(ruta), ruta);
        Assert.True(leido.EsValido, string.Join(" | ", leido.Errores));
        Assert.Equal("Derecho", leido.Definicion!.NombreCarrera);

        // Y la validación de la carpeta lo acepta.
        var otra = new StringWriter();
        Assert.Equal(Comandos.Bien, Comandos.Ejecutar(new[] { "validar" }, otra, _error, _dir));
        Assert.Contains("OK      uni-prueba/derecho-2022.json", otra.ToString());
    }

    [Fact]
    public void ConvertirAvisaSiFaltaElArchivoDeLaUniversidad()
    {
        var codigo = Ejecutar(Opciones());

        Assert.Equal(Comandos.Bien, codigo);
        Assert.Contains("AVISO Falta uni-prueba/universidad.json", Salida);
    }

    [Fact]
    public void ConvertirNoPisaUnArchivoQueYaExisteSalvoConForzar()
    {
        Ejecutar(Opciones());
        var ruta = Path.Combine(_dir, "pensums", "uni-prueba", "derecho-2022.json");
        File.WriteAllText(ruta, "cambio a mano");

        Assert.Equal(Comandos.ConErrores, Ejecutar(Opciones()));
        Assert.Contains("Ya existe", Errores);
        Assert.Equal("cambio a mano", File.ReadAllText(ruta));

        Assert.Equal(Comandos.Bien, Ejecutar(Opciones("--forzar")));
        Assert.StartsWith("{", File.ReadAllText(ruta));
    }

    [Fact]
    public void ConSalidaGuionEscribeElJsonEnPantallaSinCrearArchivos()
    {
        var codigo = Ejecutar(Opciones("--salida", "-"));

        Assert.Equal(Comandos.Bien, codigo);
        Assert.StartsWith("{\n  \"formato\": 1,", Salida);
        Assert.False(Directory.Exists(Path.Combine(_dir, "pensums")));
        Assert.True(PensumJson.Leer(Salida).EsValido);
    }

    [Fact]
    public void ConSalidaIndicadaLoGuardaAhi()
    {
        var codigo = Ejecutar(Opciones("--salida", "carpeta/mi-plan.json"));

        Assert.Equal(Comandos.Bien, codigo);
        Assert.True(File.Exists(Path.Combine(_dir, "carpeta", "mi-plan.json")));
    }

    [Fact]
    public void ConvertirElCsvDeUnapecDaElMismoPensumQueElDelCatalogoSinLosExtras()
    {
        var codigo = Ejecutar("convertir", Path.Combine(PensumEjemplo.Raiz, "docs", "pensum_iso_unapec.csv"), "--universidad", "unapec", "--carrera", "ingenieria-software",
            "--nombre", "Ingeniería de Software", "--version", "11", "--salida", "-");

        Assert.Equal(Comandos.Bien, codigo);
        var convertido = PensumJson.Leer(Salida).Definicion!;
        var catalogo = PensumJson.Leer(File.ReadAllText(PensumEjemplo.Archivo)).Definicion!;
        Assert.Equal(catalogo.Materias.Select(m => (m.Codigo, m.Creditos, m.Cuatrimestre, string.Join("|", m.Prerrequisitos))),
                     convertido.Materias.Select(m => (m.Codigo, m.Creditos, m.Cuatrimestre, string.Join("|", m.Prerrequisitos))));
        Assert.Empty(convertido.BloquesElectivas);          // esos extras se agregan a mano
        Assert.Equal(218, convertido.TotalCreditos);
    }

    [Theory]
    [InlineData("--universidad")]
    [InlineData("--carrera")]
    [InlineData("--nombre")]
    [InlineData("--version")]
    public void FaltaUnaOpcionObligatoriaEsUsoIncorrecto(string sinEsta)
    {
        var args = Opciones().ToList();
        var i = args.IndexOf(sinEsta);
        args.RemoveRange(i, 2);

        Assert.Equal(Comandos.UsoIncorrecto, Comandos.Ejecutar(args.ToArray(), _salida, _error, _dir));
        Assert.Contains($"Falta la opción {sinEsta}", Errores);
    }

    [Fact]
    public void OpcionesRarasSonUsoIncorrecto()
    {
        Assert.Equal(Comandos.UsoIncorrecto, Ejecutar(Opciones("--colores", "si")));
        Assert.Contains("No conozco la opción --colores", Errores);

        Assert.Equal(Comandos.UsoIncorrecto, Ejecutar("convertir", Csv(), "--universidad"));
        Assert.Equal(Comandos.UsoIncorrecto, Ejecutar("convertir"));
        Assert.Equal(Comandos.UsoIncorrecto, Ejecutar("convertir", "a.csv", "b.csv", "--universidad", "u", "--carrera", "c", "--nombre", "N", "--version", "1"));
    }

    [Fact]
    public void ElCsvQueNoExisteOTieneErroresNoSeConvierte()
    {
        var args = Opciones().ToArray();
        args[1] = Path.Combine(_dir, "no-esta.csv");
        Assert.Equal(Comandos.ConErrores, Ejecutar(args));
        Assert.Contains("No existe el archivo", Errores);

        var roto = Path.Combine(_dir, "roto.csv");
        File.WriteAllText(roto, "codigo,nombre\nX,Y\n");
        args[1] = roto;
        Assert.Equal(Comandos.ConErrores, Ejecutar(args));
        Assert.Contains("No se pudo convertir", Errores);
        Assert.Contains("Encabezado inesperado", Errores);
        Assert.False(Directory.Exists(Path.Combine(_dir, "pensums")));
    }

    [Fact]
    public void UnosMetadatosInvalidosNoGeneranNada()
    {
        var args = Opciones().ToArray();
        args[args.ToList().IndexOf("--universidad") + 1] = "Uni Prueba";

        Assert.Equal(Comandos.ConErrores, Ejecutar(args));
        Assert.Contains("universidad", Errores);
        Assert.False(Directory.Exists(Path.Combine(_dir, "pensums")));
    }
}
