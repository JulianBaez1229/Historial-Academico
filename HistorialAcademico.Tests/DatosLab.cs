using System.Text.RegularExpressions;
using HistorialAcademico.Core.Entities;
using HistorialAcademico.Core.Pensum;

namespace HistorialAcademico.Tests;

/// <summary>
/// Datos de prueba del motor, leídos de Fixtures/laboratorio-sintetico.md (secciones 7 y 8) y docs/pensum_iso_unapec.csv.
/// El historial es FICTICIO (las materias son las del pénsum, público; las calificaciones se sortearon): sirve como referencia
/// independiente de Banner, escrita a mano, y no contiene datos de ninguna persona real.
/// </summary>
public static class DatosLab
{
    private static string Docs
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && dir.GetFiles("*.sln").Length == 0) dir = dir.Parent;
            return Path.Combine(dir!.FullName, "docs");
        }
    }

    public static string RutaCsv => Path.Combine(Docs, "pensum_iso_unapec.csv");
    public static List<MateriaPensum> Pensum() => PensumCsvParser.Parse(File.ReadAllText(RutaCsv)).Materias;

    private static readonly Regex PeriodoRx = new(@"^\*\*(?<p>[A-Z]{3}-[A-Z]{3} \d{4})\*\*\s*$");
    private static readonly Regex EnProgresoRx = new(@"^\*\*Cursos en progreso\D+(?<p>[A-Z]{3}-[A-Z]{3} \d{4})");
    private static readonly Regex MateriaRx = new(@"^- (?<cod>[A-Z]+\d*) \| (?<nom>.+?) \| (?<cr>\d+)(?: \| (?<g>[A-F]))?");
    private static readonly Regex FaltanteRx = new(@"^- (?<cuat>\d+) \| (?<cod>[A-Z]+\d*) \| ");

    public record Lab(List<Periodo> Periodos, List<CursoEnProgreso> EnProgreso, List<string> Faltantes)
    {
        public List<MateriaCursada> Cursadas => Periodos.SelectMany(p => p.Materias).ToList();
    }

    public static Lab Cargar()
    {
        var lineas = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", "laboratorio-sintetico.md"));
        var periodos = new List<Periodo>();
        var progreso = new List<CursoEnProgreso>();
        var faltantes = new List<string>();
        Periodo? actual = null;
        string? periodoProgreso = null;
        var seccion = 0;

        foreach (var l in lineas)
        {
            if (l.StartsWith("## 7.")) { seccion = 7; continue; }
            if (l.StartsWith("## 8.")) { seccion = 8; continue; }
            if (l.StartsWith("## 9.")) break;

            if (seccion == 7)
            {
                if (PeriodoRx.Match(l) is { Success: true } mp)
                {
                    actual = new Periodo { Nombre = mp.Groups["p"].Value, Orden = periodos.Count + 1 };
                    periodos.Add(actual);
                    periodoProgreso = null;
                }
                else if (EnProgresoRx.Match(l) is { Success: true } me) { periodoProgreso = me.Groups["p"].Value; actual = null; }
                else if (MateriaRx.Match(l) is { Success: true } m)
                {
                    var cod = m.Groups["cod"].Value;
                    var cr = decimal.Parse(m.Groups["cr"].Value);
                    if (periodoProgreso is not null)
                        progreso.Add(new CursoEnProgreso { Periodo = periodoProgreso, Codigo = cod, Titulo = m.Groups["nom"].Value, HorasCredito = cr });
                    else if (actual is not null && m.Groups["g"].Success)
                        actual.Materias.Add(new MateriaCursada
                        {
                            Codigo = cod, Titulo = m.Groups["nom"].Value, HorasCredito = cr, Calificacion = m.Groups["g"].Value, Periodo = actual,
                        });
                }
            }
            else if (seccion == 8 && FaltanteRx.Match(l) is { Success: true } f)
                faltantes.Add(f.Groups["cod"].Value);
        }
        return new Lab(periodos, progreso, faltantes);
    }
}
