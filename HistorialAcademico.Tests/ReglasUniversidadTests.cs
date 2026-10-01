using System.Text.Json;
using HistorialAcademico.Core.Entities;
using HistorialAcademico.Core.Horarios;
using HistorialAcademico.Core.Indice;
using HistorialAcademico.Core.Pensum;
using HistorialAcademico.Core.Planificacion;
using HistorialAcademico.Core.Universidad;

namespace HistorialAcademico.Tests;

/// <summary>Una universidad inventada, con otra escala, otros períodos y otros límites, para probar que nada quedó fijo en el código.</summary>
internal static class UniversidadDePrueba
{
    /// <summary>Escala de 5 puntos (A a E), la D aprueba con 1 y la P (pasó) aprueba sin entrar en el índice.</summary>
    public static readonly EscalaCalificaciones Escala = new(new[]
    {
        new LetraCalificacion("A", 5, true, true),
        new LetraCalificacion("B", 4, true, true),
        new LetraCalificacion("C", 3, true, true),
        new LetraCalificacion("D", 1, true, true),
        new LetraCalificacion("E", 0, false, true),      // aquí la E reprueba y baja el promedio (en UNAPEC es exenta)
        new LetraCalificacion("P", null, true, false),
    });

    /// <summary>Dos semestres al año, sin Banner.</summary>
    public static readonly SecuenciaPeriodos Semestres = new(new PeriodoDef[] { new("SEM1"), new("SEM2") });

    public static readonly ReglasUniversidad Reglas = new("uni-prueba", "Universidad de Prueba", null, Escala, Semestres, new LimitesCreditos(18, 21, 4.00m));

    public const string Json = """
        {
          "formato": 1, "id": "uni-prueba", "nombre": "Universidad de Prueba",
          "escalaCalificaciones": [
            { "letra": "A", "puntos": 4, "aprueba": true, "cuentaParaIndice": true },
            { "letra": "F", "puntos": 0, "aprueba": false, "cuentaParaIndice": true },
            { "letra": "P", "aprueba": true, "cuentaParaIndice": false }
          ],
          "periodos": [ { "nombre": "SEM1" }, { "nombre": "SEM2" } ],
          "limitesCreditos": { "base": 18, "alto": 21, "umbralIndice": 3.5 }
        }
        """;
}

/// <summary>La escala de calificaciones como dato: el índice y el estado de las materias dependen de ella, no del código.</summary>
public class EscalaCalificacionesTests
{
    [Theory]
    [InlineData("A", 4)] [InlineData("b", 3)] [InlineData(" C ", 2)] [InlineData("D", 1)] [InlineData("F", 0)]
    public void LaEscalaDeUnapecDaLosPuntosDeSiempre(string letra, int puntos) => Assert.Equal(puntos, EscalaCalificaciones.Unapec.PuntosPorLetra(letra));

    [Theory]
    [InlineData("A+")] [InlineData("E")] [InlineData("")] [InlineData(null)] [InlineData("Z")]
    public void LasLetrasQueNoCuentanNoTienenPuntosEnUnapec(string? letra)
    {
        Assert.Null(EscalaCalificaciones.Unapec.PuntosPorLetra(letra));
        Assert.False(EscalaCalificaciones.Unapec.CuentaParaIndice(letra));
    }

    [Fact]
    public void LaFacadeAntiguaDaLoMismoQueLaEscalaDeUnapec()
    {
        foreach (var letra in new[] { "A", "B", "C", "D", "F", "E", "X", null })
        {
            Assert.Equal(EscalaCalificaciones.Unapec.PuntosPorLetra(letra), ReglasIndice.PuntosPorLetra(letra));
            Assert.Equal(EscalaCalificaciones.Unapec.CuentaComoAprobada(letra), ReglasIndice.CuentaComoAprobada(letra));
            Assert.Equal(EscalaCalificaciones.Unapec.EsExenta(letra), ReglasIndice.EsExenta(letra));
            Assert.Equal(EscalaCalificaciones.Unapec.EsAprobada(letra), ReglasIndice.EsAprobada(letra));
        }
    }

    [Fact]
    public void UnaEscalaDistintaCambiaQueApruebaYQueEntraEnElIndice()
    {
        var e = UniversidadDePrueba.Escala;

        Assert.Equal(5, e.PuntosPorLetra("A"));
        Assert.True(e.CuentaComoAprobada("D"));
        Assert.False(e.CuentaComoAprobada("E"));       // la E reprueba en esta universidad
        Assert.True(e.CuentaParaIndice("E"));          // y baja el promedio
        Assert.True(e.EsExenta("P"));                  // aprueba sin entrar en el índice
        Assert.False(e.EsExenta("A"));
        Assert.Equal(5, e.PuntosMaximos);
    }

    [Fact]
    public void ElIndiceSeCalculaConLaEscalaQueSeLeDa()
    {
        var periodos = new[]
        {
            new Periodo { Nombre = "SEM1 2025", Orden = 1, Materias = new()
            {
                new MateriaCursada { Codigo = "AAA100", Calificacion = "A", HorasCredito = 3 },   // 5 × 3 = 15
                new MateriaCursada { Codigo = "BBB100", Calificacion = "B", HorasCredito = 3 },   // 4 × 3 = 12
                new MateriaCursada { Codigo = "CCC100", Calificacion = "P", HorasCredito = 4 },   // aprueba, fuera del índice
                new MateriaCursada { Codigo = "DDD100", Calificacion = "E", HorasCredito = 2 },   // reprueba, 0 puntos, sí cuenta
            } },
        };

        var propio = CalculadoraIndice.Calcular(periodos, UniversidadDePrueba.Escala).Global;
        var unapec = CalculadoraIndice.Calcular(periodos).Global;

        Assert.Equal(8m, propio.HorasPga);                    // 3 + 3 + 2
        Assert.Equal(27m, propio.PuntosCalidad);              // 15 + 12 + 0
        Assert.Equal(3.38m, propio.Indice);                   // 27 / 8 = 3.375 → 3.38
        Assert.Equal(10m, propio.HorasAprobadas);             // A + B + P
        Assert.Equal(3.50m, unapec.Indice);                   // con la escala de UNAPEC: (4×3 + 3×3) / 6, la E queda fuera
    }

    [Fact]
    public void ElEstadoDeLasMateriasUsaLaEscalaDeLaUniversidad()
    {
        var pensum = new[]
        {
            new MateriaPensum { Codigo = "AAA100", Nombre = "A", Creditos = 3, Cuatrimestre = 1 },
            new MateriaPensum { Codigo = "BBB100", Nombre = "B", Creditos = 3, Cuatrimestre = 1 },
            new MateriaPensum { Codigo = "CCC100", Nombre = "C", Creditos = 3, Cuatrimestre = 1 },
        };
        var cursadas = new[]
        {
            new MateriaCursada { Codigo = "AAA100", Calificacion = "P" },   // exenta en esta escala
            new MateriaCursada { Codigo = "BBB100", Calificacion = "E" },   // reprobada aquí
            new MateriaCursada { Codigo = "CCC100", Calificacion = "D" },
        };

        var r = MotorEstadoPensum.Calcular(pensum, cursadas, Array.Empty<CursoEnProgreso>(), Array.Empty<Equivalencia>(), UniversidadDePrueba.Escala);

        Assert.Equal(EstadoMateria.Exenta, r.Buscar("AAA100")!.Estado);
        Assert.Equal(EstadoMateria.Disponible, r.Buscar("BBB100")!.Estado);   // reprobada: sigue pendiente
        Assert.Equal(EstadoMateria.Aprobada, r.Buscar("CCC100")!.Estado);
        Assert.Equal(6, r.CreditosAprobados);

        // Con la escala de UNAPEC, esas mismas letras se leen distinto: P no existe y E es exenta.
        var unapec = MotorEstadoPensum.Calcular(pensum, cursadas, Array.Empty<CursoEnProgreso>(), Array.Empty<Equivalencia>());
        Assert.Equal(EstadoMateria.Exenta, unapec.Buscar("BBB100")!.Estado);
        Assert.Equal(EstadoMateria.Disponible, unapec.Buscar("AAA100")!.Estado);
    }
}

/// <summary>La secuencia de períodos como dato: otra universidad puede tener dos semestres o cuatro trimestres.</summary>
public class SecuenciaPeriodosTests
{
    private static readonly SecuenciaPeriodos Semestres = UniversidadDePrueba.Semestres;
    private static readonly SecuenciaPeriodos Trimestres = new(new PeriodoDef[] { new("T1"), new("T2"), new("T3"), new("T4") });

    [Fact]
    public void ConDosSemestresElAnioSePartePorLaMitad()
    {
        var p = PeriodoAcademico.Parse("SEM1 2026", Semestres);

        Assert.Equal("SEM2 2026", p.Siguiente().Nombre);
        Assert.Equal("SEM1 2027", p.Siguiente().Siguiente().Nombre);
        Assert.Equal("SEM2 2025", p.Avanzar(-1).Nombre);
        Assert.Equal("SEM1 2028", p.Avanzar(4).Nombre);
        Assert.Equal(3, p.Distancia(p.Avanzar(3)));
    }

    [Theory]
    [InlineData("T1 2026", 1, "T2 2026")]
    [InlineData("T4 2026", 1, "T1 2027")]
    [InlineData("T1 2026", -1, "T4 2025")]
    [InlineData("T2 2026", 7, "T1 2028")]
    [InlineData("T3 2026", -6, "T1 2025")]
    public void ConCuatroTrimestresAvanzarYRetrocederCruzaLosAnios(string desde, int n, string esperado) =>
        Assert.Equal(esperado, PeriodoAcademico.Parse(desde, Trimestres).Avanzar(n).Nombre);

    [Fact]
    public void LosPeriodosSaleDeUnaSecuenciaLaConservan()
    {
        var p = PeriodoAcademico.Parse("SEM2 2026", Semestres);

        Assert.Equal(Semestres, p.Siguiente().Sec);
        Assert.Equal(Semestres, p.Avanzar(5).Sec);
        Assert.True(p < p.Siguiente());
    }

    [Theory]
    [InlineData("SEM1 2026", true)]
    [InlineData("sem2 2026", true)]
    [InlineData("SEM3 2026", false)]       // no existe en esta secuencia
    [InlineData("ENE-ABR 2026", false)]    // es de UNAPEC
    [InlineData("SEM1 26", false)]
    [InlineData("", false)]
    public void SoloSeLeenLosNombresDeLaSecuenciaDada(string texto, bool valido) => Assert.Equal(valido, PeriodoAcademico.TryParse(texto, Semestres, out _));

    [Fact]
    public void LaSecuenciaDeUnapecSigueSiendoLaPorOmision()
    {
        Assert.Equal("MAY-AGO 2026", PeriodoAcademico.Parse("ENE-ABR 2026").Siguiente().Nombre);
        Assert.False(PeriodoAcademico.TryParse("SEM1 2026", out _));
        Assert.Equal(new PeriodoAcademico(2026, 1), PeriodoAcademico.Parse("MAY-AGO 2026", SecuenciaPeriodos.Unapec));   // indicar UNAPEC o no indicar nada es igual
        Assert.Equal(new PeriodoAcademico(2026, 1).GetHashCode(), PeriodoAcademico.Parse("MAY-AGO 2026", SecuenciaPeriodos.Unapec).GetHashCode());
    }

    [Fact]
    public void PeriodosDeSecuenciasDistintasNoSonIguales() =>
        Assert.NotEqual(new PeriodoAcademico(2026, 0), new PeriodoAcademico(2026, 0, Semestres));

    [Fact]
    public void ElPrimerPeriodoPlanificableUsaLaSecuenciaDeLaUniversidad()
    {
        Assert.Equal("SEM1 2027", PeriodoAcademico.Primero("SEM2 2026", null, asumirEnCurso: true, Semestres)!.Value.Nombre);
        Assert.Equal("SEM2 2026", PeriodoAcademico.Primero("SEM2 2026", null, asumirEnCurso: false, Semestres)!.Value.Nombre);
        Assert.Equal("SEM2 2026", PeriodoAcademico.Primero(null, "SEM1 2026", true, Semestres)!.Value.Nombre);
        Assert.Null(PeriodoAcademico.Primero("SEP-DIC 2026", null, true, Semestres));      // nombres de otra universidad
    }

    [Fact]
    public void UnaSecuenciaSeComparaPorSusPeriodos()
    {
        Assert.Equal(SecuenciaPeriodos.Unapec, new SecuenciaPeriodos(new PeriodoDef[] { new("ENE-ABR", "10"), new("MAY-AGO", "20"), new("SEP-DIC", "30") }));
        Assert.NotEqual(SecuenciaPeriodos.Unapec, new SecuenciaPeriodos(new PeriodoDef[] { new("ENE-ABR"), new("MAY-AGO"), new("SEP-DIC") }));   // sin códigos de Banner
        Assert.Throws<ArgumentException>(() => new SecuenciaPeriodos(Array.Empty<PeriodoDef>()));
    }

    // ── Códigos de Banner ─────────────────────────────────────────────────────────────────

    [Fact]
    public void ElCodigoDeBannerSaleDeLaSecuencia()
    {
        var conCodigos = new SecuenciaPeriodos(new PeriodoDef[] { new("PRIM", "15"), new("OTON", "35") });
        var p = PeriodoAcademico.Parse("OTON 2026", conCodigos);

        Assert.Equal("202635", MapeoBanner.CodigoDePeriodo(p));
        Assert.True(MapeoBanner.TryPeriodoDeCodigo("202615", conCodigos, out var vuelta));
        Assert.Equal("PRIM 2026", vuelta.Nombre);
        Assert.False(MapeoBanner.TryPeriodoDeCodigo("202630", conCodigos, out _));
    }

    [Fact]
    public void SinCodigosDeBannerNoSePuedeTraducirUnPeriodo()
    {
        var p = PeriodoAcademico.Parse("SEM1 2026", Semestres);

        var ex = Assert.Throws<InvalidOperationException>(() => MapeoBanner.CodigoDePeriodo(p));
        Assert.Contains("no tiene código de Banner", ex.Message);
        Assert.False(Semestres.TieneCodigosBanner);
        Assert.True(SecuenciaPeriodos.Unapec.TieneCodigosBanner);
    }
}

/// <summary>El formato de universidad.json: lectura, errores en español, escritura y coherencia con lo incorporado.</summary>
public class ReglasUniversidadJsonTests
{
    private static ResultadoReglas Con(string buscar, string reemplazo) => ReglasUniversidadJson.Leer(UniversidadDePrueba.Json.Replace(buscar, reemplazo));

    private static void HayError(ResultadoReglas r, string fragmento)
    {
        Assert.False(r.EsValido);
        Assert.Contains(r.Errores, e => e.Contains(fragmento, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void UnaUniversidadValidaSeLee()
    {
        var r = ReglasUniversidadJson.Leer(UniversidadDePrueba.Json);

        Assert.True(r.EsValido, string.Join(" | ", r.Errores));
        var u = r.Reglas!;
        Assert.Equal(("uni-prueba", "Universidad de Prueba", null), (u.Id, u.Nombre, u.UrlBanner));
        Assert.Equal(new[] { "A", "F", "P" }, u.Escala.Letras.Select(l => l.Letra));
        Assert.True(u.Escala.EsExenta("P"));
        Assert.Equal("SEM1, SEM2", u.Periodos.ToString());
        Assert.Equal(new LimitesCreditos(18, 21, 3.5m), u.Limites);
        Assert.Equal(new ConfigPlanificador(18, 21, 3.5m), u.ConfigPorDefecto with { Minimo = 0, AsumirEnCurso = true });
    }

    [Fact]
    public void EscribirYVolverALeerDevuelveLoMismo()
    {
        var original = UniversidadDePrueba.Reglas with { Limites = new LimitesCreditos(18, 21, 3.5m) };

        var otra = ReglasUniversidadJson.Leer(ReglasUniversidadJson.Escribir(original)).Reglas!;

        Assert.Equal(original, otra);
    }

    [Theory]
    [InlineData("no es json")]
    [InlineData("[1]")]
    public void UnArchivoQueNoEsUnObjetoJsonDaUnError(string texto) => Assert.False(ReglasUniversidadJson.Leer(texto).EsValido);

    [Fact]
    public void FaltanLasPropiedadesObligatorias()
    {
        var r = ReglasUniversidadJson.Leer("{}");

        foreach (var p in ReglasUniversidadJson.RequeridasRaiz) Assert.Contains(r.Errores, e => e.Contains($"«{p}»"));
    }

    [Fact]
    public void UnaPropiedadDesconocidaSeRechaza()
    {
        HayError(Con("\"formato\": 1,", "\"formato\": 1, \"escala\": 3,"), "Propiedad desconocida «escala»");
        HayError(Con("\"letra\": \"A\", \"puntos\": 4,", "\"letra\": \"A\", \"puntoss\": 4, \"puntos\": 4,"), "propiedad desconocida «puntoss»");
    }

    [Theory]
    [InlineData("\"id\": \"uni-prueba\"", "\"id\": \"Uni Prueba\"", "«Uni Prueba» no es válido")]
    [InlineData("\"formato\": 1", "\"formato\": 2", "«formato»")]
    [InlineData("\"nombre\": \"Universidad de Prueba\"", "\"nombre\": \"\"", "no puede estar vacío")]
    [InlineData("\"letra\": \"A\"", "\"letra\": \"a b\"", "escalaCalificaciones[0].letra")]
    [InlineData("\"puntos\": 4", "\"puntos\": 40", "entre 0 y 10")]
    [InlineData("\"aprueba\": true, \"cuentaParaIndice\": true }", "\"aprueba\": \"si\", \"cuentaParaIndice\": true }", "debe ser true o false")]
    [InlineData("{ \"nombre\": \"SEM1\" }", "{ \"nombre\": \"sem 1\" }", "periodos[0].nombre")]
    [InlineData("\"base\": 18", "\"base\": 0", "entre 1 y 60")]
    [InlineData("\"alto\": 21", "\"alto\": 10", "«alto» (10) no puede ser menor que «base» (18)")]
    [InlineData("\"umbralIndice\": 3.5", "\"umbralIndice\": 4.5", "entre 0 y 4")]
    [InlineData("\"umbralIndice\": 3.5", "\"umbralIndice\": \"alto\"", "debe ser un número")]
    public void CadaValorInvalidoDaSuPropioError(string buscar, string reemplazo, string fragmento) => HayError(Con(buscar, reemplazo), fragmento);

    [Fact]
    public void LaEscalaNecesitaCoherenciaEntrePuntosYEntradaAlIndice()
    {
        HayError(Con("\"letra\": \"P\", \"aprueba\": true, \"cuentaParaIndice\": false", "\"letra\": \"P\", \"puntos\": 2, \"aprueba\": true, \"cuentaParaIndice\": false"),
            "no cuenta para el índice, así que no debe tener «puntos»");
        HayError(Con("\"letra\": \"A\", \"puntos\": 4,", "\"letra\": \"A\","), "cuenta para el índice, así que necesita «puntos»");
    }

    [Fact]
    public void LaEscalaNoPuedeRepetirLetrasNiQuedarSinAprobarNiSinIndice()
    {
        HayError(Con("\"letra\": \"F\"", "\"letra\": \"A\""), "la letra A está repetida");
        HayError(ReglasUniversidadJson.Leer(UniversidadDePrueba.Json.Replace("\"aprueba\": true", "\"aprueba\": false")), "al menos una letra debe aprobar");
        HayError(ReglasUniversidadJson.Leer(UniversidadDePrueba.Json.Replace("\"cuentaParaIndice\": true", "\"cuentaParaIndice\": false").Replace("\"puntos\": 4,", "").Replace("\"puntos\": 0,", "")),
            "al menos una letra debe contar para el índice");
    }

    [Fact]
    public void LosPeriodosNoSeRepitenYLosCodigosDeBannerSonTodosONinguno()
    {
        HayError(Con("{ \"nombre\": \"SEM2\" }", "{ \"nombre\": \"SEM1\" }"), "«SEM1» está repetido");
        HayError(Con("{ \"nombre\": \"SEM1\" }, { \"nombre\": \"SEM2\" }", "{ \"nombre\": \"SEM1\", \"codigoBanner\": \"10\" }, { \"nombre\": \"SEM2\" }"), "todos deben tenerlo");
        HayError(Con("{ \"nombre\": \"SEM1\" }, { \"nombre\": \"SEM2\" }", "{ \"nombre\": \"SEM1\", \"codigoBanner\": \"10\" }, { \"nombre\": \"SEM2\", \"codigoBanner\": \"10\" }"), "código de Banner 10 está repetido");
        HayError(Con("{ \"nombre\": \"SEM1\" }", "{ \"nombre\": \"SEM1\", \"codigoBanner\": \"1\" }"), "dos dígitos");
        HayError(Con("[ { \"nombre\": \"SEM1\" }, { \"nombre\": \"SEM2\" } ]", "[]"), "entre 1 y 12");
    }

    [Fact]
    public void LaUrlDeBannerDebeSerHttps()
    {
        HayError(Con("\"nombre\": \"Universidad de Prueba\",", "\"nombre\": \"Universidad de Prueba\", \"urlBanner\": \"http://banner.example\","), "debe empezar con https://");
        Assert.True(Con("\"nombre\": \"Universidad de Prueba\",", "\"nombre\": \"Universidad de Prueba\", \"urlBanner\": \"https://banner.example/x\",").EsValido);
    }

    [Fact]
    public void ElArchivoDebeEstarEnLaCarpetaDeSuIdYLlamarseUniversidadJson()
    {
        Assert.True(ReglasUniversidadJson.Leer(UniversidadDePrueba.Json, Path.Combine("pensums", "uni-prueba", "universidad.json")).EsValido);
        HayError(ReglasUniversidadJson.Leer(UniversidadDePrueba.Json, Path.Combine("pensums", "otra", "universidad.json")), "carpeta «otra»");
        HayError(ReglasUniversidadJson.Leer(UniversidadDePrueba.Json, Path.Combine("pensums", "uni-prueba", "reglas.json")), "se debe llamar «universidad.json»");
    }

    // ── El archivo de UNAPEC ──────────────────────────────────────────────────────────────

    [Fact]
    public void ElArchivoDeUnapecDelRepositorioCoincideExactamenteConLasReglasIncorporadas()
    {
        var ruta = Path.Combine(PensumEjemplo.CarpetaPensums, "unapec", "universidad.json");

        var leido = ReglasUniversidadJson.Leer(File.ReadAllText(ruta), ruta);

        Assert.True(leido.EsValido, string.Join(" | ", leido.Errores));
        Assert.Equal(ReglasUniversidad.Unapec, leido.Reglas);          // si se cambia uno, hay que cambiar el otro
        Assert.Equal(ReglasUniversidadJson.Escribir(ReglasUniversidad.Unapec), File.ReadAllText(ruta).Replace("\r\n", "\n"));
    }

    [Fact]
    public void ElEsquemaPublicadoDescribeLasMismasPropiedadesQueLaValidacion()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(PensumEjemplo.CarpetaPensums, "universidad.schema.json")));
        var raiz = doc.RootElement;
        var props = raiz.GetProperty("properties");

        Assert.Equal(ReglasUniversidadJson.PropiedadesRaiz.OrderBy(x => x), props.EnumerateObject().Select(p => p.Name).OrderBy(x => x));
        Assert.Equal(ReglasUniversidadJson.RequeridasRaiz.OrderBy(x => x), raiz.GetProperty("required").EnumerateArray().Select(e => e.GetString()!).OrderBy(x => x));
        var letra = props.GetProperty("escalaCalificaciones").GetProperty("items");
        Assert.Equal(ReglasUniversidadJson.PropiedadesLetra.OrderBy(x => x), letra.GetProperty("properties").EnumerateObject().Select(p => p.Name).OrderBy(x => x));
        Assert.Equal(ReglasUniversidadJson.RequeridasLetra.OrderBy(x => x), letra.GetProperty("required").EnumerateArray().Select(e => e.GetString()!).OrderBy(x => x));
        Assert.Equal(ReglasUniversidadJson.PropiedadesPeriodo.OrderBy(x => x), props.GetProperty("periodos").GetProperty("items").GetProperty("properties").EnumerateObject().Select(p => p.Name).OrderBy(x => x));
        Assert.Equal(ReglasUniversidadJson.PropiedadesLimites.OrderBy(x => x), props.GetProperty("limitesCreditos").GetProperty("properties").EnumerateObject().Select(p => p.Name).OrderBy(x => x));
        Assert.Equal(ReglasUniversidadJson.MaxPeriodosPorAnio, props.GetProperty("periodos").GetProperty("maxItems").GetInt32());
        Assert.Equal(ReglasUniversidadJson.MaxCreditosPorPeriodo, props.GetProperty("limitesCreditos").GetProperty("properties").GetProperty("base").GetProperty("maximum").GetInt32());
    }

    // ── Catálogo ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ElCatalogoDelRepositorioTieneUnapecConSuPensumYSinProblemas()
    {
        var catalogo = CatalogoUniversidades.Leer(PensumEjemplo.CarpetaPensums);

        Assert.Empty(catalogo.Problemas);
        Assert.Equal(new[] { "unapec" }, catalogo.UniversidadesValidas.Select(u => u.Id));
        Assert.Equal(new[] { "unapec/ingenieria-software-11.json" }, catalogo.PensumsValidos.Select(p => p.Clave));
        Assert.Equal(ReglasUniversidad.Unapec, catalogo.ReglasDe("unapec"));
        Assert.Null(catalogo.ReglasDe("otra"));
    }

    [Fact]
    public void UnPensumSinUniversidadValidaNoSePuedeUsar()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pensums-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "uni-prueba"));
            File.WriteAllText(Path.Combine(dir, "uni-prueba", "carrera-x-2019.json"),
                """{ "formato": 1, "universidad": "uni-prueba", "carrera": "carrera-x", "nombreCarrera": "X", "version": "2019", "totalCreditos": 3, "cuatrimestres": 1, "materias": [ { "codigo": "AAA100", "nombre": "A", "creditos": 3, "cuatrimestre": 1 } ] }""");

            var sin = CatalogoUniversidades.Leer(dir);
            Assert.Empty(sin.PensumsValidos);
            Assert.Contains(sin.Problemas, p => p.Contains("no tiene un universidad.json válido"));

            File.WriteAllText(Path.Combine(dir, "uni-prueba", "universidad.json"), UniversidadDePrueba.Json);
            var con = CatalogoUniversidades.Leer(dir);
            Assert.Empty(con.Problemas);
            Assert.Single(con.PensumsValidos);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void UnUniversidadJsonRotoSaleEnLosProblemasConSuCarpeta()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pensums-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "rota"));
            File.WriteAllText(Path.Combine(dir, "rota", "universidad.json"), "{ no es json");

            var catalogo = CatalogoUniversidades.Leer(dir);

            Assert.Empty(catalogo.UniversidadesValidas);
            Assert.Contains(catalogo.Problemas, p => p.StartsWith("rota/universidad.json: El archivo no es un JSON válido"));
        }
        finally { Directory.Delete(dir, true); }
    }
}
