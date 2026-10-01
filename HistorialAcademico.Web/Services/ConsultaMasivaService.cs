using HistorialAcademico.Core.Horarios;
using HistorialAcademico.Web.Perfiles;

namespace HistorialAcademico.Web.Services;

/// <summary>Foto del avance de una consulta a Banner de varias materias (la que muestra la pantalla).</summary>
public record EstadoConsultaMasiva(
    bool Activa,
    string Periodo,
    string PeriodoNombre,
    int Total,
    string Fase,
    string? Actual,
    IReadOnlyList<ItemLote> Items,
    bool Terminada,
    bool Cancelada,
    bool Exito,
    bool RequiereLogin,
    string Mensaje,
    DateTime Inicio,
    DateTime? Fin)
{
    public int Hechas => Items.Count;
    public int Porcentaje => Total == 0 ? 100 : (int)Math.Round(100.0 * Hechas / Total);
    public int ConSecciones => Items.Count(i => i.Resultado == ResultadoItem.ConSecciones);
    public int SinSecciones => Items.Count(i => i.Resultado == ResultadoItem.SinSecciones);
    public int ConError => Items.Count(i => i.Resultado == ResultadoItem.Error);
    public int Omitidas => Items.Count(i => i.Resultado == ResultadoItem.Omitida);
    public int TotalSecciones => Items.Sum(i => i.Secciones);
}

/// <summary>
/// Consulta a Banner, a petición tuya, todas las materias disponibles de un período, una detrás de otra y con pausa entre
/// ellas. Corre mientras la aplicación está abierta y muestra el avance; no se repite sola ni queda programada: cada consulta
/// nace de pulsar el botón, y solo puede haber una a la vez por perfil (cada perfil tiene su propia consulta y su propio resumen).
/// Se puede cancelar.
/// </summary>
public class ConsultaMasivaService
{
    private sealed class Trabajo
    {
        public CancellationTokenSource? Cancelacion;
        public EstadoConsultaMasiva? Estado;
        public Task Tarea = Task.CompletedTask;
    }

    private readonly IServiceScopeFactory _ambitos;
    private readonly object _candado = new();
    private readonly Dictionary<string, Trabajo> _trabajos = new();

    public ConsultaMasivaService(IServiceScopeFactory ambitos) => _ambitos = ambitos;

    private static string Clave(PerfilActual? perfil) => perfil?.Id ?? "";

    private Trabajo De(string clave) => _trabajos.TryGetValue(clave, out var t) ? t : _trabajos[clave] = new Trabajo();

    /// <summary>Las tareas de las consultas en curso (o las últimas), de todos los perfiles; las pruebas las esperan.</summary>
    public Task Tarea { get { lock (_candado) return Task.WhenAll(_trabajos.Values.Select(t => t.Tarea).ToList()); } }

    /// <summary>La consulta sin perfil (la de las pruebas del servicio solo).</summary>
    public EstadoConsultaMasiva? Estado => EstadoDe(null);

    /// <summary>La consulta en curso del perfil, o el resumen de la última que terminó en esta ejecución (null si no hubo ninguna).</summary>
    public EstadoConsultaMasiva? EstadoDe(PerfilActual? perfil) { lock (_candado) return _trabajos.TryGetValue(Clave(perfil), out var t) ? t.Estado : null; }

    /// <summary>Empieza la consulta. Devuelve false, con el motivo, si ya hay una en curso o no hay nada que consultar.</summary>
    public (bool Iniciada, string Mensaje) Iniciar(IReadOnlyList<(string Codigo, string Nombre)> materias, string periodo, bool omitirConsultadas, PerfilActual? perfil = null)
    {
        if (!MapeoBanner.TryPeriodoDeCodigo(periodo, out var p)) return (false, $"El período «{periodo}» no es de Grado.");
        if (materias.Count == 0) return (false, "No hay materias disponibles que consultar.");

        lock (_candado)
        {
            var clave = Clave(perfil);
            var trabajo = De(clave);
            if (trabajo.Estado is { Activa: true }) return (false, "Ya hay una consulta a Banner en curso. Espera a que termine o cancélala.");

            trabajo.Cancelacion?.Dispose();
            trabajo.Cancelacion = new CancellationTokenSource();
            var token = trabajo.Cancelacion.Token;
            trabajo.Estado = new EstadoConsultaMasiva(true, periodo, p.Nombre, materias.Count, "Preparando la consulta…", null,
                new List<ItemLote>(), false, false, false, false, "", DateTime.UtcNow, null);
            trabajo.Tarea = Task.Run(() => EjecutarAsync(trabajo, perfil, materias, periodo, omitirConsultadas, token));
        }
        return (true, $"Consultando {materias.Count} materias en Banner. Puedes seguir el avance en esta pantalla.");
    }

    /// <summary>Pide detener la consulta: termina la materia que va y no sigue con las demás.</summary>
    public bool Cancelar(PerfilActual? perfil = null)
    {
        lock (_candado)
        {
            if (!_trabajos.TryGetValue(Clave(perfil), out var trabajo) || trabajo.Estado is not { Activa: true }) return false;
            trabajo.Cancelacion?.Cancel();
            trabajo.Estado = trabajo.Estado with { Fase = "Cancelando…" };
            return true;
        }
    }

    /// <summary>
    /// Para borrar un perfil: cancela su consulta en curso, espera (hasta el límite) a que suelte la base de datos y olvida su resumen.
    /// Devuelve false si la consulta no terminó a tiempo (entonces no conviene borrar sus archivos todavía).
    /// </summary>
    public async Task<bool> DetenerYOlvidarAsync(PerfilActual perfil, TimeSpan limite)
    {
        Task tarea;
        lock (_candado)
        {
            if (!_trabajos.TryGetValue(Clave(perfil), out var trabajo)) return true;
            trabajo.Cancelacion?.Cancel();
            tarea = trabajo.Tarea;
        }
        try { await tarea.WaitAsync(limite); }
        catch (TimeoutException) { return false; }
        catch (Exception) { /* una consulta que falló ya soltó todo */ }

        lock (_candado)
        {
            if (_trabajos.Remove(Clave(perfil), out var quitado)) quitado.Cancelacion?.Dispose();
        }
        return true;
    }

    private async Task EjecutarAsync(Trabajo trabajo, PerfilActual? perfil, IReadOnlyList<(string Codigo, string Nombre)> materias, string periodo, bool omitir, CancellationToken ct)
    {
        void Actualizar(Func<EstadoConsultaMasiva, EstadoConsultaMasiva> cambio)
        {
            lock (_candado) if (trabajo.Estado is not null) trabajo.Estado = cambio(trabajo.Estado);
        }

        ResultadoLoteGuardado resultado;
        try
        {
            using var ambito = _ambitos.CreateScope();
            // El ámbito de segundo plano trabaja con la base y la sesión de Banner del mismo perfil que pidió la consulta.
            if (perfil is not null) ambito.ServiceProvider.GetRequiredService<PerfilActual>().CopiarDe(perfil);
            var servicio = ambito.ServiceProvider.GetRequiredService<HorariosService>();
            resultado = await servicio.ConsultarVariasAsync(materias, periodo, omitir, permitirLogin: true,
                fase: texto => Actualizar(e => e with { Fase = texto }),
                alTerminarItem: item => Actualizar(e =>
                {
                    var items = e.Items.Append(item).ToList();
                    var siguiente = materias.FirstOrDefault(m => items.All(i => i.Codigo != m.Codigo));
                    return e with { Items = items, Actual = siguiente.Codigo is null ? null : $"{siguiente.Codigo} · {siguiente.Nombre}" };
                }),
                ct);
        }
        catch (Exception ex)
        {
            resultado = new ResultadoLoteGuardado(false, $"Error inesperado: {ex.GetType().Name}: {ex.Message}");
        }

        Actualizar(e => e with
        {
            Activa = false, Terminada = true, Fin = DateTime.UtcNow, Actual = null,
            Cancelada = resultado.Cancelada, Exito = resultado.Exito, RequiereLogin = resultado.RequiereLogin,
            Fase = resultado.Cancelada ? "Cancelada" : resultado.Exito ? "Terminada" : "Detenida",
            Mensaje = Resumir(e, resultado),
        });
    }

    /// <summary>El resumen de lo que pasó, en lenguaje llano: cuántas materias tienen secciones, cuáles no y cuáles fallaron.</summary>
    public static string Resumir(EstadoConsultaMasiva e, ResultadoLoteGuardado resultado)
    {
        var consultadas = e.ConSecciones + e.SinSecciones + e.ConError;
        var partes = new List<string>();
        if (e.ConSecciones > 0) partes.Add($"{e.ConSecciones} con secciones ({e.TotalSecciones} en total)");
        if (e.SinSecciones > 0) partes.Add($"{e.SinSecciones} sin secciones publicadas por ahora");
        if (e.ConError > 0) partes.Add($"{e.ConError} con error");
        var detalle = partes.Count > 0 ? ": " + string.Join(", ", partes) : "";
        var omitidas = e.Omitidas > 0 ? $" {e.Omitidas} omitidas." : "";

        var cuerpo = consultadas == 0 ? "No se consultó ninguna materia." : $"Consultadas {consultadas} de {e.Total} materias en {e.PeriodoNombre}{detalle}.";
        if (resultado.Cancelada) return $"Consulta cancelada. {cuerpo}{omitidas}";
        if (!resultado.Exito) return $"{resultado.Mensaje} {cuerpo}{omitidas}".Trim();
        return $"{cuerpo}{omitidas}";
    }
}
