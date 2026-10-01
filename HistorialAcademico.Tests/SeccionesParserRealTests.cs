using HistorialAcademico.Banner;
using HistorialAcademico.Core.Horarios;

namespace HistorialAcademico.Tests;

/// <summary>
/// [Fact] que se omite cuando samples/horarios/ no existe (datos reales de Banner con nombres de profesores; fuera del repo).
/// Comprueba que el parser entiende las respuestas reales. No imprime ningún dato personal.
/// </summary>
public sealed class SeccionesRealFactAttribute : FactAttribute
{
    public static string Ruta(string archivo)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && dir.GetFiles("*.sln").Length == 0) dir = dir.Parent;
        return Path.Combine(dir?.FullName ?? AppContext.BaseDirectory, "samples", "horarios", archivo);
    }

    public SeccionesRealFactAttribute()
    {
        if (!File.Exists(Ruta("05-secciones-202630-ISO.json")))
            Skip = "Faltan las muestras reales samples/horarios/ (fuera del repositorio). Usa POST /banner/explorar-horarios para generarlas.";
    }
}

public class SeccionesParserRealTests
{
    private static ResultadoBusqueda Real() => SeccionesParser.Parse(File.ReadAllText(SeccionesRealFactAttribute.Ruta("05-secciones-202630-ISO.json")));

    [SeccionesRealFact]
    public void LeeUnaPaginaRealCompletaSinFallar()
    {
        var r = Real();

        Assert.True(r.Total >= r.Secciones.Count);
        Assert.NotEmpty(r.Secciones);
        Assert.All(r.Secciones, s =>
        {
            Assert.Matches(@"^[A-Z]{2,4}\d{3}$", s.Codigo);
            Assert.NotEmpty(s.Nrc);
            Assert.True(s.Creditos > 0, $"{s.Codigo} sin créditos");
            Assert.True(s.CupoMaximo >= 0);        // un curso especial (p. ej. sección «TU1») puede tener cupo 0
            Assert.False(s.Llena && s.SinCupoAsignado);
            Assert.DoesNotContain("&", s.Titulo);          // las entidades HTML ya vienen decodificadas
            Assert.All(s.Profesores, p => Assert.DoesNotContain("@", p));
        });
    }

    [SeccionesRealFact]
    public void LasHorasYLosDiasDeLosBloquesRealesSonCoherentes()
    {
        var bloques = Real().Secciones.SelectMany(s => s.Bloques).ToList();

        Assert.NotEmpty(bloques);
        Assert.Contains(bloques, b => !b.SinDiaFijo);
        Assert.All(bloques.Where(b => b.Inicio is not null && b.Fin is not null), b =>
        {
            Assert.True(b.Inicio < b.Fin, $"{b.Inicio} no es anterior a {b.Fin}");
            Assert.True(b.Inicio!.Value.Minute % 5 == 0, "la hora de inicio no se normalizó (0801 → 08:00)");
        });
        Assert.All(bloques.Where(b => b.FechaInicio is not null && b.FechaFin is not null), b => Assert.True(b.FechaInicio <= b.FechaFin));
    }

    [SeccionesRealFact]
    public void ElJsonRealNoDejaCorreosNiMatriculasEnElResultado()
    {
        var serializado = System.Text.Json.JsonSerializer.Serialize(Real());

        Assert.DoesNotContain("@", serializado);
        Assert.DoesNotContain("emailAddress", serializado, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("bannerId", serializado, StringComparison.OrdinalIgnoreCase);
    }

    [SeccionesRealFact]
    public void ElPeriodoSinSeccionesPublicadasSeLeeComoVacio()
    {
        var ruta = SeccionesRealFactAttribute.Ruta("05-secciones-202710-ISO.json");
        if (!File.Exists(ruta)) return;   // esa muestra solo existe si se capturó un período futuro sin publicar

        Assert.True(SeccionesParser.Parse(File.ReadAllText(ruta)).SinSecciones);
    }
}
