using System.Text.Json;

namespace HistorialAcademico.Core.Planificacion;

/// <summary>
/// Cómo estaba la situación académica la última vez que se miró un plan: con qué pénsum y reglas, qué materias faltaban, cuál era el primer
/// período planificable y cuándo se graduaba la persona según ese plan. Se compara con la de ahora para decir qué cambió (por ejemplo, tras sincronizar).
/// </summary>
public record InstantaneaPlan(string Pensum, bool AsumirEnCurso, List<string> Pendientes, string? Primero, string? Graduacion)
{
    private static readonly JsonSerializerOptions Opciones = new() { PropertyNameCaseInsensitive = true };

    public string ATexto() => JsonSerializer.Serialize(this, Opciones);

    /// <summary>Lee lo guardado; cualquier cosa que no se entienda es «no hay nada guardado» (nunca lanza).</summary>
    public static InstantaneaPlan? DeTexto(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return null;
        try { return JsonSerializer.Deserialize<InstantaneaPlan>(texto, Opciones); }
        catch (JsonException) { return null; }
    }
}

/// <param name="Salieron">Estaban en el plan y ya no faltan (se aprobaron o se están cursando): se quitan del plan.</param>
/// <param name="Volvieron">Ya no estaban por cursar y ahora sí (no quedaron aprobadas): reaparecen en «Por planificar».</param>
public record CambiosPlan(
    List<string> Salieron, List<string> Volvieron, string? PrimeroAntes, string? PrimeroDespues, string? GraduacionAntes, string? GraduacionDespues)
{
    public bool PrimeroCambio => PrimeroAntes is not null && PrimeroAntes != PrimeroDespues;
    public bool Hay => Salieron.Count > 0 || Volvieron.Count > 0 || PrimeroCambio;

    private const int MaximoEnLista = 8;

    private static string Lista(IReadOnlyList<string> codigos) =>
        codigos.Count <= MaximoEnLista
            ? string.Join(", ", codigos)
            : $"{string.Join(", ", codigos.Take(MaximoEnLista))} y {codigos.Count - MaximoEnLista} más";

    /// <summary>Lo que se le cuenta a la persona, en frases cortas (vacío si no cambió nada).</summary>
    public List<string> Frases()
    {
        var frases = new List<string>();
        if (!Hay) return frases;
        if (Salieron.Count > 0) frases.Add($"Salieron del plan porque ya están aprobadas o en curso: {Lista(Salieron)}.");
        if (Volvieron.Count > 0) frases.Add($"Volvieron a «Por planificar» porque no quedaron aprobadas: {Lista(Volvieron)}.");
        if (PrimeroCambio) frases.Add($"El primer período planificable ahora es {PrimeroDespues}.");

        if (GraduacionAntes != GraduacionDespues && GraduacionDespues is not null)
            frases.Add($"Tu graduación estimada pasó de {GraduacionAntes ?? "—"} a {GraduacionDespues}.");
        else if (GraduacionDespues is not null)
            frases.Add($"Tu graduación estimada sigue en {GraduacionDespues}.");
        return frases;
    }
}

/// <summary>Compara la situación de antes con la de ahora para ajustar un plan tras un cambio (una sincronización, por ejemplo).</summary>
public static class ReconciliadorPlan
{
    /// <param name="antes">Lo guardado la última vez (null si es la primera vez que se mira).</param>
    /// <param name="salieron">Las materias del plan que dejaron de faltar (las calcula quien quita las materias del plan).</param>
    public static CambiosPlan Comparar(InstantaneaPlan? antes, InstantaneaPlan ahora, IEnumerable<string> salieron)
    {
        var quitadas = salieron.OrderBy(c => c, StringComparer.OrdinalIgnoreCase).ToList();

        // Otro pénsum u otras reglas de «asumir que apruebas lo que cursas»: la lista de lo que falta cambia por otra razón, no porque
        // se haya sincronizado. No hay nada que comparar; solo se cuentan las que salieron del plan.
        if (antes is null || antes.Pensum != ahora.Pensum || antes.AsumirEnCurso != ahora.AsumirEnCurso)
            return new CambiosPlan(quitadas, new(), null, ahora.Primero, null, ahora.Graduacion);

        var eranPendientes = antes.Pendientes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var volvieron = ahora.Pendientes.Where(c => !eranPendientes.Contains(c)).OrderBy(c => c, StringComparer.OrdinalIgnoreCase).ToList();
        return new CambiosPlan(quitadas, volvieron, antes.Primero, ahora.Primero, antes.Graduacion, ahora.Graduacion);
    }
}
