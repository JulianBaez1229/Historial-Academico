using HistorialAcademico.Core.Perfiles;
using Xunit;

namespace HistorialAcademico.Tests;

/// <summary>El PIN nunca se guarda en texto plano y se comprueba sin pistas.</summary>
public class PinHasherTests
{
    private const int Rapido = 1000;

    [Theory]
    [InlineData("", "Escribe un PIN")]
    [InlineData("123", "al menos 4")]
    [InlineData(" 1234", "espacios")]
    [InlineData("1234 ", "espacios")]
    public void RechazaPinsQueNoSirven(string pin, string parteDelMensaje) =>
        Assert.Contains(parteDelMensaje, PinHasher.ErrorDePin(pin));

    [Fact]
    public void RechazaUnPinMuyLargoYAceptaLosLimites()
    {
        Assert.Contains("máximo 64", PinHasher.ErrorDePin(new string('9', 65)));
        Assert.Null(PinHasher.ErrorDePin("1234"));
        Assert.Null(PinHasher.ErrorDePin(new string('9', 64)));
        Assert.Null(PinHasher.ErrorDePin("mi clave con espacios"));
        Assert.NotNull(PinHasher.ErrorDePin(null));
    }

    [Fact]
    public void ElHashNoContieneElPinYLlevaSuFormato()
    {
        var hash = PinHasher.Hash("mi-pin-secreto", Rapido);

        Assert.DoesNotContain("mi-pin-secreto", hash);
        var partes = hash.Split('$');
        Assert.Equal(new[] { "pbkdf2-sha256", "1000" }, partes.Take(2));
        Assert.Equal(16, Convert.FromBase64String(partes[2]).Length);   // sal
        Assert.Equal(32, Convert.FromBase64String(partes[3]).Length);   // hash
    }

    [Fact]
    public void ElMismoPinDaHashesDistintosPorLaSal()
    {
        Assert.NotEqual(PinHasher.Hash("1234", Rapido), PinHasher.Hash("1234", Rapido));
    }

    [Fact]
    public void VerificaElPinCorrectoYRechazaElIncorrecto()
    {
        var hash = PinHasher.Hash("clave-1234", Rapido);

        Assert.True(PinHasher.Verificar("clave-1234", hash));
        Assert.False(PinHasher.Verificar("clave-1235", hash));
        Assert.False(PinHasher.Verificar("CLAVE-1234", hash));
        Assert.False(PinHasher.Verificar("", hash));
        Assert.False(PinHasher.Verificar(null, hash));
    }

    [Fact]
    public void LasIteracionesQuedanDentroDelHashYAsiSePuedenSubirDespues()
    {
        var viejo = PinHasher.Hash("1234", 1000);
        var nuevo = PinHasher.Hash("1234", 2000);

        Assert.True(PinHasher.Verificar("1234", viejo));
        Assert.True(PinHasher.Verificar("1234", nuevo));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1234")]
    [InlineData("pbkdf2-sha256$1000$sal")]
    [InlineData("otro$1000$AAAA$AAAA")]
    [InlineData("pbkdf2-sha256$mil$AAAA$AAAA")]
    [InlineData("pbkdf2-sha256$0$AAAA$AAAA")]
    [InlineData("pbkdf2-sha256$1000$no-es-base64!$AAAA")]
    public void UnTextoGuardadoQueNoSeEntiendeNuncaCoincide(string? guardado) =>
        Assert.False(PinHasher.Verificar("1234", guardado));

    [Fact]
    public void ElPredeterminadoUsaLasIteracionesRecomendadas() =>
        Assert.True(PinHasher.IteracionesPredeterminadas >= 600_000);
}

/// <summary>Cuánto se hace esperar tras varios PIN incorrectos seguidos.</summary>
public class PoliticaBloqueoTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4)]
    public void HastaCuatroFallosNoHayEspera(int fallos) => Assert.Equal(TimeSpan.Zero, PoliticaBloqueo.Espera(fallos));

    [Theory]
    [InlineData(5, 30)]
    [InlineData(6, 60)]
    [InlineData(7, 120)]
    [InlineData(8, 240)]
    [InlineData(9, 480)]
    public void DesdeElQuintoFalloLaEsperaSeDuplica(int fallos, int segundos) =>
        Assert.Equal(TimeSpan.FromSeconds(segundos), PoliticaBloqueo.Espera(fallos));

    [Theory]
    [InlineData(10)]
    [InlineData(50)]
    [InlineData(int.MaxValue)]
    public void LaEsperaNuncaPasaDeQuinceMinutos(int fallos) => Assert.Equal(TimeSpan.FromMinutes(15), PoliticaBloqueo.Espera(fallos));
}

/// <summary>La lista de perfiles del equipo: creación, PIN con espera tras fallos, archivo dañado y borrado.</summary>
public sealed class AlmacenPerfilesTests : IDisposable
{
    private readonly string _raiz = Path.Combine(Path.GetTempPath(), "almacen-" + Guid.NewGuid().ToString("N"));
    private DateTime _ahora = new(2027, 3, 1, 10, 0, 0, DateTimeKind.Utc);

    private AlmacenPerfiles Nuevo() => new(_raiz, () => _ahora, iteracionesPin: 1000);

    public void Dispose()
    {
        try { Directory.Delete(_raiz, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void SinArchivoNoHayPerfiles()
    {
        var almacen = Nuevo();

        Assert.Empty(almacen.Listar());
        Assert.Null(almacen.Obtener("abc"));
        Assert.Null(almacen.Obtener(null));
        Assert.False(almacen.DatosAnterioresAdoptados);
    }

    [Fact]
    public void CreaUnPerfilConSuCarpetaYLoGuarda()
    {
        var almacen = Nuevo();

        var (perfil, error) = almacen.Crear("  Ana  ", null);

        Assert.Null(error);
        Assert.NotNull(perfil);
        Assert.Equal("Ana", perfil!.Nombre);
        Assert.Matches("^[0-9a-f]{8}$", perfil.Id);
        Assert.False(perfil.TienePin);
        Assert.True(Directory.Exists(almacen.CarpetaDe(perfil.Id)));
        Assert.Equal(Path.Combine(_raiz, "perfiles", perfil.Id, "historial.db"), almacen.RutaBase(perfil.Id));
        Assert.Equal("Ana", Nuevo().Obtener(perfil.Id)!.Nombre);   // otro almacén lee el mismo archivo
    }

    [Fact]
    public void ElPinNuncaSeGuardaEnTextoPlano()
    {
        var almacen = Nuevo();

        var (perfil, _) = almacen.Crear("Ana", "mi-pin-9876");

        Assert.True(perfil!.TienePin);
        var archivo = File.ReadAllText(almacen.RutaArchivo);
        Assert.DoesNotContain("mi-pin-9876", archivo);
        Assert.Contains("pbkdf2-sha256", archivo);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void UnNombreVacioNoSirve(string? nombre)
    {
        var (perfil, error) = Nuevo().Crear(nombre, null);

        Assert.Null(perfil);
        Assert.Equal("Escribe tu nombre.", error);
    }

    [Fact]
    public void ElNombreTieneLimiteYNoAdmiteCaracteresDeControl()
    {
        var almacen = Nuevo();

        Assert.Contains("40", almacen.Crear(new string('a', 41), null).Error);
        Assert.NotNull(almacen.Crear(new string('a', 40), null).Perfil);
        Assert.Contains("caracteres que no se pueden usar", almacen.Crear("Ana\u0007", null).Error);
    }

    [Fact]
    public void NoSePermitenDosPerfilesConElMismoNombreSinImportarMayusculas()
    {
        var almacen = Nuevo();
        almacen.Crear("Ana", null);

        var (perfil, error) = almacen.Crear("ANA", null);

        Assert.Null(perfil);
        Assert.Contains("Ya hay un perfil llamado", error);
        Assert.Single(almacen.Listar());
    }

    [Fact]
    public void UnPinInvalidoNoCreaElPerfil()
    {
        var almacen = Nuevo();

        var (perfil, error) = almacen.Crear("Ana", "12");

        Assert.Null(perfil);
        Assert.Contains("al menos 4", error);
        Assert.Empty(almacen.Listar());
        Assert.False(Directory.Exists(Path.Combine(_raiz, "perfiles")));
    }

    [Fact]
    public void HayUnMaximoDePerfiles()
    {
        var almacen = Nuevo();
        for (var i = 0; i < AlmacenPerfiles.MaxPerfiles; i++) Assert.NotNull(almacen.Crear($"Perfil {i}", null).Perfil);

        var (perfil, error) = almacen.Crear("Uno más", null);

        Assert.Null(perfil);
        Assert.Contains("máximo", error);
    }

    [Fact]
    public void UnPerfilSinPinEntraSiempre()
    {
        var almacen = Nuevo();
        var id = almacen.Crear("Ana", null).Perfil!.Id;

        Assert.Equal(ResultadoEntrada.Correcto, almacen.Intentar(id, null).Resultado);
        Assert.Equal(ResultadoEntrada.Correcto, almacen.Intentar(id, "lo-que-sea").Resultado);
        Assert.Equal(ResultadoEntrada.NoExiste, almacen.Intentar("no-existe", null).Resultado);
        Assert.Equal(ResultadoEntrada.NoExiste, almacen.Intentar(null, "1234").Resultado);
    }

    [Fact]
    public void ElPinCorrectoEntraYElIncorrectoNo()
    {
        var almacen = Nuevo();
        var id = almacen.Crear("Ana", "1234").Perfil!.Id;

        Assert.Equal(ResultadoEntrada.PinIncorrecto, almacen.Intentar(id, "0000").Resultado);
        Assert.Equal(ResultadoEntrada.PinIncorrecto, almacen.Intentar(id, null).Resultado);
        Assert.Equal(ResultadoEntrada.Correcto, almacen.Intentar(id, "1234").Resultado);
    }

    [Fact]
    public void DespuesDeCuatroFallosSeguidosHayQueEsperarYLaEsperaCrece()
    {
        var almacen = Nuevo();
        var id = almacen.Crear("Ana", "1234").Perfil!.Id;

        for (var i = 1; i <= 4; i++)
        {
            var intento = almacen.Intentar(id, "mal");
            Assert.Equal((ResultadoEntrada.PinIncorrecto, TimeSpan.Zero, i), (intento.Resultado, intento.Espera, intento.FallosSeguidos));
        }

        var quinto = almacen.Intentar(id, "mal");
        Assert.Equal(ResultadoEntrada.PinIncorrecto, quinto.Resultado);
        Assert.Equal(TimeSpan.FromSeconds(30), quinto.Espera);

        // Durante la espera ni siquiera el PIN correcto entra, y no se cuenta como otro fallo.
        var durante = almacen.Intentar(id, "1234");
        Assert.Equal(ResultadoEntrada.Bloqueado, durante.Resultado);
        Assert.Equal(TimeSpan.FromSeconds(30), durante.Espera);
        Assert.Equal(5, Nuevo().Obtener(id)!.Fallos);

        _ahora += TimeSpan.FromSeconds(31);
        var sexto = almacen.Intentar(id, "mal");
        Assert.Equal(TimeSpan.FromSeconds(60), sexto.Espera);
    }

    [Fact]
    public void PasadaLaEsperaElPinCorrectoEntraYLaCuentaVuelveACero()
    {
        var almacen = Nuevo();
        var id = almacen.Crear("Ana", "1234").Perfil!.Id;
        for (var i = 0; i < 5; i++) almacen.Intentar(id, "mal");

        _ahora += TimeSpan.FromSeconds(31);

        Assert.Equal(ResultadoEntrada.Correcto, almacen.Intentar(id, "1234").Resultado);
        var perfil = Nuevo().Obtener(id)!;
        Assert.Equal(0, perfil.Fallos);
        Assert.Null(perfil.BloqueadoHasta);
    }

    [Fact]
    public void UnAciertoAntesDelLimiteReiniciaLosFallos()
    {
        var almacen = Nuevo();
        var id = almacen.Crear("Ana", "1234").Perfil!.Id;
        for (var i = 0; i < 3; i++) almacen.Intentar(id, "mal");

        Assert.Equal(ResultadoEntrada.Correcto, almacen.Intentar(id, "1234").Resultado);

        Assert.Equal(1, almacen.Intentar(id, "mal").FallosSeguidos);
    }

    [Fact]
    public void LaEsperaSobreviveAReabrirLaAplicacion()
    {
        var id = Nuevo().Crear("Ana", "1234").Perfil!.Id;
        for (var i = 0; i < 5; i++) Nuevo().Intentar(id, "mal");   // cada intento con un almacén nuevo, como tras reiniciar

        var intento = Nuevo().Intentar(id, "1234");

        Assert.Equal(ResultadoEntrada.Bloqueado, intento.Resultado);
    }

    [Fact]
    public void GuardaLaUniversidadDelPerfil()
    {
        var almacen = Nuevo();
        var id = almacen.Crear("Ana", null).Perfil!.Id;

        almacen.GuardarUniversidad(id, "unapec");
        almacen.GuardarUniversidad("no-existe", "otra");   // no hace nada ni falla

        Assert.Equal("unapec", Nuevo().Obtener(id)!.UniversidadId);
    }

    [Fact]
    public void RecuerdaQueLosDatosAnterioresYaSeOfrecieron()
    {
        var almacen = Nuevo();

        almacen.MarcarDatosAnterioresAdoptados();

        Assert.True(Nuevo().DatosAnterioresAdoptados);
    }

    [Fact]
    public void UnArchivoDanadoSeApartaSinBorrarnadaYSeEmpiezaDeCero()
    {
        Directory.CreateDirectory(_raiz);
        File.WriteAllText(Path.Combine(_raiz, "perfiles.json"), "{ esto no es json");
        var almacen = Nuevo();

        var perfiles = almacen.Listar();

        Assert.Empty(perfiles);
        Assert.Contains("estaba dañado", almacen.AdvertenciaAlCargar);
        var copia = Assert.Single(Directory.GetFiles(_raiz, "perfiles.json.danado-*"));
        Assert.Equal("{ esto no es json", File.ReadAllText(copia));
        Assert.NotNull(almacen.Crear("Ana", null).Perfil);   // se puede seguir usando
    }

    [Fact]
    public void BorrarQuitaElPerfilYTodaSuCarpeta()
    {
        var almacen = Nuevo();
        var ana = almacen.Crear("Ana", null).Perfil!;
        var beto = almacen.Crear("Beto", null).Perfil!;
        File.WriteAllText(almacen.RutaBase(ana.Id), "datos");

        Assert.True(almacen.Borrar(ana.Id));

        Assert.False(Directory.Exists(almacen.CarpetaDe(ana.Id)));
        Assert.True(Directory.Exists(almacen.CarpetaDe(beto.Id)));
        Assert.Equal("Beto", Assert.Single(almacen.Listar()).Nombre);
        Assert.False(almacen.Borrar(ana.Id));
    }

    [Fact]
    public void LaListaSaleEnElOrdenDeCreacion()
    {
        var almacen = Nuevo();
        almacen.Crear("Zoe", null);
        _ahora += TimeSpan.FromMinutes(1);
        almacen.Crear("Ana", null);

        Assert.Equal(new[] { "Zoe", "Ana" }, almacen.Listar().Select(p => p.Nombre));
    }

    [Fact]
    public void NoQuedanArchivosTemporalesDespuesDeGuardar()
    {
        var almacen = Nuevo();
        almacen.Crear("Ana", null);

        Assert.Empty(Directory.GetFiles(_raiz, "*.tmp"));
    }
}
