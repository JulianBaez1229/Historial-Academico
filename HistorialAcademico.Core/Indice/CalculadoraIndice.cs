using HistorialAcademico.Core.Entities;
using HistorialAcademico.Core.Universidad;

namespace HistorialAcademico.Core.Indice;

/// <summary>Totales calculados con las reglas de <see cref="ReglasIndice"/>.</summary>
public record TotalesIndice(decimal HorasIntentadas, decimal HorasAprobadas, decimal HorasPga, decimal PuntosCalidad, decimal Indice);

public record IndicePeriodoCalculado(string Periodo, TotalesIndice DelPeriodo, TotalesIndice Acumulado);

public class ResultadoIndice
{
    public List<IndicePeriodoCalculado> Periodos { get; } = new();
    public TotalesIndice Global { get; set; } = new(0, 0, 0, 0, 0);
}

/// <summary>
/// Índice = Σ(puntos × créditos) / Σ(créditos que cuentan para el índice).
/// Se calcula a partir de las calificaciones (letras), no de los puntos que publica Banner,
/// para poder contrastarlo después con <see cref="ComparadorConBanner"/>.
/// </summary>
public static class CalculadoraIndice
{
    /// <param name="escala">La escala de calificaciones de la universidad; sin ella se usa la de UNAPEC.</param>
    public static ResultadoIndice Calcular(IEnumerable<Periodo> periodos, EscalaCalificaciones? escala = null)
    {
        escala ??= EscalaCalificaciones.Unapec;
        var r = new ResultadoIndice();
        decimal aIntentadas = 0, aAprobadas = 0, aPga = 0, aPuntos = 0;

        foreach (var p in periodos.OrderBy(x => x.Orden))
        {
            var t = Totales(p.Materias, escala);
            aIntentadas += t.HorasIntentadas; aAprobadas += t.HorasAprobadas; aPga += t.HorasPga; aPuntos += t.PuntosCalidad;
            r.Periodos.Add(new IndicePeriodoCalculado(p.Nombre, t, new TotalesIndice(aIntentadas, aAprobadas, aPga, aPuntos, Indice(aPuntos, aPga, escala))));
        }

        r.Global = new TotalesIndice(aIntentadas, aAprobadas, aPga, aPuntos, Indice(aPuntos, aPga, escala));
        return r;
    }

    public static TotalesIndice Totales(IEnumerable<MateriaCursada> materias, EscalaCalificaciones? escala = null)
    {
        escala ??= EscalaCalificaciones.Unapec;
        decimal intentadas = 0, aprobadas = 0, pga = 0, puntos = 0;
        foreach (var m in materias)
        {
            intentadas += m.HorasCredito;
            if (escala.CuentaComoAprobada(m.Calificacion)) aprobadas += m.HorasCredito;
            if (escala.CuentaParaIndice(m.Calificacion))
            {
                pga += m.HorasCredito;
                puntos += escala.PuntosPorLetra(m.Calificacion)!.Value * m.HorasCredito;
            }
        }
        return new TotalesIndice(intentadas, aprobadas, pga, puntos, Indice(puntos, pga, escala));
    }

    private static decimal Indice(decimal puntos, decimal horasPga, EscalaCalificaciones escala) =>
        horasPga == 0 ? 0 : escala.Redondear(puntos / horasPga);
}

/// <summary>Contrasta el cálculo propio con los totales que publica Banner y devuelve advertencias.</summary>
public static class ComparadorConBanner
{
    public static List<string> Comparar(ResultadoIndice calculado, IEnumerable<Periodo> periodosBanner, DatosAlumno? alumno)
    {
        var avisos = new List<string>();
        var banner = periodosBanner.OrderBy(p => p.Orden).ToList();

        for (var i = 0; i < banner.Count && i < calculado.Periodos.Count; i++)
        {
            var b = banner[i];
            var c = calculado.Periodos[i];
            Diferencia(avisos, b.Nombre, "Horas Intentadas", c.DelPeriodo.HorasIntentadas, b.HorasIntentadas);
            Diferencia(avisos, b.Nombre, "Horas Aprobadas", c.DelPeriodo.HorasAprobadas, b.HorasAprobadas);
            Diferencia(avisos, b.Nombre, "Horas PGA", c.DelPeriodo.HorasPga, b.HorasPga);
            Diferencia(avisos, b.Nombre, "Puntos de Calidad", c.DelPeriodo.PuntosCalidad, b.PuntosCalidad);
            Diferencia(avisos, b.Nombre, "PGA del período", c.DelPeriodo.Indice, b.Pga);
            Diferencia(avisos, b.Nombre, "PGA acumulado", c.Acumulado.Indice, b.AcumPga);
        }

        if (alumno is not null)
        {
            var g = calculado.Global;
            Diferencia(avisos, "Global", "Horas Aprobadas", g.HorasAprobadas, alumno.TotalHorasAprobadas);
            Diferencia(avisos, "Global", "Horas PGA", g.HorasPga, alumno.TotalHorasPga);
            Diferencia(avisos, "Global", "Puntos de Calidad", g.PuntosCalidad, alumno.TotalPuntosCalidad);
            Diferencia(avisos, "Global", "Índice", g.Indice, alumno.TotalPga);
        }
        return avisos;
    }

    private static void Diferencia(List<string> avisos, string donde, string que, decimal calculado, decimal banner)
    {
        if (calculado != banner)
            avisos.Add(FormattableString.Invariant($"{donde}: {que} calculado = {calculado:0.##} pero Banner publica {banner:0.##}."));
    }
}
