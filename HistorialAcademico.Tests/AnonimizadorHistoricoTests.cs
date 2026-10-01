using HistorialAcademico.Banner;
using HistorialAcademico.Core;
using HistorialAcademico.Core.Universidad;
using HistorialAcademico.Validador;
using Xunit;

namespace HistorialAcademico.Tests;

/// <summary>El anonimizador de históricos: la muestra que sale es falsa, pero sigue siendo un histórico coherente.</summary>
public class AnonimizadorHistoricoTests
{
    private static string Fixture(string nombre) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", nombre));

    [Fact]
    public void LaMuestraSeLeeConElParserYCuadraConElValidador()
    {
        var salida = AnonimizadorHistorico.Anonimizar(Fixture("historico_sintetico.html"));

        var h = HistoricoParser.Parse(salida);

        Assert.Empty(ValidadorHistorico.Validar(h));
        Assert.NotNull(h.TotalGlobal);
        Assert.Equal(HistoricoParser.Parse(Fixture("historico_sintetico.html")).Periodos.Count, h.Periodos.Count);
    }

    [Fact]
    public void SeConservanLasMateriasPeroLaIdentidadEsFalsa()
    {
        var original = HistoricoParser.Parse(Fixture("historico_sintetico.html"));

        var h = HistoricoParser.Parse(AnonimizadorHistorico.Anonimizar(Fixture("historico_sintetico.html")));

        Assert.Equal(original.Periodos.SelectMany(p => p.Materias).Select(m => m.Codigo), h.Periodos.SelectMany(p => p.Materias).Select(m => m.Codigo));
        Assert.Equal(original.Periodos.SelectMany(p => p.Materias).Select(m => m.HorasCredito), h.Periodos.SelectMany(p => p.Materias).Select(m => m.HorasCredito));
        Assert.Equal("ESTUDIANTE DE PRUEBA", h.Alumno.Nombre);
        Assert.Contains("PRUEBA", h.Alumno.Campus);
        Assert.Contains("PRUEBA", h.Alumno.Programa);
    }

    [Fact]
    public void ElAnioDelPrimerPeriodoSeCorreAlAnioPedido()
    {
        var h = HistoricoParser.Parse(AnonimizadorHistorico.Anonimizar(Fixture("historico_sintetico.html"), anioInicial: 2015));

        Assert.EndsWith("2015", h.Periodos[0].Nombre);
        Assert.Equal(h.Periodos.Count, h.Periodos.Select(p => p.Nombre).Distinct().Count());
    }

    [Fact]
    public void ConLaMismaSemillaSaleLoMismoYConOtraCambianLasNotas()
    {
        var html = Fixture("historico_sintetico.html");

        Assert.Equal(AnonimizadorHistorico.Anonimizar(html, semilla: 7), AnonimizadorHistorico.Anonimizar(html, semilla: 7));
        Assert.NotEqual(AnonimizadorHistorico.Anonimizar(html, semilla: 7), AnonimizadorHistorico.Anonimizar(html, semilla: 8));
    }

    [Fact]
    public void LasExentasSeQuedanExentas()
    {
        var original = HistoricoParser.Parse(Fixture("historico_sintetico.html"));
        var h = HistoricoParser.Parse(AnonimizadorHistorico.Anonimizar(Fixture("historico_sintetico.html")));

        var antes = original.Periodos.SelectMany(p => p.Materias).Count(m => m.Calificacion == "E");
        Assert.Equal(antes, h.Periodos.SelectMany(p => p.Materias).Count(m => m.Calificacion == "E"));
    }

    [Fact]
    public void LoAprobadoSigueAprobadoYLoReprobadoSigueReprobado()
    {
        var original = HistoricoParser.Parse(Fixture("historico_sintetico.html")).Periodos.SelectMany(p => p.Materias).ToList();

        foreach (var semilla in new[] { 1, 2, 3 })
        {
            var nuevas = HistoricoParser.Parse(AnonimizadorHistorico.Anonimizar(Fixture("historico_sintetico.html"), semilla: semilla)).Periodos.SelectMany(p => p.Materias).ToList();

            for (var i = 0; i < original.Count; i++)
                Assert.Equal(EscalaCalificaciones.Unapec.CuentaComoAprobada(original[i].Calificacion), EscalaCalificaciones.Unapec.CuentaComoAprobada(nuevas[i].Calificacion));
        }
    }

    [Fact]
    public void UnHistoricoConDatosPersonalesRealesNoDejaNingunoEnLaMuestra()
    {
        var real = Fixture("historico_sintetico.html")
            .Replace("ESTUDIANTE DE PRUEBA", "MARIA PEREZ GOMEZ")
            .Replace("CAMPUS - PRUEBA", "CAMPUS - CENTRAL");

        var salida = AnonimizadorHistorico.Anonimizar(real);

        Assert.DoesNotContain("MARIA", salida, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PEREZ", salida, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CENTRAL", salida, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnArchivoQueNoEsUnHistoricoSeRechazaEnVezDeEntregarBasura()
    {
        Assert.ThrowsAny<Exception>(() => AnonimizadorHistorico.Anonimizar("<html><body>hola</body></html>"));
    }

    // ── El comando «anonimizar» ───────────────────────────────────────────────────────────

    private sealed class CarpetaTemporal : IDisposable
    {
        public string Ruta { get; } = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "ha-anon-" + Guid.NewGuid().ToString("N"))).FullName;
        public void Dispose() { try { Directory.Delete(Ruta, true); } catch (IOException) { } }
    }

    private static int Ejecutar(string dir, out string salida, out string error, params string[] args)
    {
        var s = new StringWriter(); var e = new StringWriter();
        var codigo = Comandos.Ejecutar(args.Prepend("anonimizar").ToArray(), s, e, dir);
        salida = s.ToString(); error = e.ToString();
        return codigo;
    }

    [Fact]
    public void ElComandoGuardaLaMuestraEnTestsSamplesSinTocarElOriginal()
    {
        using var t = new CarpetaTemporal();
        var original = Path.Combine(t.Ruta, "real.html");
        File.WriteAllText(original, Fixture("historico_sintetico.html"));
        var antes = File.ReadAllBytes(original);

        var codigo = Ejecutar(t.Ruta, out var salida, out _, "real.html");

        Assert.Equal(Comandos.Bien, codigo);
        Assert.Contains("revísalo antes de hacer commit", salida);
        var destino = Path.Combine(t.Ruta, "tests", "samples", "real-anonimizado.html");
        Assert.True(File.Exists(destino));
        Assert.Empty(ValidadorHistorico.Validar(HistoricoParser.Parse(File.ReadAllText(destino))));
        Assert.Equal(antes, File.ReadAllBytes(original));
    }

    [Fact]
    public void ElComandoNoPisaUnaMuestraExistenteSalvoConForzar()
    {
        using var t = new CarpetaTemporal();
        File.WriteAllText(Path.Combine(t.Ruta, "real.html"), Fixture("historico_sintetico.html"));

        Assert.Equal(Comandos.Bien, Ejecutar(t.Ruta, out _, out _, "real.html"));
        Assert.Equal(Comandos.ConErrores, Ejecutar(t.Ruta, out _, out var error, "real.html"));
        Assert.Contains("--forzar", error);
        Assert.Equal(Comandos.Bien, Ejecutar(t.Ruta, out _, out _, "real.html", "--forzar", "--semilla", "3"));
    }

    [Fact]
    public void ElComandoNuncaEscribeSobreLaEntrada()
    {
        using var t = new CarpetaTemporal();
        var original = Path.Combine(t.Ruta, "real.html");
        File.WriteAllText(original, Fixture("historico_sintetico.html"));

        var codigo = Ejecutar(t.Ruta, out _, out var error, "real.html", "--salida", "real.html", "--forzar");

        Assert.Equal(Comandos.ConErrores, codigo);
        Assert.Contains("el original no se toca", error);
        Assert.Equal(Fixture("historico_sintetico.html"), File.ReadAllText(original));
    }

    [Fact]
    public void ElComandoAvisaSiElArchivoNoExisteONoEsUnHistorico()
    {
        using var t = new CarpetaTemporal();
        File.WriteAllText(Path.Combine(t.Ruta, "raro.html"), "<html><body>hola</body></html>");

        Assert.Equal(Comandos.ConErrores, Ejecutar(t.Ruta, out _, out var error1, "no-existe.html"));
        Assert.Contains("No existe el archivo", error1);
        Assert.Equal(Comandos.ConErrores, Ejecutar(t.Ruta, out _, out var error2, "raro.html"));
        Assert.Contains("No se pudo anonimizar", error2);
        Assert.False(Directory.Exists(Path.Combine(t.Ruta, "tests")));   // no se dejó nada a medias
    }

    public static TheoryData<string> ArgumentosMalos => new()
    {
        "", "a.html b.html", "a.html --anio-inicial abc", "a.html --semilla x", "a.html --rara 1",
    };

    [Theory]
    [MemberData(nameof(ArgumentosMalos))]
    public void ElComandoConArgumentosMalosMuestraLaAyuda(string args)
    {
        using var t = new CarpetaTemporal();

        Assert.Equal(Comandos.UsoIncorrecto, Ejecutar(t.Ruta, out _, out var error, args.Split(' ', StringSplitOptions.RemoveEmptyEntries)));
        Assert.Contains("Uso:", error);
    }
}
