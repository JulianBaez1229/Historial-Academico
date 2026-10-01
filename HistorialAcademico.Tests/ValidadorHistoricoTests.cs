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
}
