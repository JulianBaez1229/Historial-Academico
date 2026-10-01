using HistorialAcademico.Core.Indice;
using HistorialAcademico.Core.Universidad;

namespace HistorialAcademico.Core.Planificacion;

/// <summary>Una materia que todavía no tiene nota real (en curso o planificada), con la que la persona espera sacar.</summary>
public record FilaSimulacion(string Codigo, decimal Creditos, string? Letra);

public enum EstadoObjetivo
{
    /// <summary>Ya se le puso nota esperada a todo lo que falta: solo queda comparar el índice proyectado con el objetivo.</summary>
    TodoConNota,
    /// <summary>El objetivo ya está asegurado: se alcanza aunque lo que falta salga con la nota más baja que cuenta.</summary>
    Asegurado,
    /// <summary>Hace falta un promedio mínimo en lo que falta.</summary>
    Alcanzable,
    /// <summary>Ni con la nota más alta en todo lo que falta se llega.</summary>
    Imposible,
}

/// <param name="IndiceActual">El índice de hoy, según lo aprobado.</param>
/// <param name="IndiceProyectado">El índice si todo lo que tiene nota esperada sale como se espera (las demás materias no cuentan todavía).</param>
/// <param name="CreditosConNota">Créditos que cuentan para el índice y ya tienen nota esperada.</param>
/// <param name="CreditosPorDefinir">Créditos que cuentan para el índice y todavía no tienen nota esperada (incluye las materias sin planificar).</param>
/// <param name="PromedioNecesario">Promedio mínimo (en puntos) que hace falta en lo que falta para llegar al objetivo; null si no aplica.</param>
/// <param name="LetraMinima">La letra más baja con la que ese promedio se alcanza, si se saca la misma en todo (null si no aplica).</param>
/// <param name="MaximoPosible">El índice al que se llegaría con la nota más alta en todo lo que falta.</param>
public record ResultadoSimulacion(
    decimal IndiceActual, decimal IndiceProyectado, decimal CreditosConNota, decimal CreditosPorDefinir,
    EstadoObjetivo Estado, decimal? PromedioNecesario, string? LetraMinima, decimal MaximoPosible);

/// <summary>
/// Simulador de índice: con las notas que la persona espera sacar calcula el índice proyectado y qué promedio necesita en el resto para
/// llegar a un índice objetivo. Índice = Σ(puntos × créditos) / Σ(créditos que cuentan), igual que <see cref="CalculadoraIndice"/>: una nota que
/// no cuenta para el índice (la E de UNAPEC) no mueve nada.
/// </summary>
public static class SimuladorIndice
{
    private static string N2(decimal v) => v.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
    private static string Cr(decimal v) => v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Lo que se le dice a la persona sobre su objetivo, en una frase. El navegador recalcula lo mismo con cada cambio (planificador.js);
    /// los dos deben decir exactamente lo mismo, y una prueba lo comprueba.
    /// </summary>
    public static string Describir(ResultadoSimulacion r, decimal objetivo, string letraMaxima) => r.Estado switch
    {
        EstadoObjetivo.TodoConNota =>
            $"Le pusiste nota a todo lo que falta: tu índice quedaría en {N2(r.IndiceProyectado)}, {(r.IndiceProyectado >= objetivo ? "que alcanza" : "que no alcanza")} el objetivo de {N2(objetivo)}.",
        EstadoObjetivo.Asegurado =>
            $"El objetivo de {N2(objetivo)} ya lo tienes asegurado: se alcanza aunque lo que falta ({Cr(r.CreditosPorDefinir)} créditos) salga con la nota más baja.",
        EstadoObjetivo.Alcanzable =>
            $"Para llegar a {N2(objetivo)} necesitas un promedio de {N2(r.PromedioNecesario ?? 0)} en los {Cr(r.CreditosPorDefinir)} créditos que faltan por definir (por ejemplo, {r.LetraMinima} en todo).",
        _ =>
            $"El objetivo de {N2(objetivo)} no se alcanza: ni con {letraMaxima} en todo lo que falta ({Cr(r.CreditosPorDefinir)} créditos) llegarías a más de {N2(r.MaximoPosible)}.",
    };

    public static ResultadoSimulacion Calcular(
        TotalesIndice actual, IEnumerable<FilaSimulacion> filas, decimal creditosSinFila, decimal objetivo, EscalaCalificaciones escala)
    {
        var horas = actual.HorasPga;
        var puntos = actual.PuntosCalidad;
        decimal conNota = 0, porDefinir = Math.Max(0, creditosSinFila);

        foreach (var f in filas.Where(f => f.Creditos > 0))
        {
            var letra = escala.Buscar(f.Letra);
            if (letra is null) { porDefinir += f.Creditos; continue; }        // sin nota (o una letra que no existe): falta por definir
            if (!letra.CuentaParaIndice) continue;                             // exenta: aprueba pero no entra en el índice
            horas += f.Creditos;
            puntos += (letra.Puntos ?? 0) * f.Creditos;
            conNota += f.Creditos;
        }

        decimal Indice(decimal p, decimal h) => h == 0 ? 0 : escala.Redondear(p / h);
        var proyectado = Indice(puntos, horas);
        var maximoPuntos = escala.PuntosMaximos;
        var maximo = Indice(puntos + maximoPuntos * porDefinir, horas + porDefinir);

        if (porDefinir == 0)
            return new ResultadoSimulacion(actual.Indice, proyectado, conNota, porDefinir, EstadoObjetivo.TodoConNota, null, null, proyectado);

        // Promedio necesario x en lo que falta: (puntos + x·porDefinir) / (horas + porDefinir) ≥ objetivo.
        var necesario = (objetivo * (horas + porDefinir) - puntos) / porDefinir;
        var minimo = escala.Letras.Where(l => l.CuentaParaIndice).Select(l => l.Puntos ?? 0).DefaultIfEmpty(0).Min();

        // La comparación se hace sobre el índice REDONDEADO (el que se ve): un 3.496 se muestra 3.50 y cumple.
        if (Indice(puntos + minimo * porDefinir, horas + porDefinir) >= objetivo)
            return new ResultadoSimulacion(actual.Indice, proyectado, conNota, porDefinir, EstadoObjetivo.Asegurado, null, null, maximo);
        if (maximo < objetivo)
            return new ResultadoSimulacion(actual.Indice, proyectado, conNota, porDefinir, EstadoObjetivo.Imposible, necesario, null, maximo);

        var letraMinima = escala.Letras.Where(l => l.CuentaParaIndice && (l.Puntos ?? 0) >= necesario)
            .OrderBy(l => l.Puntos).Select(l => l.Letra).FirstOrDefault();
        return new ResultadoSimulacion(actual.Indice, proyectado, conNota, porDefinir, EstadoObjetivo.Alcanzable, Math.Round(necesario, 2, MidpointRounding.AwayFromZero), letraMinima, maximo);
    }
}
