using HistorialAcademico.Core.Entities;

namespace HistorialAcademico.Core.Horarios;

public enum TipoCelda { Clase, NoDisponible }

/// <summary>
/// Un rectángulo de la grilla semanal: una clase o una franja no disponible en un día, entre dos horas.
/// Si varias clases coinciden en el mismo día se reparten el ancho: <see cref="Carril"/> de <see cref="Carriles"/>.
/// </summary>
public record CeldaGrilla(
    DiasSemana Dia, int InicioMin, int FinMin, TipoCelda Tipo, string Etiqueta, string Detalle, bool ConChoque, string? Nrc, int Carril, int Carriles);

/// <summary>El calendario semanal (lunes a sábado; domingo solo si hay algo ese día) de un horario tentativo.</summary>
public class GrillaSemanal
{
    public static readonly DiasSemana[] DiasBase =
        { DiasSemana.Lunes, DiasSemana.Martes, DiasSemana.Miercoles, DiasSemana.Jueves, DiasSemana.Viernes, DiasSemana.Sabado };

    /// <summary>Hora (0-23) de la primera fila y hora en que termina la última.</summary>
    public const int HoraInicioMinima = 7;
    public const int HoraFinMinima = 21;

    public List<DiasSemana> Dias { get; init; } = new();
    public int HoraInicio { get; init; }
    public int HoraFin { get; init; }
    public List<CeldaGrilla> Celdas { get; init; } = new();
    public List<Choque> Choques { get; init; } = new();

    public int MinutosTotales => (HoraFin - HoraInicio) * 60;

    /// <summary>Clases que quedan fuera de la grilla por no tener día fijo u horas (la parte virtual): se listan aparte.</summary>
    public List<(SeccionBanner Seccion, BloqueBanner Bloque)> SinHorarioFijo { get; init; } = new();

    public static GrillaSemanal Construir(IEnumerable<SeccionBanner> secciones, IEnumerable<BloqueNoDisponible>? noDisponibles = null)
    {
        var lista = secciones.ToList();
        var choques = DetectorChoques.Entre(lista);
        var enChoque = choques.SelectMany(c => new[] { c.A, c.B }).ToHashSet();

        var bloques = new List<(DiasSemana Dia, int Ini, int Fin, TipoCelda Tipo, string Etiqueta, string Detalle, bool Choque, string? Nrc)>();
        var sinFijo = new List<(SeccionBanner, BloqueBanner)>();
        foreach (var s in lista)
            foreach (var b in s.Bloques)
            {
                if (b.SinDiaFijo || b.Inicio is null || b.Fin is null) { sinFijo.Add((s, b)); continue; }
                foreach (var dia in DiasBase.Append(DiasSemana.Domingo).Where(d => b.Dias.HasFlag(d)))
                    bloques.Add((dia, DetectorChoques.Minutos(b.Inicio.Value), DetectorChoques.Minutos(b.Fin.Value), TipoCelda.Clase,
                        $"{s.Codigo}-{s.Seccion}", string.IsNullOrWhiteSpace(b.Aula) ? s.Titulo : $"{s.Titulo} · {b.Edificio} {b.Aula}".Trim(), enChoque.Contains(s), s.Nrc));
            }
        foreach (var nd in noDisponibles ?? Enumerable.Empty<BloqueNoDisponible>())
            bloques.Add((nd.Dia, nd.DesdeMin, nd.HastaMin, TipoCelda.NoDisponible, "No disponible", "", false, null));

        var dias = DiasBase.ToList();
        if (bloques.Any(b => b.Dia == DiasSemana.Domingo)) dias.Add(DiasSemana.Domingo);

        var horaInicio = HoraInicioMinima;
        var horaFin = HoraFinMinima;
        if (bloques.Count > 0)
        {
            horaInicio = Math.Min(horaInicio, bloques.Min(b => b.Ini) / 60);
            horaFin = Math.Max(horaFin, (bloques.Max(b => b.Fin) + 59) / 60);
        }

        var celdas = new List<CeldaGrilla>();
        foreach (var dia in dias)
        {
            var delDia = bloques.Where(b => b.Dia == dia).ToList();
            celdas.AddRange(delDia.Where(b => b.Tipo == TipoCelda.NoDisponible)
                .Select(b => new CeldaGrilla(dia, b.Ini, b.Fin, b.Tipo, b.Etiqueta, b.Detalle, false, null, 0, 1)));
            celdas.AddRange(Carriles(dia, delDia.Where(b => b.Tipo == TipoCelda.Clase).OrderBy(b => b.Ini).ThenBy(b => b.Fin).ToList()));
        }

        return new GrillaSemanal { Dias = dias, HoraInicio = horaInicio, HoraFin = horaFin, Celdas = celdas, Choques = choques, SinHorarioFijo = sinFijo };
    }

    /// <summary>Reparte el ancho del día entre las clases que se cruzan: cada grupo de clases encadenadas usa tantos carriles como le hagan falta.</summary>
    private static IEnumerable<CeldaGrilla> Carriles(
        DiasSemana dia, List<(DiasSemana Dia, int Ini, int Fin, TipoCelda Tipo, string Etiqueta, string Detalle, bool Choque, string? Nrc)> clases)
    {
        var resultado = new List<CeldaGrilla>();
        var grupo = new List<(int Carril, (DiasSemana Dia, int Ini, int Fin, TipoCelda Tipo, string Etiqueta, string Detalle, bool Choque, string? Nrc) Clase)>();
        var finPorCarril = new List<int>();
        var finGrupo = int.MinValue;

        void Cerrar()
        {
            var carriles = Math.Max(1, finPorCarril.Count);
            resultado.AddRange(grupo.Select(g => new CeldaGrilla(dia, g.Clase.Ini, g.Clase.Fin, g.Clase.Tipo, g.Clase.Etiqueta, g.Clase.Detalle, g.Clase.Choque, g.Clase.Nrc, g.Carril, carriles)));
            grupo.Clear();
            finPorCarril.Clear();
        }

        foreach (var c in clases)
        {
            if (grupo.Count > 0 && c.Ini >= finGrupo) Cerrar();
            var carril = finPorCarril.FindIndex(fin => fin <= c.Ini);
            if (carril < 0) { finPorCarril.Add(c.Fin); carril = finPorCarril.Count - 1; }
            else finPorCarril[carril] = c.Fin;
            grupo.Add((carril, c));
            finGrupo = grupo.Count == 1 ? c.Fin : Math.Max(finGrupo, c.Fin);
        }
        if (grupo.Count > 0) Cerrar();
        return resultado;
    }
}
