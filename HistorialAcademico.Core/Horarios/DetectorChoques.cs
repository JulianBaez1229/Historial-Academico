using HistorialAcademico.Core.Entities;

namespace HistorialAcademico.Core.Horarios;

/// <summary>Dos secciones que coinciden en un día y una franja de horas.</summary>
public record Choque(SeccionBanner A, SeccionBanner B, DiasSemana Dias, TimeOnly Desde, TimeOnly Hasta);

/// <summary>
/// Choques de horario entre secciones y con las franjas en las que no puedo tomar clases.
/// Un bloque sin día fijo (la parte virtual asincrónica) o sin horas no ocupa tiempo y nunca choca.
/// Que una clase termine a las 10:00 y otra empiece a las 10:00 NO es un choque.
/// </summary>
public static class DetectorChoques
{
    public static int Minutos(TimeOnly t) => t.Hour * 60 + t.Minute;

    /// <summary>Si dos bloques coinciden: en qué días y entre qué horas. Los bloques con fechas que no se cruzan (medio período) no chocan.</summary>
    public static bool Solapan(BloqueBanner a, BloqueBanner b, out DiasSemana dias, out TimeOnly desde, out TimeOnly hasta)
    {
        dias = a.Dias & b.Dias;
        desde = hasta = default;
        if (a.SinDiaFijo || b.SinDiaFijo || dias == DiasSemana.Ninguno) return false;
        if (a.Inicio is null || a.Fin is null || b.Inicio is null || b.Fin is null) return false;
        if (!FechasSeCruzan(a, b)) return false;

        desde = a.Inicio.Value > b.Inicio.Value ? a.Inicio.Value : b.Inicio.Value;
        hasta = a.Fin.Value < b.Fin.Value ? a.Fin.Value : b.Fin.Value;
        return desde < hasta;
    }

    private static bool FechasSeCruzan(BloqueBanner a, BloqueBanner b)
    {
        if (a.FechaInicio is null || a.FechaFin is null || b.FechaInicio is null || b.FechaFin is null) return true;   // sin fechas: se asume todo el período
        return a.FechaInicio <= b.FechaFin && b.FechaInicio <= a.FechaFin;
    }

    /// <summary>Todos los choques entre las secciones dadas (cada pareja de bloques que coincide es un choque).</summary>
    public static List<Choque> Entre(IReadOnlyList<SeccionBanner> secciones)
    {
        var choques = new List<Choque>();
        for (var i = 0; i < secciones.Count; i++)
            for (var j = i + 1; j < secciones.Count; j++)
                foreach (var a in secciones[i].Bloques)
                    foreach (var b in secciones[j].Bloques)
                        if (Solapan(a, b, out var dias, out var desde, out var hasta))
                            choques.Add(new Choque(secciones[i], secciones[j], dias, desde, hasta));
        return choques;
    }

    /// <summary>Las franjas no disponibles con las que choca la sección (vacío si es compatible).</summary>
    public static List<BloqueNoDisponible> QueChocanConNoDisponibles(SeccionBanner seccion, IEnumerable<BloqueNoDisponible> noDisponibles)
    {
        var resultado = new List<BloqueNoDisponible>();
        foreach (var nd in noDisponibles)
            foreach (var b in seccion.Bloques)
            {
                if (b.SinDiaFijo || b.Inicio is null || b.Fin is null || !b.Dias.HasFlag(nd.Dia)) continue;
                if (Minutos(b.Inicio.Value) < nd.HastaMin && nd.DesdeMin < Minutos(b.Fin.Value)) { resultado.Add(nd); break; }
            }
        return resultado;
    }

    public static bool ChocaConNoDisponibles(SeccionBanner seccion, IEnumerable<BloqueNoDisponible> noDisponibles) =>
        QueChocanConNoDisponibles(seccion, noDisponibles).Count > 0;
}
