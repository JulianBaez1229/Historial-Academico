using HistorialAcademico.Core.Pensum;

namespace HistorialAcademico.Tests;

/// <summary>Leer el texto de un plan de estudios pegado desde la página de una universidad.</summary>
public class ImportadorPensumTextoAnalisisTests
{
    private static ResultadoAnalisis A(string texto) => ImportadorPensumTexto.Analizar(texto);

    private static string Resumen(FilaEditable f) => $"{f.Codigo}|{f.Nombre}|{f.Creditos}|{f.Cuatrimestre}|{f.Prerrequisitos}|{(f.Electiva ? "E" : "")}";

    // ── Formatos de columnas ──────────────────────────────────────────────────────────────

    [Fact]
    public void UnaTablaCopiadaConTabuladoresEncabezadoYTitulosDeCuatrimestre()
    {
        var r = A("Código\tAsignatura\tCréditos\tPrerrequisitos\nCuatrimestre 1\nESP101\tAnálisis de Textos Discursivos\t3\t\nISO100\tFundamentos de Informática\t5\t\n" +
                  "Cuatrimestre 2\nESP106\tRedacción de Textos Discursivos I\t3\tESP101\nISO200\tProgramación y Estructura de Datos\t5\tISO100");

        Assert.Equal(new[]
        {
            "ESP101|Análisis de Textos Discursivos|3|1||", "ISO100|Fundamentos de Informática|5|1||",
            "ESP106|Redacción de Textos Discursivos I|3|2|ESP101|", "ISO200|Programación y Estructura de Datos|5|2|ISO100|",
        }, r.Filas.Select(Resumen));
        Assert.Empty(r.Avisos);
        Assert.False(r.CuatrimestresEstimados);
    }

    [Fact]
    public void ElEncabezadoFijaElOrdenDeLasColumnasAunqueSeaOtro()
    {
        var r = A("Asignatura\tCódigo\tPrerrequisito\tCrédito\tCuatrimestre\nFísica I\tING716\tMAT131\t3\t3\nLaboratorio de Física I\tING717\tMAT131\t1\t3");

        Assert.Equal(new[] { "ING716|Física I|3|3|MAT131|", "ING717|Laboratorio de Física I|1|3|MAT131|" }, r.Filas.Select(Resumen));
    }

    [Theory]
    [InlineData("ISO100 | Fundamentos de Informática | 5 |  ")]
    [InlineData("| ISO100 | Fundamentos de Informática | 5 | |")]
    [InlineData("ISO100;Fundamentos de Informática;5;")]
    [InlineData("ISO100    Fundamentos de Informática    5")]
    [InlineData("ISO100\tFundamentos de Informática\t5")]
    [InlineData("ISO100 Fundamentos de Informática 5")]
    public void LasColumnasPuedenIrSeparadasDeVariasManeras(string linea)
    {
        var f = Assert.Single(A(linea + "\n").Filas);

        Assert.Equal(("ISO100", "Fundamentos de Informática", "5"), (f.Codigo, f.Nombre, f.Creditos));
    }

    [Fact]
    public void UnaLineaConEspaciosSimplesSeSeparaEnCodigoNombreCreditosYPrerrequisitos()
    {
        var f = Assert.Single(A("ISO625 Programación Móvil 4 ISO515").Filas);

        Assert.Equal(("ISO625", "Programación Móvil", "4", "ISO515"), (f.Codigo, f.Nombre, f.Creditos, f.Prerrequisitos));
    }

    [Theory]
    [InlineData("ENG001 Inglés I 0", "Inglés I", "0", "")]
    [InlineData("MAT100 Cálculo 2 4 MAT099", "Cálculo 2", "4", "MAT099")]           // el 2 del nombre no se toma por los créditos
    [InlineData("ISO200 Programación 5 ISO100 67%", "Programación", "5", "ISO100; 67% créditos aprobados")]
    [InlineData("SOC281 Seminario de Grado 3 SOC253 90%", "Seminario de Grado", "3", "SOC253; 90% créditos aprobados")]
    [InlineData("ESP101 Análisis de Textos 3 -", "Análisis de Textos", "3", "")]
    public void ElNumeroDeCreditosSeDistingueDeLosNumerosDelNombre(string linea, string nombre, string creditos, string prerrequisitos)
    {
        var f = Assert.Single(A(linea).Filas);

        Assert.Equal((nombre, creditos, prerrequisitos), (f.Nombre, f.Creditos, f.Prerrequisitos));
    }

    [Fact]
    public void LosCodigosPuedenTenerEspaciosOGuiones()
    {
        var r = A("ISO 100\tFundamentos\t5\t\nISO-200\tProgramación\t5\tISO 100");

        Assert.Equal(new[] { "ISO100", "ISO200" }, r.Filas.Select(f => f.Codigo));
        Assert.Equal("ISO100", r.Filas[1].Prerrequisitos);
    }

    [Fact]
    public void LosCodigosSoloDeLetrasSeAceptanEnTablasConColumnas()
    {
        var r = A("Código\tAsignatura\tCréditos\nTFG\tTrabajo Final de Grado\t6\nODEP\tOptativa Deporte\t0");

        Assert.Equal(new[] { "TFG", "ODEP" }, r.Filas.Select(f => f.Codigo));
    }

    // ── Títulos de cuatrimestre ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Cuatrimestre 3", "3")]
    [InlineData("CUATRIMESTRE 3", "3")]
    [InlineData("Cuatrimestre III", "3")]
    [InlineData("Cuatrimestre IV", "4")]
    [InlineData("Semestre: 5", "5")]
    [InlineData("Semestre - 6", "6")]
    [InlineData("Tercer semestre", "3")]
    [InlineData("PRIMER CUATRIMESTRE", "1")]
    [InlineData("Décimo cuatrimestre", "10")]
    [InlineData("3er Cuatrimestre", "3")]
    [InlineData("2do semestre", "2")]
    [InlineData("IV Ciclo", "4")]
    [InlineData("Nivel 2", "2")]
    [InlineData("Trimestre 7", "7")]
    [InlineData("Período 12", "12")]
    public void ElTituloFijaElCuatrimestreDeLasMateriasQueSiguen(string titulo, string esperado)
    {
        var r = A($"{titulo}\nISO100\tFundamentos\t5\t");

        Assert.Equal(esperado, Assert.Single(r.Filas).Cuatrimestre);
    }

    [Fact]
    public void UnTituloNuevoCambiaElCuatrimestreYUnaColumnaDeCuatrimestreTienePrioridad()
    {
        var r = A("Código\tAsignatura\tCréditos\tPrerrequisitos\tCuatrimestre\nCuatrimestre 2\nAAA100\tUna\t3\t\t\nBBB100\tOtra\t3\t\t5\nCuatrimestre 3\nCCC100\tTercera\t3\t\t");

        Assert.Equal(new[] { "2", "5", "3" }, r.Filas.Select(f => f.Cuatrimestre));
    }

    [Fact]
    public void UnNombreDeMateriaQueEmpiezaComoUnTituloNoSeTomaPorTitulo()
    {
        var r = A("Código\tAsignatura\tCréditos\nNivel básico\nNIV100\tNivelación de Matemática\t3\nCiclo de introducción\nCICLO Básico");

        Assert.Equal(new[] { "NIV100" }, r.Filas.Select(f => f.Codigo));
        Assert.Equal("1", Assert.Single(r.Filas).Cuatrimestre);   // ninguno de esos textos fijó un cuatrimestre: se estimó
        Assert.Equal(4, r.Avisos.Count);                          // las tres líneas sueltas (saltadas) y el aviso de la estimación
    }

    // ── Prerrequisitos ────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("ISO 515, ISO-520 y 67% de créditos", "ISO515; ISO520; 67% créditos aprobados")]
    [InlineData("ISO100 / ISO101", "ISO100; ISO101")]
    [InlineData("ISO100; ISO101", "ISO100; ISO101")]
    [InlineData("iso100 + iso101", "ISO100; ISO101")]
    [InlineData("Haber aprobado el 59%", "59% créditos aprobados")]
    [InlineData("E077; 67% créditos aprobados", "E077; 67% créditos aprobados")]
    [InlineData("ISO100.", "ISO100")]
    [InlineData("Ninguno", "")]
    [InlineData("N/A", "")]
    [InlineData("-", "")]
    [InlineData("—", "")]
    [InlineData("No aplica", "")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void LosPrerrequisitosSeNormalizan(string? texto, string esperado) => Assert.Equal(esperado, ImportadorPensumTexto.NormalizarPrerrequisitos(texto));

    [Fact]
    public void LoQueNoSeReconoceComoPrerrequisitoSeConservaParaQueLaValidacionLoSeñale()
    {
        var normal = ImportadorPensumTexto.NormalizarPrerrequisitos("ISO100, Autorización del director");

        Assert.Equal("ISO100; Autorización del director", normal);
    }

    // ── Electivas ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("E077\tElectiva I\t3\t", true)]
    [InlineData("E078\tELECTIVA GENERAL II\t3\t", true)]
    [InlineData("ISO100\tFundamentos\t3\t", false)]
    public void UnaMateriaConElectivaEnElNombreSeMarcaComoElectiva(string linea, bool esperada) =>
        Assert.Equal(esperada, Assert.Single(A(linea).Filas).Electiva);

    [Fact]
    public void UnaColumnaDeElectivaTambienSeLee()
    {
        var r = A("Código\tAsignatura\tCréditos\tElectiva\nAAA100\tNormal\t3\tNo\nBBB100\tOtra cosa\t3\tSí");

        Assert.Equal(new[] { false, true }, r.Filas.Select(f => f.Electiva));
    }

    // ── Sin cuatrimestres ─────────────────────────────────────────────────────────────────

    [Fact]
    public void SiElTextoNoTraeCuatrimestresSeEstimanPorLosPrerrequisitosYSeAvisa()
    {
        var r = A("AAA100\tPrimera\t3\t\nBBB200\tSegunda\t3\tAAA100\nCCC300\tTercera\t3\tBBB200; 50%\nDDD100\tOtra sin requisitos\t3\t");

        Assert.Equal(new[] { "1", "2", "3", "1" }, r.Filas.Select(f => f.Cuatrimestre));
        Assert.True(r.CuatrimestresEstimados);
        Assert.Contains(r.Avisos, a => a.Contains("lo estimé según los prerrequisitos"));
    }

    [Fact]
    public void LaEstimacionAguantaCiclosYPrerrequisitosQueNoExisten()
    {
        var r = A("AAA100\tUna\t3\tBBB100\nBBB100\tOtra\t3\tAAA100\nCCC100\tTercera\t3\tZZZ999");

        Assert.All(r.Filas, f => Assert.InRange(int.Parse(f.Cuatrimestre), 1, 24));
        Assert.Equal("1", r.Filas[2].Cuatrimestre);
    }

    [Fact]
    public void SiAlgunaFilaTieneCuatrimestreNoSeEstimaNada()
    {
        var r = A("Cuatrimestre 4\nAAA100\tUna\t3\t\nBBB200\tOtra\t3\tAAA100");

        Assert.All(r.Filas, f => Assert.Equal("4", f.Cuatrimestre));
        Assert.False(r.CuatrimestresEstimados);
    }

    // ── Lo que no se entiende ─────────────────────────────────────────────────────────────

    [Fact]
    public void LasLineasQueNoSonMateriasSeSaltanConUnAviso()
    {
        var r = A("Plan de estudios 2019\nCuatrimestre 1\nISO100\tFundamentos\t5\t\nTotal de créditos: 5\n(*) Requisito de graduación\n");

        Assert.Single(r.Filas);
        Assert.Equal(3, r.Avisos.Count);
        Assert.Contains("Línea 1: no la entendí como una materia, la salté: «Plan de estudios 2019».", r.Avisos);
        Assert.Contains(r.Avisos, a => a.Contains("Línea 4") && a.Contains("Total de créditos"));
    }

    [Fact]
    public void LosAvisosSeLimitanYSeDiceCuantosMasHay()
    {
        var basura = string.Join("\n", Enumerable.Range(1, 40).Select(i => $"texto suelto número {i}"));

        var r = A("Cuatrimestre 1\nISO100\tFundamentos\t5\t\n" + basura);

        Assert.Equal(16, r.Avisos.Count);
        Assert.Equal("Y 25 líneas más que tampoco se entendieron como materias.", r.Avisos[^1]);
    }

    [Fact]
    public void CadaFilaRecuerdaLaLineaDeDondeSalio()
    {
        var r = A("Código\tAsignatura\tCréditos\n\nAAA100\tUna\t3\ntexto suelto\nBBB100\tOtra\t3");

        Assert.Equal(new[] { 3, 5 }, r.Filas.Select(f => f.Linea));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n \n")]
    [InlineData(null)]
    public void UnTextoVacioAvisaQueNoSePegoNada(string? texto)
    {
        var r = A(texto!);

        Assert.Empty(r.Filas);
        Assert.Equal(new[] { "No pegaste ningún texto." }, r.Avisos);
    }

    [Fact]
    public void UnTextoSinMateriasDiceQueSePegueLaTabla()
    {
        var r = A("Bienvenido a la universidad\nEste es un texto cualquiera");

        Assert.Empty(r.Filas);
        Assert.Contains(r.Avisos, a => a.StartsWith("No encontré ninguna materia."));
    }

    [Fact]
    public void UnTextoDemasiadoLargoSeRechazaYDemasiadasMateriasSeCortan()
    {
        Assert.Contains("demasiado largo", A(new string('x', ImportadorPensumTexto.MaxCaracteres + 1)).Avisos[0]);

        var muchas = string.Join("\n", Enumerable.Range(1, ImportadorPensumTexto.MaxFilas + 20).Select(i => $"M{i:0000}\tMateria {i}\t1\t"));
        var r = A(muchas);
        Assert.Equal(ImportadorPensumTexto.MaxFilas, r.Filas.Count);
        Assert.Contains(r.Avisos, a => a.Contains($"primeras {ImportadorPensumTexto.MaxFilas} materias"));
    }

    [Fact]
    public void FinesDeLineaWindowsMacYMarcaDeOrdenDeBytesNoMolestan()
    {
        var r = A("﻿Código\tAsignatura\tCréditos\r\nAAA100\tUna\t3\r\rBBB100\tOtra\t4\r\n");

        Assert.Equal(new[] { "AAA100", "BBB100" }, r.Filas.Select(f => f.Codigo));
    }

    [Fact]
    public void UnEncabezadoConSoloEspaciosSimplesTambienSeReconoce()
    {
        var r = A("Código Asignatura Créditos Prerrequisitos\nISO100 Fundamentos 5\nISO200 Programación 5 ISO100");

        Assert.Equal(new[] { "ISO100", "ISO200" }, r.Filas.Select(f => f.Codigo));
        Assert.Empty(r.Avisos.Where(a => a.Contains("Código Asignatura")));
    }

    // ── Piezas sueltas ────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("3", 3)] [InlineData("III", 3)] [InlineData("iv", 4)] [InlineData("XII", 12)] [InlineData("tercero", 3)]
    [InlineData("0", null)] [InlineData("25", null)] [InlineData("abc", null)] [InlineData("", null)]
    public void NumeroDePeriodo(string texto, int? esperado) => Assert.Equal(esperado, ImportadorPensumTexto.NumeroDePeriodo(texto));

    [Theory]
    [InlineData("Ingeniería de Software", "ingenieria-de-software")]
    [InlineData("  Derecho  ", "derecho")]
    [InlineData("Administración de Empresas (Plan 2019)", "administracion-de-empresas-plan-2019")]
    [InlineData("¡Ñandú & Cía!", "nandu-cia")]
    [InlineData("../../etc/passwd", "etc-passwd")]
    [InlineData("???", "")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Slug(string? texto, string esperado) => Assert.Equal(esperado, ImportadorPensumTexto.Slug(texto));

    // ── El pénsum real de UNAPEC, en varios formatos de texto ─────────────────────────────

    private static string TextoDeUnapec(Func<MateriaPensumFila, string> formato, bool conTitulos)
    {
        var lineas = new List<string>();
        var actual = 0;
        foreach (var m in DatosLab.Pensum().OrderBy(m => m.Cuatrimestre).ThenBy(m => m.Codigo))
        {
            if (conTitulos && m.Cuatrimestre != actual) { actual = m.Cuatrimestre; lineas.Add($"Cuatrimestre {actual}"); }
            lineas.Add(formato(new MateriaPensumFila(m.Codigo, m.Nombre, m.Creditos, m.Prerrequisitos ?? "")));
        }
        return string.Join("\n", lineas);
    }

    private record MateriaPensumFila(string Codigo, string Nombre, int Creditos, string Prerrequisitos);

    private static void ComprobarContraElPensumReal(ResultadoAnalisis r, bool cuatrimestresExactos = true)
    {
        var real = DatosLab.Pensum().ToDictionary(m => m.Codigo);
        Assert.Equal(real.Count, r.Filas.Count);
        foreach (var f in r.Filas)
        {
            var m = real[f.Codigo];
            Assert.Equal((m.Nombre, m.Creditos.ToString()), (f.Nombre, f.Creditos));
            Assert.Equal((m.Prerrequisitos ?? "").Replace(" ", ""), f.Prerrequisitos.Replace(" ", ""));
            Assert.Equal(m.EsElectiva, f.Electiva);
            if (cuatrimestresExactos) Assert.Equal(m.Cuatrimestre.ToString(), f.Cuatrimestre);
        }
    }

    [Fact]
    public void ElPensumDeUnapecEnTabuladoresConEncabezadoYTitulosSeLeeExactamente()
    {
        var texto = "Código\tAsignatura\tCréditos\tPrerrequisitos\n" + TextoDeUnapec(f => $"{f.Codigo}\t{f.Nombre}\t{f.Creditos}\t{f.Prerrequisitos}", conTitulos: true);

        var r = A(texto);

        ComprobarContraElPensumReal(r);
        Assert.Empty(r.Avisos);
    }

    [Fact]
    public void ElMismoPensumSeparadoPorBarrasOPuntoYComaSeLeeIgual()
    {
        ComprobarContraElPensumReal(A("| Código | Asignatura | Créditos | Prerrequisitos |\n" + TextoDeUnapec(f => $"| {f.Codigo} | {f.Nombre} | {f.Creditos} | {f.Prerrequisitos} |", true)));
        ComprobarContraElPensumReal(A("Código;Asignatura;Créditos;Prerrequisitos\n" + TextoDeUnapec(f => $"{f.Codigo};{f.Nombre};{f.Creditos};{f.Prerrequisitos}", true)));
    }

    [Fact]
    public void ElMismoPensumEnUnaSolaLineaPorMateriaSeLeeSalvoLosCodigosSoloDeLetras()
    {
        // «TFG» y «ODEP» no tienen números: sin columnas no se pueden distinguir del texto, y se avisan en vez de perderse en silencio.
        var texto = TextoDeUnapec(f => $"{f.Codigo} {f.Nombre} {f.Creditos} {f.Prerrequisitos}".Trim(), conTitulos: true);

        var r = A(texto);

        var real = DatosLab.Pensum().Where(m => m.Codigo is not ("TFG" or "ODEP")).ToList();
        Assert.Equal(real.Count, r.Filas.Count);
        Assert.All(r.Filas, f => Assert.Equal(real.Single(m => m.Codigo == f.Codigo).Cuatrimestre.ToString(), f.Cuatrimestre));
        Assert.Contains(r.Avisos, a => a.Contains("ODEP"));
        Assert.Contains(r.Avisos, a => a.Contains("TFG"));
    }

    [Fact]
    public void ElPensumSinTitulosDeCuatrimestreSeEstimaYQuedaValido()
    {
        var r = A("Código\tAsignatura\tCréditos\tPrerrequisitos\n" + TextoDeUnapec(f => $"{f.Codigo}\t{f.Nombre}\t{f.Creditos}\t{f.Prerrequisitos}", conTitulos: false));

        Assert.True(r.CuatrimestresEstimados);
        ComprobarContraElPensumReal(r, cuatrimestresExactos: false);
        var construccion = ImportadorPensumTexto.Construir(r.Filas, new MetadatosPensum("unapec", "personal-prueba", "Prueba", "1"));
        Assert.True(construccion.EsValido, string.Join(" | ", construccion.Resultado.Errores));
        Assert.Equal(218, construccion.TotalCreditos);
    }
}

/// <summary>Armar y validar el pénsum con las filas de la vista previa.</summary>
public class ImportadorPensumTextoConstruccionTests
{
    private static readonly MetadatosPensum Meta = new("uni-prueba", "personal-derecho", "Derecho", "2022");

    private static FilaEditable F(string codigo, string nombre, string creditos, string cuat, string prer = "", bool electiva = false, bool quitar = false) =>
        new() { Codigo = codigo, Nombre = nombre, Creditos = creditos, Cuatrimestre = cuat, Prerrequisitos = prer, Electiva = electiva, Quitar = quitar };

    [Fact]
    public void UnasFilasValidasDanUnPensumValidoConSusTotales()
    {
        var r = ImportadorPensumTexto.Construir(new[] { F("AAA100", "Una", "3", "1"), F("BBB200", "Otra", "5", "2", "AAA100"), F("E001", "Electiva I", "3", "2", "", true) }, Meta);

        Assert.True(r.EsValido, string.Join(" | ", r.Resultado.Errores));
        Assert.Equal((11, 2), (r.TotalCreditos, r.Cuatrimestres));
        var d = r.Resultado.Definicion!;
        Assert.Equal(("uni-prueba/personal-derecho-2022.json", true), (d.Clave, d.EsPersonal));
        Assert.Equal(new[] { "AAA100" }, d.Materias.Single(m => m.Codigo == "BBB200").Prerrequisitos);
        Assert.True(d.Materias.Single(m => m.Codigo == "E001").Electiva);
    }

    [Fact]
    public void LasFilasQuitadasYLasEnBlancoNoCuentan()
    {
        var r = ImportadorPensumTexto.Construir(new[]
        {
            F("AAA100", "Una", "3", "1"), F("ZZZ999", "Quitada", "x", "1", quitar: true), new FilaEditable(), F("  ", "", "", ""),
        }, Meta);

        Assert.True(r.EsValido, string.Join(" | ", r.Resultado.Errores));
        Assert.Single(r.Resultado.Definicion!.Materias);
    }

    [Fact]
    public void SinNingunaMateriaNoHayPensum()
    {
        var r = ImportadorPensumTexto.Construir(new[] { F("AAA100", "Una", "3", "1", quitar: true), new FilaEditable() }, Meta);

        Assert.False(r.EsValido);
        Assert.Contains("El pénsum no tiene ninguna materia.", r.ErroresGenerales);
    }

    [Fact]
    public void LosProblemasDeUnaFilaSeDevuelvenConEsaFila()
    {
        var mala = F("", "", "abc", "");
        var buena = F("AAA100", "Una", "3", "1");

        var r = ImportadorPensumTexto.Construir(new[] { buena, mala }, Meta);

        Assert.False(r.EsValido);
        Assert.DoesNotContain(buena, r.ErroresPorFila.Keys);
        Assert.Equal(new[] { "Falta el código.", "Falta el nombre de la asignatura.", "Los créditos «abc» no son un número entero.", "Falta el cuatrimestre." }, r.ErroresPorFila[mala]);
    }

    [Theory]
    [InlineData("3.5")]
    [InlineData("-2")]
    [InlineData("")]
    [InlineData("tres")]
    public void LosCreditosDebenSerEnterosNoNegativos(string creditos)
    {
        var f = F("AAA100", "Una", creditos, "1");

        var r = ImportadorPensumTexto.Construir(new[] { f }, Meta);

        Assert.False(r.EsValido);
        Assert.Contains(r.ErroresPorFila[f], e => e.Contains("créditos"));
    }

    [Fact]
    public void ElCuatrimestreDebeSerUnNumeroDentroDelRango()
    {
        var texto = F("AAA100", "Una", "3", "tercero");
        var fuera = F("BBB100", "Otra", "3", "30");

        var r = ImportadorPensumTexto.Construir(new[] { texto, fuera }, Meta);

        Assert.Contains(r.ErroresPorFila[texto], e => e.Contains("no es un número"));
        Assert.False(r.EsValido);
    }

    [Fact]
    public void UnCodigoRepetidoUnPrerrequisitoQueNoExisteYUnCicloSeRelacionanConSuFila()
    {
        var a = F("AAA100", "Una", "3", "1");
        var repetida = F("AAA100", "Repetida", "3", "1");
        var huerfana = F("CCC300", "Huérfana", "3", "2", "ZZZ999");
        var ciclo1 = F("DDD100", "Ciclo uno", "3", "1", "EEE100");
        var ciclo2 = F("EEE100", "Ciclo dos", "3", "1", "DDD100");

        var r = ImportadorPensumTexto.Construir(new[] { a, repetida, huerfana, ciclo1, ciclo2 }, Meta);

        Assert.False(r.EsValido);
        Assert.Contains(r.ErroresPorFila[huerfana], e => e.Contains("ZZZ999 no existe"));
        Assert.True(r.ErroresPorFila.ContainsKey(a) || r.ErroresPorFila.ContainsKey(repetida) || r.ErroresGenerales.Any(e => e.Contains("AAA100")));
        Assert.Contains(r.ErroresGenerales.Concat(r.ErroresPorFila.Values.SelectMany(v => v)), e => e.Contains("ciclo"));
    }

    [Fact]
    public void LosMensajesDeFilaNoRepitenElIndiceInternoDeLaValidacion()
    {
        var f = F("AAA100", "Una", "30", "1");   // créditos fuera de rango: 0 a 20

        var r = ImportadorPensumTexto.Construir(new[] { f }, Meta);

        var mensaje = Assert.Single(r.ErroresPorFila[f]);
        Assert.DoesNotContain("materias[", mensaje);
        Assert.Contains("entre 0 y 20", mensaje);
    }

    [Fact]
    public void ElOrdenDeLasFilasSeConservaEnElPensum()
    {
        var r = ImportadorPensumTexto.Construir(new[] { F("CCC100", "C", "1", "2"), F("AAA100", "A", "1", "1"), F("BBB100", "B", "1", "1") }, Meta);

        Assert.Equal(new[] { "CCC100", "AAA100", "BBB100" }, r.Resultado.Definicion!.Materias.Select(m => m.Codigo));
    }

    [Fact]
    public void LosCodigosSeNormalizanAlConstruir()
    {
        var r = ImportadorPensumTexto.Construir(new[] { F(" iso-100 ", "Una", " 5 ", " 1 ", " ") }, Meta);

        Assert.True(r.EsValido, string.Join(" | ", r.Resultado.Errores));
        Assert.Equal("ISO100", r.Resultado.Definicion!.Materias[0].Codigo);
    }
}
