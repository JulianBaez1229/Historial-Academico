using HistorialAcademico.Banner;
using HistorialAcademico.Core;
using Xunit;

namespace HistorialAcademico.Tests;

public class ValidadorHistoricoTests
{
    private static List<string> Validar(string html) => ValidadorHistorico.Validar(HistoricoParser.Parse(html));

    [Fact]
    public void ElHistoricoSinteticoCuadra() => Assert.Empty(Validar(Muestras.LeerSintetico()));

    [Fact]
    public void DetectaPuntosDeUnaMateriaQueNoSumanElTotalDelPeriodo()
    {
        // MAT101 pasa de 12.00 a 15.00 puntos: el período ya no suma 24.00.
        var html = Muestras.LeerSintetico().Replace("<td class=\"dddefault\">3.000</td><td class=\"dddefault\">12.00</td>", "<td class=\"dddefault\">3.000</td><td class=\"dddefault\">15.00</td>");
        var errores = Validar(html);
        Assert.Contains(errores, e => e.Contains("ENE-ABR 2025") && e.Contains("puntos de calidad"));
    }

    [Fact]
    public void DetectaUnAcumuladoQueNoEncadena()
    {
        // El acumulado de MAY-AGO 2025 dice 31.00 puntos en vez de 30.00.
        var html = Muestras.LeerSintetico().Replace(
            "<td class=\"dddefault\">13.000</td><td class=\"dddefault\">10.000</td><td class=\"dddefault\">10.000</td><td class=\"dddefault\">13.000</td><td class=\"dddefault\">30.00</td><td class=\"dddefault\" colspan=\"2\">2.31</td></tr>\n<tr><td class=\"ddseparator\" colspan=\"11\">&nbsp;</td></tr>\n<tr><td class=\"ddseparator\" colspan=\"4\">",
            "<td class=\"dddefault\">13.000</td><td class=\"dddefault\">10.000</td><td class=\"dddefault\">10.000</td><td class=\"dddefault\">13.000</td><td class=\"dddefault\">31.00</td><td class=\"dddefault\" colspan=\"2\">2.31</td></tr>\n<tr><td class=\"ddseparator\" colspan=\"11\">&nbsp;</td></tr>\n<tr><td class=\"ddseparator\" colspan=\"4\">");
        Assert.Contains(Validar(html), e => e.Contains("acumulado de Puntos de Calidad"));
    }

    [Fact]
    public void DetectaUnPgaPublicadoQueNoCorrespondeALosPuntos()
    {
        var html = Muestras.LeerSintetico().Replace("<td class=\"dddefault\" colspan=\"2\">3.43</td>", "<td class=\"dddefault\" colspan=\"2\">3.99</td>");
        Assert.Contains(Validar(html), e => e.Contains("PGA"));
    }

    [Fact]
    public void ElHistoricoRealCuadra() =>
        Assert.Empty(ValidadorHistorico.Validar(HistoricoParser.Parse(Muestras.LeerAnonimizado())));

    // ── Banner que cuenta menos horas intentadas que las materias que lista (p. ej. tras un cambio de pénsum) ──

    /// <summary>
    /// Cambia las «Horas Intentadas» que publica Banner en el período <paramref name="periodo"/> (0 = el primero): las baja o las sube en
    /// <paramref name="cambio"/> y arrastra el cambio a los acumulados de ese período y de los siguientes, como lo haría Banner.
    /// </summary>
    internal static string ConHorasIntentadasCambiadas(string html, int periodo, decimal cambio)
    {
        static string Formato(decimal v) => v.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);
        var n = -1;
        html = System.Text.RegularExpressions.Regex.Replace(html, @"(Periodo Actual</th><td class=""dddefault"">)(\d+\.\d{3})(</td>)", m =>
            ++n == periodo ? m.Groups[1].Value + Formato(decimal.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture) + cambio) + m.Groups[3].Value : m.Value);
        var k = -1;
        return System.Text.RegularExpressions.Regex.Replace(html, @"(Acumulativo:</th><td class=""dddefault"">)(\d+\.\d{3})(</td>)", m =>
            ++k >= periodo ? m.Groups[1].Value + Formato(decimal.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture) + cambio) + m.Groups[3].Value : m.Value);
    }

    [Fact]
    public void SiLasMateriasSumanMasQueLasHorasIntentadasDeBannerNoEsUnErrorPeroSeAvisa()
    {
        var html = ConHorasIntentadasCambiadas(Muestras.LeerAnonimizado(), periodo: 2, cambio: -5m);   // ENE-ABR 2025: Banner cuenta 22 en vez de 27
        var h = HistoricoParser.Parse(html);

        Assert.Empty(ValidadorHistorico.Validar(h));                       // los datos están completos: se puede guardar
        var aviso = Assert.Single(ValidadorHistorico.Avisos(h));
        Assert.StartsWith("ENE-ABR 2025: las materias suman 27 horas y Banner cuenta 22 como intentadas", aviso);
        Assert.Contains("sin puntos de calidad:", aviso);                  // y se nombran las candidatas a ser la diferencia
        Assert.Contains("ESP106 3 cr (E)", aviso);
    }

    [Fact]
    public void ElHistoricoQueCuadraNoTieneAvisos()
    {
        Assert.Empty(ValidadorHistorico.Avisos(HistoricoParser.Parse(Muestras.LeerAnonimizado())));
        Assert.Empty(ValidadorHistorico.Avisos(HistoricoParser.Parse(Muestras.LeerSintetico())));
    }

    [Fact]
    public void SiLasMateriasSumanMenosQueLasHorasIntentadasSigueSiendoUnErrorPorqueFaltaUnaMateria()
    {
        var html = ConHorasIntentadasCambiadas(Muestras.LeerAnonimizado(), periodo: 2, cambio: +3m);

        var errores = Validar(html);

        var error = Assert.Single(errores);
        Assert.Contains("ENE-ABR 2025: horas de las materias vs Horas Intentadas", error);
        Assert.Contains("falta alguna materia", error);
    }

    [Fact]
    public void LosPuntosQueNoCuadranSiguenSiendoUnErrorAunqueLasHorasSobren()
    {
        var html = ConHorasIntentadasCambiadas(Muestras.LeerAnonimizado(), periodo: 0, cambio: -4m)
            .Replace("<td class=\"dddefault\" colspan=\"2\">2.62</td>", "<td class=\"dddefault\" colspan=\"2\">3.62</td>");   // el PGA acumulado y el global

        Assert.Contains(Validar(html), e => e.Contains("PGA"));
    }
}
