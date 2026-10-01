using System.Globalization;

namespace HistorialAcademico.Core.Planificacion;

/// <summary>
/// Configuración del planificador. Límite de créditos por cuatrimestre: 25 normal, 27 si el índice supera 3.40.
/// El mínimo solo genera una alerta (0 = sin alerta) y no se aplica al último período del plan.
/// </summary>
public record ConfigPlanificador(
    int LimiteBase = 25,
    int LimiteAlto = 27,
    decimal UmbralIndice = 3.40m,
    int Minimo = 0,
    bool AsumirEnCurso = true)
{
    /// <summary>El límite alto solo aplica cuando el índice es estrictamente mayor que el umbral.</summary>
    public int LimiteEfectivo(decimal indice) => indice > UmbralIndice ? LimiteAlto : LimiteBase;

    public string ExplicarLimite(decimal indice)
    {
        var inv = CultureInfo.InvariantCulture;
        return indice > UmbralIndice
            ? string.Create(inv, $"Tu índice ({indice:0.00}) supera {UmbralIndice:0.00}: límite de {LimiteAlto} créditos por cuatrimestre.")
            : string.Create(inv, $"Tu índice ({indice:0.00}) no supera {UmbralIndice:0.00}: límite de {LimiteBase} créditos por cuatrimestre ({LimiteAlto} si lo superas).");
    }
}
