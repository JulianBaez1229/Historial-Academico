using HistorialAcademico.Core.Models;

namespace HistorialAcademico.Core;

/// <summary>
/// Comprueba que los datos leídos de Banner son coherentes entre sí antes de guardarlos:
/// las materias suman los totales de su período, los acumulados encadenan período a período y el
/// "Global" coincide con el último acumulado. Si algo no cuadra, la sincronización no toca la base de datos.
/// </summary>
public static class ValidadorHistorico
{
    private const decimal ToleranciaPga = 0.006m;

    /// <returns>Lista de problemas encontrados; vacía si todo cuadra.</returns>
    public static List<string> Validar(HistoricoBanner h)
    {
        var errores = new List<string>();
        TotalesBanner? anterior = null;

        foreach (var p in h.Periodos)
        {
            var t = p.TotalesPeriodo!;
            var acum = p.TotalesAcumulados!;

            Comparar(errores, $"{p.Nombre}: puntos de calidad de las materias vs total del período", p.Materias.Sum(m => m.PuntosCalidad), t.PuntosCalidad);
            // Si las materias suman MENOS que las Horas Intentadas, el lector se saltó filas: es un error. Si suman MÁS, Banner lista materias que no
            // cuenta como intentadas (convalidadas, excluidas, de un pénsum anterior…): los datos están completos y solo se avisa (ver Avisos).
            var horasMaterias = p.Materias.Sum(m => m.HorasCredito);
            if (horasMaterias < t.HorasIntentadas)
                errores.Add(FormattableString.Invariant($"{p.Nombre}: horas de las materias vs Horas Intentadas: {horasMaterias:0.000} < {t.HorasIntentadas:0.000} (falta alguna materia)."));

            var esperado = anterior is null ? t : Sumar(anterior, t);
            Comparar(errores, $"{p.Nombre}: acumulado de Horas Intentadas", acum.HorasIntentadas, esperado.HorasIntentadas);
            Comparar(errores, $"{p.Nombre}: acumulado de Horas Aprobadas", acum.HorasAprobadas, esperado.HorasAprobadas);
            Comparar(errores, $"{p.Nombre}: acumulado de Horas PGA", acum.HorasPga, esperado.HorasPga);
            Comparar(errores, $"{p.Nombre}: acumulado de Puntos de Calidad", acum.PuntosCalidad, esperado.PuntosCalidad);

            ComprobarPga(errores, $"{p.Nombre}: PGA del período", t);
            ComprobarPga(errores, $"{p.Nombre}: PGA acumulado", acum);
            anterior = acum;
        }

        var g = h.TotalGlobal!;
        if (anterior is not null)
        {
            Comparar(errores, "Global: Horas Aprobadas vs último acumulado", g.HorasAprobadas, anterior.HorasAprobadas);
            Comparar(errores, "Global: Horas PGA vs último acumulado", g.HorasPga, anterior.HorasPga);
            Comparar(errores, "Global: Puntos de Calidad vs último acumulado", g.PuntosCalidad, anterior.PuntosCalidad);
        }
        ComprobarPga(errores, "Global: PGA", g);
        return errores;
    }

    /// <summary>
    /// Cosas que no impiden guardar el histórico pero que conviene decir. Hoy: períodos donde las materias suman más horas que las «Horas Intentadas» que
    /// publica Banner. Pasa, por ejemplo, tras un cambio de pénsum, cuando Banner lista materias convalidadas o excluidas. Los puntos de calidad y los
    /// acumulados sí cuadran (si no, <see cref="Validar"/> ya lo habría rechazado). Se nombran las materias sin puntos de calidad del período,
    /// que son las que suelen ser la diferencia.
    /// </summary>
    public static List<string> Avisos(HistoricoBanner h)
    {
        var avisos = new List<string>();
        foreach (var p in h.Periodos)
        {
            var t = p.TotalesPeriodo;
            if (t is null) continue;
            var horasMaterias = p.Materias.Sum(m => m.HorasCredito);
            if (horasMaterias <= t.HorasIntentadas) continue;

            var sinPuntos = p.Materias.Where(m => m.HorasCredito > 0 && m.PuntosCalidad == 0)
                .Select(m => FormattableString.Invariant($"{m.Codigo} {m.HorasCredito:0.##} cr ({(string.IsNullOrEmpty(m.Calificacion) ? "sin nota" : m.Calificacion)})")).ToList();
            avisos.Add(FormattableString.Invariant(
                $"{p.Nombre}: las materias suman {horasMaterias:0.##} horas y Banner cuenta {t.HorasIntentadas:0.##} como intentadas") +
                (sinPuntos.Count > 0 ? $" (sin puntos de calidad: {string.Join(", ", sinPuntos.Take(6))})." : "."));
        }
        return avisos;
    }

    private static TotalesBanner Sumar(TotalesBanner a, TotalesBanner b) => new(
        a.HorasIntentadas + b.HorasIntentadas, a.HorasAprobadas + b.HorasAprobadas, a.HorasGanadas + b.HorasGanadas,
        a.HorasPga + b.HorasPga, a.PuntosCalidad + b.PuntosCalidad, 0);

    private static void Comparar(List<string> errores, string que, decimal obtenido, decimal esperado)
    {
        if (obtenido != esperado) errores.Add(FormattableString.Invariant($"{que}: {obtenido} ≠ {esperado}."));
    }

    /// <summary>El PGA publicado debe ser Puntos ÷ Horas PGA redondeado a 2 decimales.</summary>
    private static void ComprobarPga(List<string> errores, string que, TotalesBanner t)
    {
        if (t.HorasPga == 0) return;
        var calculado = t.PuntosCalidad / t.HorasPga;
        if (Math.Abs(calculado - t.Pga) > ToleranciaPga)
            errores.Add(FormattableString.Invariant($"{que}: Banner publica {t.Pga} pero {t.PuntosCalidad} ÷ {t.HorasPga} = {calculado:0.00}."));
    }
}
