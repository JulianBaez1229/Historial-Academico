using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HistorialAcademico.Core.Perfiles;

/// <summary>Lo que se guarda en perfiles.json.</summary>
public class DatosAlmacen
{
    public int Version { get; set; } = 1;
    public List<Perfil> Perfiles { get; set; } = new();
    /// <summary>Ya se trajeron a algún perfil los datos de la versión anterior (una sola base junto al proyecto).</summary>
    public bool DatosAnterioresAdoptados { get; set; }
}

/// <summary>
/// Los perfiles de este equipo, guardados en una carpeta del usuario (fuera del repositorio): perfiles.json con la lista y, por cada
/// perfil, perfiles/&lt;id&gt;/ con su base de datos y su sesión de Banner. Nunca se guarda un PIN en texto plano.
/// </summary>
public sealed class AlmacenPerfiles
{
    public const int MaxNombre = 40;
    public const int MaxPerfiles = 12;

    private static readonly JsonSerializerOptions Formato = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    private readonly object _candado = new();
    private readonly Func<DateTime> _ahora;
    private readonly int _iteracionesPin;

    /// <param name="raiz">La carpeta de datos del usuario.</param>
    /// <param name="ahora">El reloj en UTC (las pruebas lo controlan).</param>
    /// <param name="iteracionesPin">Iteraciones del hash del PIN (las pruebas usan pocas para ir rápido).</param>
    public AlmacenPerfiles(string raiz, Func<DateTime>? ahora = null, int iteracionesPin = PinHasher.IteracionesPredeterminadas)
    {
        Raiz = Path.GetFullPath(raiz);
        _ahora = ahora ?? (() => DateTime.UtcNow);
        _iteracionesPin = iteracionesPin;
    }

    public string Raiz { get; }
    public string RutaArchivo => Path.Combine(Raiz, "perfiles.json");

    /// <summary>Si el archivo de perfiles estaba dañado, dónde se guardó la copia (y se empezó de cero). Null si todo estuvo bien.</summary>
    public string? AdvertenciaAlCargar { get; private set; }

    /// <summary>La carpeta de la carpeta de datos del usuario donde vive el perfil.</summary>
    public string CarpetaDe(string id) => Path.Combine(Raiz, "perfiles", id);
    public string RutaBase(string id) => Path.Combine(CarpetaDe(id), "historial.db");

    // ── Lectura y escritura del archivo ───────────────────────────────────────────────────

    private DatosAlmacen Cargar()
    {
        if (!File.Exists(RutaArchivo)) return new DatosAlmacen();
        try
        {
            return JsonSerializer.Deserialize<DatosAlmacen>(File.ReadAllText(RutaArchivo, Encoding.UTF8), Formato) ?? new DatosAlmacen();
        }
        catch (JsonException)
        {
            // No se borra ni se ignora en silencio: se aparta el archivo dañado para que se pueda revisar.
            var copia = $"{RutaArchivo}.danado-{_ahora():yyyyMMddHHmmss}";
            File.Move(RutaArchivo, copia, overwrite: true);
            AdvertenciaAlCargar = $"El archivo de perfiles estaba dañado. Lo guardé como «{Path.GetFileName(copia)}» y empecé sin perfiles; tus carpetas de datos siguen en «{Path.Combine(Raiz, "perfiles")}».";
            return new DatosAlmacen();
        }
    }

    private void Guardar(DatosAlmacen datos)
    {
        Directory.CreateDirectory(Raiz);
        var temporal = RutaArchivo + ".tmp";
        File.WriteAllText(temporal, JsonSerializer.Serialize(datos, Formato), new UTF8Encoding(false));
        File.Move(temporal, RutaArchivo, overwrite: true);   // reemplazo completo: nunca queda a medias
    }

    // ── Consultas ─────────────────────────────────────────────────────────────────────────

    public IReadOnlyList<Perfil> Listar()
    {
        lock (_candado) return Cargar().Perfiles.OrderBy(p => p.Creado).ThenBy(p => p.Nombre, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public Perfil? Obtener(string? id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        lock (_candado) return Cargar().Perfiles.FirstOrDefault(p => p.Id == id);
    }

    public bool DatosAnterioresAdoptados { get { lock (_candado) return Cargar().DatosAnterioresAdoptados; } }

    /// <summary>Por qué un nombre no sirve (null si sirve): entre 1 y 40 caracteres, sin caracteres de control y sin repetir el de otro perfil.</summary>
    public string? ErrorDeNombre(string? nombre, string? idIgnorado = null)
    {
        var n = nombre?.Trim() ?? "";
        if (n.Length == 0) return "Escribe tu nombre.";
        if (n.Length > MaxNombre) return $"El nombre puede tener como máximo {MaxNombre} caracteres.";
        if (n.Any(char.IsControl)) return "El nombre tiene caracteres que no se pueden usar.";
        lock (_candado)
            return Cargar().Perfiles.Any(p => p.Id != idIgnorado && string.Equals(p.Nombre, n, StringComparison.CurrentCultureIgnoreCase))
                ? $"Ya hay un perfil llamado «{n}» en este equipo." : null;
    }

    // ── Cambios ───────────────────────────────────────────────────────────────────────────

    /// <summary>Crea el perfil y su carpeta. Sin PIN (null) el perfil se abre sin pedir nada.</summary>
    public (Perfil? Perfil, string? Error) Crear(string? nombre, string? pin, int pasoAsistente = PasosAsistente.Terminado)
    {
        if (pin is not null && PinHasher.ErrorDePin(pin) is { } errorPin) return (null, errorPin);
        lock (_candado)
        {
            var datos = Cargar();
            if (datos.Perfiles.Count >= MaxPerfiles) return (null, $"Este equipo ya tiene {MaxPerfiles} perfiles, que es el máximo.");
            var error = ErrorDeNombre(nombre);
            if (error is not null) return (null, error);

            string id;
            do id = Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();
            while (datos.Perfiles.Any(p => p.Id == id));

            var perfil = new Perfil { Id = id, Nombre = nombre!.Trim(), Creado = _ahora(), PinHash = pin is null ? null : PinHasher.Hash(pin, _iteracionesPin),
                PasoAsistente = PasosAsistente.EsPasoValido(pasoAsistente) ? pasoAsistente : PasosAsistente.Terminado };
            Directory.CreateDirectory(CarpetaDe(id));
            datos.Perfiles.Add(perfil);
            Guardar(datos);
            return (perfil, null);
        }
    }

    /// <summary>
    /// Comprueba el PIN y lleva la cuenta de los fallos: tras varios seguidos hay que esperar (cada vez más), y la espera se guarda,
    /// así que cerrar y volver a abrir la aplicación no la salta. Un perfil sin PIN entra siempre.
    /// </summary>
    public IntentoDeEntrada Intentar(string? id, string? pin)
    {
        lock (_candado)
        {
            var datos = Cargar();
            var perfil = datos.Perfiles.FirstOrDefault(p => p.Id == id);
            if (perfil is null) return new(ResultadoEntrada.NoExiste);

            var ahora = _ahora();
            if (perfil.BloqueadoHasta is { } hasta && hasta > ahora) return new(ResultadoEntrada.Bloqueado, hasta - ahora, perfil.Fallos);
            if (!perfil.TienePin) return new(ResultadoEntrada.Correcto);

            if (PinHasher.Verificar(pin, perfil.PinHash))
            {
                if (perfil.Fallos != 0 || perfil.BloqueadoHasta is not null) { perfil.Fallos = 0; perfil.BloqueadoHasta = null; Guardar(datos); }
                return new(ResultadoEntrada.Correcto);
            }

            perfil.Fallos++;
            var espera = PoliticaBloqueo.Espera(perfil.Fallos);
            perfil.BloqueadoHasta = espera > TimeSpan.Zero ? ahora + espera : null;
            Guardar(datos);
            return new(ResultadoEntrada.PinIncorrecto, espera, perfil.Fallos);
        }
    }

    /// <summary>Guarda la universidad del pénsum elegido (sus reglas se usarán al abrir el perfil).</summary>
    public void GuardarUniversidad(string id, string? universidad)
    {
        lock (_candado)
        {
            var datos = Cargar();
            var perfil = datos.Perfiles.FirstOrDefault(p => p.Id == id);
            if (perfil is null || perfil.UniversidadId == universidad) return;
            perfil.UniversidadId = universidad;
            Guardar(datos);
        }
    }

    /// <summary>Guarda en qué paso del asistente va el perfil (0 = terminado), para seguir donde quedó si se cierra la aplicación.</summary>
    public void GuardarPaso(string id, int paso)
    {
        if (paso != PasosAsistente.Terminado && !PasosAsistente.EsPasoValido(paso)) throw new ArgumentOutOfRangeException(nameof(paso));
        lock (_candado)
        {
            var datos = Cargar();
            var perfil = datos.Perfiles.FirstOrDefault(p => p.Id == id);
            if (perfil is null || perfil.PasoAsistente == paso) return;
            perfil.PasoAsistente = paso;
            Guardar(datos);
        }
    }

    /// <summary>Cambia el nombre del perfil. Devuelve por qué no se pudo (null si se pudo).</summary>
    public string? Renombrar(string id, string? nombre)
    {
        lock (_candado)
        {
            var datos = Cargar();
            var perfil = datos.Perfiles.FirstOrDefault(p => p.Id == id);
            if (perfil is null) return "Ese perfil ya no existe.";
            var error = ErrorDeNombre(nombre, id);
            if (error is not null) return error;
            perfil.Nombre = nombre!.Trim();
            Guardar(datos);
            return null;
        }
    }

    public void MarcarDatosAnterioresAdoptados()
    {
        lock (_candado)
        {
            var datos = Cargar();
            if (datos.DatosAnterioresAdoptados) return;
            datos.DatosAnterioresAdoptados = true;
            Guardar(datos);
        }
    }

    /// <summary>
    /// Borra la carpeta del perfil (base de datos y sesión de Banner) y lo quita de la lista. Devuelve false si no existía.
    /// La carpeta va primero: si no se puede borrar (un archivo abierto) se lanza la excepción y el perfil sigue en la lista, para poder
    /// reintentar, en vez de quedar datos sueltos que ya nadie ve desde la aplicación.
    /// </summary>
    public bool Borrar(string id)
    {
        lock (_candado)
        {
            var datos = Cargar();
            if (datos.Perfiles.All(p => p.Id != id)) return false;
            var carpeta = CarpetaDe(id);
            if (Directory.Exists(carpeta)) Directory.Delete(carpeta, recursive: true);
            datos.Perfiles.RemoveAll(p => p.Id == id);
            Guardar(datos);
            return true;
        }
    }
}
