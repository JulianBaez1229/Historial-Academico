namespace HistorialAcademico.Tests;

/// <summary>Rutas de los archivos de prueba y utilidades para leerlos. Todos son datos ficticios o anonimizados: pueden estar en el repositorio.</summary>
public static class Muestras
{
    public static string Sintetico => Path.Combine(AppContext.BaseDirectory, "Fixtures", "historico_sintetico.html");

    /// <summary>
    /// tests/samples/historico-anonimizado.html: un histórico con la forma de uno real (varios períodos, exentas, cursos en progreso, códigos del plan anterior)
    /// pero con identidad y calificaciones ficticias; lo generó <c>validador-pensums anonimizar</c>. Los totales de las pruebas salen de este archivo.
    /// </summary>
    public static string Anonimizado => Path.Combine(AppContext.BaseDirectory, "Samples", "historico-anonimizado.html");

    // Siempre con "\n": en Windows Git puede dejar los archivos con CRLF y varias pruebas parten o reemplazan el HTML buscando saltos de línea.
    public static string LeerSintetico() => File.ReadAllText(Sintetico).ReplaceLineEndings("\n");
    public static string LeerAnonimizado() => File.ReadAllText(Anonimizado).ReplaceLineEndings("\n");
}
