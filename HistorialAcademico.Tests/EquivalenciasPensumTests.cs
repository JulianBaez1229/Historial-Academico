using System.Text.Json;
using HistorialAcademico.Core.Entities;
using HistorialAcademico.Core.Pensum;
using HistorialAcademico.Core.Universidad;
using HistorialAcademico.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace HistorialAcademico.Tests;

/// <summary>Las equivalencias que declara un pénsum: formato, validación y cómo se convierten.</summary>
public class EquivalenciasDeclaradasFormatoTests
{
    /// <summary>Un pénsum (plan 2023) que cubre materias de un plan anterior.</summary>
    private const string Base = """
        {
          "formato": 1, "universidad": "u", "carrera": "c", "nombreCarrera": "C", "version": "2023", "totalCreditos": 7, "cuatrimestres": 2,
          "materias": [
            { "codigo": "FIS100", "nombre": "Física", "creditos": 3, "cuatrimestre": 1 },
            { "codigo": "LAB100", "nombre": "Laboratorio", "creditos": 1, "cuatrimestre": 1 },
            { "codigo": "MAT200", "nombre": "Matemática", "creditos": 3, "cuatrimestre": 2 }
          ],
          "equivalencias": [
            { "origen": "FIS001", "destino": ["FIS100", "LAB100"], "nota": "Física con laboratorio del plan anterior." },
            { "origen": "ESP102", "destino": [] }
          ]
        }
        """;

    private static ResultadoPensumJson Con(string buscar, string reemplazo) => PensumJson.Leer(Base.Replace(buscar, reemplazo));

    private static void HayError(ResultadoPensumJson r, string fragmento)
    {
        Assert.False(r.EsValido);
        Assert.Contains(r.Errores, e => e.Contains(fragmento, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void LasEquivalenciasSeLeenConSusDestinosYNotas()
    {
        var r = PensumJson.Leer(Base);

        Assert.True(r.EsValido, string.Join(" | ", r.Errores));
        var e = r.Definicion!.Equivalencias;
        Assert.Equal(new[] { "FIS001", "ESP102" }, e.Select(x => x.Origen));
        Assert.Equal(new[] { "FIS100", "LAB100" }, e[0].Destino);
        Assert.Equal("Física con laboratorio del plan anterior.", e[0].Nota);
        Assert.Empty(e[1].Destino);
        Assert.Null(e[1].Nota);
    }

    [Fact]
    public void UnPensumSinEquivalenciasSigueSiendoValido()
    {
        var sinEquivalencias = Base[..Base.IndexOf(",\n  \"equivalencias\"", StringComparison.Ordinal)] + "\n}";

        var r = PensumJson.Leer(sinEquivalencias);

        Assert.True(r.EsValido, string.Join(" | ", r.Errores));
        Assert.Empty(r.Definicion!.Equivalencias);
    }

    [Fact]
    public void CadaDestinoSeConvierteEnUnaFilaYUnaSinDestinoEnUnaFilaSinEquivalente()
    {
        var filas = PensumJson.Leer(Base).Definicion!.AEquivalencias();

        Assert.Equal(new[] { ("FIS001", "FIS100"), ("FIS001", "LAB100"), ("ESP102", (string?)null) }, filas.Select(f => (f.CodigoBanner, f.CodigoPensum)));
        Assert.All(filas.Take(2), f => Assert.Equal("Física con laboratorio del plan anterior.", f.Nota));
    }

    [Fact]
    public void EscribirYVolverALeerConservaLasEquivalencias()
    {
        var p = PensumJson.Leer(Base).Definicion!;

        var otra = PensumJson.Leer(PensumJson.Escribir(p)).Definicion!;

        Assert.Equal(p.Equivalencias.Select(e => (e.Origen, string.Join(",", e.Destino), e.Nota)), otra.Equivalencias.Select(e => (e.Origen, string.Join(",", e.Destino), e.Nota)));
        Assert.Contains("\"equivalencias\": [", PensumJson.Escribir(p));
    }

    [Theory]
    [InlineData("\"origen\": \"FIS001\"", "\"origen\": \"fis 001\"", "no es válido")]
    [InlineData("\"destino\": [\"FIS100\", \"LAB100\"]", "\"destino\": [\"FIS100\", \"XXX999\"]", "el destino XXX999 no existe en el pénsum")]
    [InlineData("\"destino\": [\"FIS100\", \"LAB100\"]", "\"destino\": [\"FIS100\", \"FIS100\"]", "el destino FIS100 está repetido")]
    [InlineData("\"destino\": [\"FIS100\", \"LAB100\"]", "\"destino\": [\"fis 100\"]", "no es un código de materia válido")]
    [InlineData("\"destino\": [\"FIS100\", \"LAB100\"]", "\"destino\": \"FIS100\"", "debe ser una lista")]
    [InlineData("\"origen\": \"ESP102\"", "\"origen\": \"FIS001\"", "el origen FIS001 está repetido")]
    [InlineData("\"origen\": \"ESP102\", \"destino\": []", "\"destino\": []", "falta «origen»")]
    [InlineData("\"origen\": \"ESP102\", \"destino\": []", "\"origen\": \"ESP102\"", "falta «destino»")]
    [InlineData("\"origen\": \"ESP102\", \"destino\": []", "\"origen\": \"ESP102\", \"destino\": [], \"creditos\": 3", "propiedad desconocida «creditos»")]
    public void CadaErrorDeUnaEquivalenciaDiceDondeEsta(string buscar, string reemplazo, string fragmento) => HayError(Con(buscar, reemplazo), fragmento);

    [Fact]
    public void UnaEquivalenciaQueNoEsUnObjetoOUnaListaMalPuestaSeRechaza()
    {
        HayError(Con("\"equivalencias\": [", "\"equivalencias\": [ 5,"), "equivalencias[0]: debe ser un objeto");
        HayError(PensumJson.Leer(Base[..Base.IndexOf(",\n  \"equivalencias\"", StringComparison.Ordinal)] + ",\n  \"equivalencias\": 5\n}"), "debe ser una lista");
    }

    [Fact]
    public void UnOrigenQueYaEsUnaMateriaDelPensumSoloDaUnaAdvertencia()
    {
        var r = Con("\"origen\": \"ESP102\"", "\"origen\": \"MAT200\"");

        Assert.True(r.EsValido, string.Join(" | ", r.Errores));
        Assert.Contains(r.Advertencias, a => a.Contains("MAT200 ya es una materia de este pénsum"));
    }

    [Fact]
    public void ElEsquemaPublicadoDescribeLasEquivalencias()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(PensumEjemplo.CarpetaPensums, "schema.json")));
        var eq = doc.RootElement.GetProperty("$defs").GetProperty("equivalencia");

        Assert.Equal(PensumJson.PropiedadesEquivalencia.OrderBy(x => x), eq.GetProperty("properties").EnumerateObject().Select(p => p.Name).OrderBy(x => x));
        Assert.Equal(PensumJson.RequeridasEquivalencia.OrderBy(x => x), eq.GetProperty("required").EnumerateArray().Select(e => e.GetString()!).OrderBy(x => x));
        Assert.False(eq.GetProperty("additionalProperties").GetBoolean());
    }

    [Fact]
    public void LasEquivalenciasDeUnapecEnElArchivoSonLasInicialesDeSiempre()
    {
        var declaradas = PensumJson.Leer(File.ReadAllText(PensumEjemplo.Archivo), PensumEjemplo.Archivo).Definicion!.AEquivalencias();

        Assert.Equal(EquivalenciasIniciales.Valores.Select(v => (v.CodigoBanner, v.CodigoPensum)).OrderBy(x => x.CodigoBanner).ThenBy(x => x.CodigoPensum),
                     declaradas.Select(v => (v.CodigoBanner, v.CodigoPensum)).OrderBy(x => x.CodigoBanner).ThenBy(x => x.CodigoPensum));
    }
}

/// <summary>Unir lo que declara el pénsum con lo que puso la persona.</summary>
public class EquivalenciasEfectivasTests
{
    private static Equivalencia E(string banner, string? pensum, int id = 0, string? nota = null) => new() { Id = id, CodigoBanner = banner, CodigoPensum = pensum, Nota = nota };

    [Fact]
    public void JuntaLasDosListas()
    {
        var r = EquivalenciasEfectivas.Unir(new[] { E("ING701", "ING716") }, new[] { E("QUI100", "QUI200", 5) });

        Assert.Equal(new[] { ("QUI100", "QUI200"), ("ING701", "ING716") }, r.Select(e => (e.CodigoBanner, e.CodigoPensum!)));
    }

    [Fact]
    public void SiEstaEnLasDosSeQuedaLaPersonalQueEsLaEditable()
    {
        var personal = E("ing701", "ing716", id: 7, nota: "mía");

        var r = EquivalenciasEfectivas.Unir(new[] { E("ING701", "ING716", nota: "del pénsum") }, new[] { personal });

        var unica = Assert.Single(r);
        Assert.Same(personal, unica);
        Assert.Equal(7, unica.Id);
    }

    [Fact]
    public void LaComparacionIgnoraMayusculasYEspaciosYDistingueElDestino()
    {
        var r = EquivalenciasEfectivas.Unir(new[] { E("ING701", "ING716"), E("ING701", "ING717"), E("ESP102", null) }, new[] { E(" ing701 ", "ing716", 1), E("esp102", null, 2) });

        Assert.Equal(3, r.Count);        // ING701→ING717 sí se agrega; las otras dos ya estaban
        Assert.Contains(r, e => e.CodigoPensum == "ING717");
    }

    [Fact]
    public void UnaListaVaciaNoCambiaNada()
    {
        Assert.Empty(EquivalenciasEfectivas.Unir(Array.Empty<Equivalencia>(), Array.Empty<Equivalencia>()));
        Assert.Single(EquivalenciasEfectivas.Unir(Array.Empty<Equivalencia>(), new[] { E("A", "B") }));
        Assert.Single(EquivalenciasEfectivas.Unir(new[] { E("A", "B") }, Array.Empty<Equivalencia>()));
    }

    [Fact]
    public void EstaEnDiceSiLaMismaEquivalenciaYaEstaEnLaLista()
    {
        var lista = new[] { E("ING701", "ING716"), E("ESP102", null) };

        Assert.True(EquivalenciasEfectivas.EstaEn(E("ing701", "ing716"), lista));
        Assert.True(EquivalenciasEfectivas.EstaEn(E("ESP102", null), lista));
        Assert.False(EquivalenciasEfectivas.EstaEn(E("ING701", "ING717"), lista));
        Assert.False(EquivalenciasEfectivas.EstaEn(E("ESP102", "ESP106"), lista));
    }
}

/// <summary>Cambiar de plan o de carrera: qué materias se convalidan con tu histórico y cómo se recalcula todo.</summary>
public class ConvalidacionTests : IDisposable
{
    /// <summary>Derecho plan 2019 (AAA100, BBB200) y plan 2025, que declara que AAA100 y BBB200 del plan anterior cubren CCC100 y DDD200.</summary>
    private const string Derecho2025 = """
        { "formato": 1, "universidad": "uni-prueba", "carrera": "derecho", "nombreCarrera": "Derecho", "version": "2025", "totalCreditos": 11, "cuatrimestres": 3,
          "materias": [
            { "codigo": "CCC100", "nombre": "Introducción", "creditos": 3, "cuatrimestre": 1 },
            { "codigo": "DDD200", "nombre": "Fundamentos", "creditos": 4, "cuatrimestre": 2, "prerrequisitos": ["CCC100"] },
            { "codigo": "EEE300", "nombre": "Nueva materia", "creditos": 4, "cuatrimestre": 3, "prerrequisitos": ["DDD200"] }
          ],
          "equivalencias": [
            { "origen": "AAA100", "destino": ["CCC100"], "nota": "Introducción del plan 2019." },
            { "origen": "BBB200", "destino": ["DDD200"] },
            { "origen": "ZZZ999", "destino": [] }
          ] }
        """;

    private readonly CatalogoTemporal _catalogo = new();
    private readonly BdPrueba _bd = new();
    private readonly ReglasUniversidadService _reglas;
    private readonly CarreraService _s;
    private readonly AcademicoService _academico;

    public ConvalidacionTests()
    {
        _catalogo.Escribir("uni-prueba", "derecho-2025.json", Derecho2025);
        _reglas = _catalogo.Reglas();
        _academico = new AcademicoService(_bd.Db, _reglas);
        _s = new CarreraService(_bd.Db, _reglas, _academico);
    }

    public void Dispose() { _bd.Dispose(); _catalogo.Dispose(); }

    private async Task HistoricoAsync(params (string Codigo, string Nota, int Horas)[] materias)
    {
        _bd.Db.Periodos.Add(new Periodo { Nombre = "SEM1 2024", Orden = 1, Materias = materias.Select(m => new MateriaCursada { Codigo = m.Codigo, Calificacion = m.Nota, HorasCredito = m.Horas }).ToList() });
        await _bd.Db.SaveChangesAsync();
    }

    private PensumDefinicion Definicion(string clave) => _reglas.LeerCatalogo().PensumsValidos.Single(p => p.Clave == clave);

    // ── El cálculo ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ElPlanNuevoConvalidaLoDelPlanAnteriorPorLasEquivalenciasQueDeclara()
    {
        await HistoricoAsync(("AAA100", "A", 3), ("BBB200", "A", 5));
        var d = Definicion("uni-prueba/derecho-2025.json");
        var cursadas = await _bd.Db.MateriasCursadas.AsNoTracking().ToListAsync();

        var r = CarreraService.Convalidar(d, _reglas.LeerCatalogo().ReglasDe("uni-prueba")!, cursadas, Array.Empty<CursoEnProgreso>(), Array.Empty<Equivalencia>());

        Assert.Equal(new[] { "CCC100", "DDD200" }, r.Materias.Select(m => m.Codigo));
        Assert.Equal(new[] { "AAA100", "BBB200" }, r.Materias.Select(m => m.PorEquivalencia));
        Assert.Equal(new[] { "A", "A" }, r.Materias.Select(m => m.Calificacion));
        Assert.Equal((2, 0), (r.PorEquivalencia, r.Directas));
        Assert.Equal((7, 11), (r.CreditosAprobados, r.CreditosTotales));
        Assert.Equal(1, r.Disponibles);                            // EEE300 queda disponible: su prerrequisito DDD200 se convalidó
    }

    [Fact]
    public async Task SinLasEquivalenciasElMismoHistoricoNoConvalidaNada()
    {
        await HistoricoAsync(("AAA100", "A", 3), ("BBB200", "A", 5));
        var sinEquivalencias = Definicion("uni-prueba/derecho-2025.json") with { Equivalencias = new() };

        var r = CarreraService.Convalidar(sinEquivalencias, _reglas.LeerCatalogo().ReglasDe("uni-prueba")!,
            await _bd.Db.MateriasCursadas.AsNoTracking().ToListAsync(), Array.Empty<CursoEnProgreso>(), Array.Empty<Equivalencia>());

        Assert.Empty(r.Materias);
        Assert.Equal(0, r.CreditosAprobados);
    }

    [Fact]
    public async Task LasEquivalenciasPersonalesSeSumanALasDelPensum()
    {
        await HistoricoAsync(("QUI100", "A", 3));
        var personal = new Equivalencia { CodigoBanner = "QUI100", CodigoPensum = "EEE300" };   // solo la persona sabe que le convalidaron esta

        var r = CarreraService.Convalidar(Definicion("uni-prueba/derecho-2025.json"), _reglas.LeerCatalogo().ReglasDe("uni-prueba")!,
            await _bd.Db.MateriasCursadas.AsNoTracking().ToListAsync(), Array.Empty<CursoEnProgreso>(), new[] { personal });

        var m = Assert.Single(r.Materias);
        Assert.Equal(("EEE300", "QUI100"), (m.Codigo, m.PorEquivalencia));
    }

    [Fact]
    public async Task UnaAprobacionDirectaPesaMasQueUnaEquivalencia()
    {
        await HistoricoAsync(("AAA100", "A", 3), ("CCC100", "P", 3));   // CCC100 la aprobó directamente (exenta) y además AAA100 la cubre

        var r = CarreraService.Convalidar(Definicion("uni-prueba/derecho-2025.json"), _reglas.LeerCatalogo().ReglasDe("uni-prueba")!,
            await _bd.Db.MateriasCursadas.AsNoTracking().ToListAsync(), Array.Empty<CursoEnProgreso>(), Array.Empty<Equivalencia>());

        var ccc = Assert.Single(r.Materias, m => m.Codigo == "CCC100");
        Assert.Null(ccc.PorEquivalencia);
        Assert.Equal("P", ccc.Calificacion);
        Assert.True(ccc.Exenta);
        Assert.Equal((1, 0), (r.Directas, r.PorEquivalencia));
    }

    [Fact]
    public async Task LoQueNoTieneEquivalenteNoSeConvalida()
    {
        await HistoricoAsync(("ZZZ999", "A", 3));

        var r = CarreraService.Convalidar(Definicion("uni-prueba/derecho-2025.json"), _reglas.LeerCatalogo().ReglasDe("uni-prueba")!,
            await _bd.Db.MateriasCursadas.AsNoTracking().ToListAsync(), Array.Empty<CursoEnProgreso>(), Array.Empty<Equivalencia>());

        Assert.Empty(r.Materias);
    }

    [Fact]
    public async Task UnaMateriaReprobadaNoSeConvalida()
    {
        await HistoricoAsync(("AAA100", "F", 3));

        var r = CarreraService.Convalidar(Definicion("uni-prueba/derecho-2025.json"), ReglasUniversidad.Unapec,
            await _bd.Db.MateriasCursadas.AsNoTracking().ToListAsync(), Array.Empty<CursoEnProgreso>(), Array.Empty<Equivalencia>());

        Assert.Empty(r.Materias);
    }

    // ── La lista de carreras muestra lo que se convalidaría ───────────────────────────────

    [Fact]
    public async Task LaListaMuestraParaCadaPensumCuantoCubreTuHistorico()
    {
        await HistoricoAsync(("AAA100", "A", 3), ("BBB200", "A", 5));

        var v = await _s.ObtenerAsync("uni-prueba", "derecho");

        Assert.True(v.HayHistorico);
        Assert.Equal(new[] { "uni-prueba/derecho-2019.json", "uni-prueba/derecho-2023.json", "uni-prueba/derecho-2025.json" }, v.Convalidaciones.Keys.OrderBy(k => k));
        Assert.Equal(8, v.Convalidaciones["uni-prueba/derecho-2019.json"].CreditosAprobados);    // el plan 2019 las tiene directas
        Assert.Equal(0, v.Convalidaciones["uni-prueba/derecho-2019.json"].PorEquivalencia);
        Assert.Equal(0, v.Convalidaciones["uni-prueba/derecho-2023.json"].CreditosAprobados);    // el 2023 no las conoce
        Assert.Equal((2, 7), (v.Convalidaciones["uni-prueba/derecho-2025.json"].PorEquivalencia, v.Convalidaciones["uni-prueba/derecho-2025.json"].CreditosAprobados));
    }

    [Fact]
    public async Task SinHistoricoNoHayNadaQueConvalidar()
    {
        var v = await _s.ObtenerAsync(null, null);

        Assert.False(v.HayHistorico);
        Assert.Empty(v.Convalidaciones);
    }

    // ── Al cambiar de plan ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AlElegirElPlanNuevoElMensajeDiceQueSeConvalidaYElEstadoSeRecalcula()
    {
        await _s.ActivarAsync("uni-prueba/derecho-2019.json");
        await HistoricoAsync(("AAA100", "A", 3), ("BBB200", "A", 5));
        Assert.Equal(8, (await _academico.ObtenerAsync()).Pensum.CreditosAprobados);

        var r = await _s.ActivarAsync("uni-prueba/derecho-2025.json");
        var e = await _academico.ObtenerAsync();

        Assert.True(r.Ok, r.Mensaje);
        Assert.Contains("Se convalidan 2 materias por equivalencia: CCC100 (por AAA100), DDD200 (por BBB200).", r.Mensaje);
        Assert.Equal(EstadoMateria.Aprobada, e.Pensum.Buscar("CCC100")!.Estado);
        Assert.Equal("AAA100", e.Pensum.Buscar("CCC100")!.PorEquivalencia);
        Assert.Equal(EstadoMateria.Disponible, e.Pensum.Buscar("EEE300")!.Estado);
        Assert.Equal(7, e.Pensum.CreditosAprobados);
        Assert.Empty(await _bd.Db.Equivalencias.ToListAsync());                         // no se copió nada a las equivalencias personales
        Assert.Equal(3, e.EquivalenciasDeclaradas.Count);                                // AAA100→CCC100, BBB200→DDD200 y ZZZ999 sin equivalente
    }

    [Fact]
    public async Task ConUnaSolaMateriaConvalidadaElMensajeVaEnSingular()
    {
        await HistoricoAsync(("AAA100", "A", 3));

        var r = await _s.ActivarAsync("uni-prueba/derecho-2025.json");

        Assert.Contains("Se convalida 1 materia por equivalencia: CCC100 (por AAA100).", r.Mensaje);
    }

    [Fact]
    public async Task SiNoHayNadaQueConvalidarElMensajeNoLoMenciona()
    {
        await HistoricoAsync(("QUI100", "A", 3));

        var r = await _s.ActivarAsync("uni-prueba/derecho-2025.json");

        Assert.DoesNotContain("convalid", r.Mensaje);
    }

    [Fact]
    public async Task MuchasConvalidacionesSeResumenConLasPrimeras()
    {
        var muchas = Enumerable.Range(1, 8).Select(i => $"{{ \"codigo\": \"M{i:00}\", \"nombre\": \"M{i}\", \"creditos\": 1, \"cuatrimestre\": 1 }}").ToList();
        var equivalencias = Enumerable.Range(1, 8).Select(i => $"{{ \"origen\": \"O{i:00}\", \"destino\": [\"M{i:00}\"] }}");
        _catalogo.Escribir("uni-prueba", "grande-1.json",
            $$"""{ "formato": 1, "universidad": "uni-prueba", "carrera": "grande", "nombreCarrera": "Grande", "version": "1", "totalCreditos": 8, "cuatrimestres": 1, "materias": [ {{string.Join(",", muchas)}} ], "equivalencias": [ {{string.Join(",", equivalencias)}} ] }""");
        await HistoricoAsync(Enumerable.Range(1, 8).Select(i => ($"O{i:00}", "A", 1)).ToArray());

        var r = await _s.ActivarAsync("uni-prueba/grande-1.json");

        Assert.Contains("Se convalidan 8 materias por equivalencia: M01 (por O01), M02 (por O02), M03 (por O03), M04 (por O04), M05 (por O05), M06 (por O06) y 2 más.", r.Mensaje);
    }

    // ── El estado usa las equivalencias del pénsum activo ─────────────────────────────────

    [Fact]
    public async Task SinPensumDelCatalogoActivoNoHayEquivalenciasDeclaradas()
    {
        var e = await _academico.ObtenerAsync();

        Assert.Empty(e.EquivalenciasDeclaradas);
        Assert.Empty(e.Equivalencias);
    }

    [Fact]
    public async Task LasEquivalenciasPersonalesYLasDelPensumSeUsanJuntasSinRepetirse()
    {
        await _s.ActivarAsync("uni-prueba/derecho-2025.json");
        _bd.Db.Equivalencias.AddRange(
            new Equivalencia { CodigoBanner = "AAA100", CodigoPensum = "CCC100", Nota = "mía" },     // la misma que ya declara el pénsum
            new Equivalencia { CodigoBanner = "QUI100", CodigoPensum = "EEE300" });                   // solo mía
        await _bd.Db.SaveChangesAsync();

        var e = await _academico.ObtenerAsync();

        Assert.Equal(4, e.Equivalencias.Count);                                                     // 2 personales + BBB200→DDD200 y ZZZ999 del pénsum
        Assert.Equal(new[] { "CCC100" }, e.EquivalentesDe("AAA100"));
        Assert.Equal(new[] { "DDD200" }, e.EquivalentesDe("BBB200"));
        Assert.Equal(new[] { "EEE300" }, e.EquivalentesDe("QUI100"));
        Assert.True(e.SinEquivalente("ZZZ999"));
    }

    [Fact]
    public async Task UnCambioEnElArchivoDelPensumSeNotaSinReiniciar()
    {
        await _s.ActivarAsync("uni-prueba/derecho-2025.json");
        Assert.Equal(3, (await _academico.ObtenerAsync()).EquivalenciasDeclaradas.Count);

        _catalogo.Escribir("uni-prueba", "derecho-2025.json", Derecho2025.Replace("{ \"origen\": \"ZZZ999\", \"destino\": [] }", "{ \"origen\": \"YYY888\", \"destino\": [\"EEE300\"] }, { \"origen\": \"ZZZ999\", \"destino\": [] }"));

        var declaradas = (await _academico.ObtenerAsync()).EquivalenciasDeclaradas;
        Assert.Equal(4, declaradas.Count);
        Assert.Contains(declaradas, d => d is { CodigoBanner: "YYY888", CodigoPensum: "EEE300" });
    }

    // ── El catálogo se reutiliza mientras los archivos no cambien ─────────────────────────

    [Fact]
    public void ElCatalogoSeReutilizaSiNadaCambioYSeVuelveALeerSiCambia()
    {
        var a = _reglas.LeerCatalogo();
        var b = _reglas.LeerCatalogo();
        Assert.Same(a, b);

        _catalogo.Escribir("uni-prueba", "nuevo-1.json", CatalogoTemporal.Pensum("nuevo", "Nuevo", "1", ("ABC100", 3, 1, "")));
        var c = _reglas.LeerCatalogo();

        Assert.NotSame(a, c);
        Assert.Equal(a.PensumsValidos.Count() + 1, c.PensumsValidos.Count());
    }
}
