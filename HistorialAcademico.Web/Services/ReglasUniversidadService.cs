using HistorialAcademico.Core.Entities;
using HistorialAcademico.Core.Universidad;
using HistorialAcademico.Web.Perfiles;

namespace HistorialAcademico.Web.Services;

/// <summary>
/// Las reglas de la universidad activa (escala de calificaciones, períodos, límites de créditos), leídas de
/// pensums/&lt;universidad&gt;/universidad.json. Se usa la universidad del perfil en uso; si aún no eligió carrera, la de «Pensums:Universidad»
/// (por defecto unapec) hasta que <see cref="Establecer"/> la cambie a la del pénsum que eligió la persona. La carpeta se busca desde la aplicación
/// hacia arriba, o se indica con «Pensums:Carpeta».
/// Si el archivo falta o tiene errores, la aplicación no se cae: usa los valores de UNAPEC y guarda el motivo en
/// <see cref="Problemas"/> para poder mostrarlo.
/// </summary>
public class ReglasUniversidadService
{
    private const string Incorporados = "valores incorporados de UNAPEC";

    private sealed record Cargado(ReglasUniversidad Reglas, string Origen, IReadOnlyList<string> Problemas);

    private volatile Cargado _actual;

    /// <summary>La carpeta pensums/ (null si no se encontró).</summary>
    public string? Carpeta { get; }

    public ReglasUniversidad Activa => _actual.Reglas;

    /// <summary>De dónde salieron las reglas: la ruta del archivo o «valores incorporados de UNAPEC».</summary>
    public string Origen => _actual.Origen;

    /// <summary>
    /// El nombre de la universidad que la persona eligió, para mostrárselo. Null si todavía no eligió ninguna o si su archivo no se pudo leer
    /// (en ese caso Activa son las reglas incorporadas de UNAPEC, y decir que es su universidad sería falso).
    /// </summary>
    public string? NombreDeLaUniversidadElegida =>
        _perfil?.UniversidadId is { Length: > 0 } && _actual.Origen != Incorporados ? _actual.Reglas.Nombre : null;

    /// <summary>Por qué no se pudieron leer las reglas del archivo (vacío si todo salió bien).</summary>
    public IReadOnlyList<string> Problemas => _actual.Problemas;

    private readonly PerfilActual? _perfil;

    /// <param name="perfil">El perfil en uso: si ya eligió una carrera, arranca con las reglas de su universidad.</param>
    public ReglasUniversidadService(IWebHostEnvironment entorno, IConfiguration configuracion, ILogger<ReglasUniversidadService> registro, PerfilActual? perfil = null)
    {
        _perfil = perfil;
        var id = perfil?.UniversidadId is { Length: > 0 } delPerfil ? delPerfil
            : configuracion["Pensums:Universidad"] is { Length: > 0 } configurada ? configurada : "unapec";
        Carpeta = configuracion["Pensums:Carpeta"] is { Length: > 0 } c ? c : BuscarCarpeta(entorno.ContentRootPath) ?? BuscarCarpeta(AppContext.BaseDirectory);

        _actual = Cargar(id, Carpeta);
        foreach (var p in Problemas) registro.LogWarning("Reglas de la universidad «{Id}»: {Problema}", id, p);
    }

    private ReglasUniversidadService(ReglasUniversidad reglas, string origen)
    {
        _actual = new Cargado(reglas, origen, Array.Empty<string>());
    }

    /// <summary>Un servicio con reglas fijas (para pruebas o para quien no tiene la carpeta de pénsums).</summary>
    public static ReglasUniversidadService Fijas(ReglasUniversidad reglas) => new(reglas, "reglas indicadas directamente");

    // El servicio vive una petición (las reglas dependen del perfil), pero el catálogo se comparte: se lee del disco solo si cambió.
    private static readonly object _candadoCatalogo = new();
    private static readonly Dictionary<string, (string Firma, CatalogoCompleto Catalogo)> _catalogos = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Los pénsums y universidades que hay ahora en la carpeta. Un archivo nuevo o modificado aparece sin reiniciar: se vuelve a
    /// leer cuando cambia la lista de archivos, su fecha o su tamaño (si no, se reutiliza lo ya leído, porque varias pantallas lo piden a la vez).
    /// </summary>
    public CatalogoCompleto LeerCatalogo()
    {
        if (Carpeta is null || !Directory.Exists(Carpeta)) return new CatalogoCompleto(new(), new());
        var firma = string.Join("|", new DirectoryInfo(Carpeta).EnumerateFiles("*.json", SearchOption.AllDirectories)
            .OrderBy(f => f.FullName, StringComparer.Ordinal).Select(f => $"{f.FullName}:{f.LastWriteTimeUtc.Ticks}:{f.Length}"));
        lock (_candadoCatalogo)
        {
            if (!_catalogos.TryGetValue(Carpeta, out var guardado) || guardado.Firma != firma)
                _catalogos[Carpeta] = guardado = (firma, CatalogoUniversidades.Leer(Carpeta));
            return guardado.Catalogo;
        }
    }

    /// <summary>Las equivalencias que declara el pénsum con esa clave (unapec/ingenieria-software-11.json); vacío si no está en el catálogo.</summary>
    public List<Equivalencia> EquivalenciasDeclaradas(string? clave) =>
        clave is null ? new() : LeerCatalogo().PensumsValidos.FirstOrDefault(p => p.Clave == clave)?.AEquivalencias() ?? new();

    /// <summary>
    /// Pasa a usar las reglas de otra universidad (la del pénsum elegido). Devuelve false, y no cambia nada, si esa universidad
    /// no tiene un universidad.json válido.
    /// </summary>
    public bool Establecer(string universidad)
    {
        var nuevo = Cargar(universidad, Carpeta);
        if (nuevo.Problemas.Count > 0) return false;
        _actual = nuevo;
        _perfil?.GuardarUniversidad(universidad);
        return true;
    }

    private Cargado Cargar(string id, string? carpeta)
    {
        if (carpeta is null)
            return new(ReglasUniversidad.Unapec, Incorporados, new[] { "No encontré la carpeta pensums/; uso las reglas incorporadas de UNAPEC." });

        var catalogo = LeerCatalogo();
        var entrada = catalogo.Universidades.FirstOrDefault(u => u.Resultado.Reglas?.Id == id || Path.GetFileName(Path.GetDirectoryName(u.Ruta)) == id);
        if (entrada is null)
            return new(ReglasUniversidad.Unapec, Incorporados, new[] { $"No encontré pensums/{id}/universidad.json; uso las reglas incorporadas de UNAPEC." });
        if (!entrada.EsValida)
            return new(ReglasUniversidad.Unapec, Incorporados, entrada.Resultado.Errores.Select(e => $"pensums/{id}/universidad.json: {e}").Append("Uso las reglas incorporadas de UNAPEC.").ToList());
        return new(entrada.Resultado.Reglas!, entrada.Ruta, Array.Empty<string>());
    }

    /// <summary>Sube desde la carpeta dada hasta encontrar una subcarpeta «pensums» (la de la solución cuando se corre desde el repositorio).</summary>
    private static string? BuscarCarpeta(string inicio)
    {
        var dir = new DirectoryInfo(inicio);
        while (dir is not null)
        {
            var candidata = Path.Combine(dir.FullName, "pensums");
            if (Directory.Exists(candidata)) return candidata;
            dir = dir.Parent;
        }
        return null;
    }
}
