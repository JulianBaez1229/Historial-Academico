using System.Text;
using HistorialAcademico.Validador;

// Los mensajes llevan tildes y ñ: se escriben en UTF-8 aunque la consola use otra codificación.
Console.OutputEncoding = new UTF8Encoding(false);
var salida = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };
var error = new StreamWriter(Console.OpenStandardError(), new UTF8Encoding(false)) { AutoFlush = true };

return Comandos.Ejecutar(args, salida, error);
