using System.Globalization;
using System.Text;
using HistorialAcademico.Core.Entities;
using HistorialAcademico.Core.Manual;
using HistorialAcademico.Core.Pensum;
using HistorialAcademico.Core.Universidad;
using HistorialAcademico.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace HistorialAcademico.Web.Services;

/// <summary>Un pénsum del catálogo tal como se muestra en la lista.</summary>
public record PensumEnCatalogo(PensumDefinicion Definicion, ReglasUniversidad Universidad, bool EsActivo, bool CoincideConCargado)
{
    public bool EsPersonal => Definicion.EsPersonal;
}

/// <summary>Una materia del pénsum que tu histórico ya cubre: aprobada directamente o por equivalencia con otra de Banner.</summary>
public record MateriaConvalidada(string Codigo, string Nombre, int Creditos, string? Calificacion, string? PorEquivalencia, bool Exenta);

/// <summary>Qué pasaría con tu histórico si usaras un pénsum: cuánto cubre y qué materias se convalidan.</summary>
public record ResultadoConvalidacion(
    int CreditosAprobados, int CreditosTotales, decimal Porcentaje, int Disponibles, List<MateriaConvalidada> Materias)
{
    public int PorEquivalencia => Materias.Count(m => m.PorEquivalencia is not null);
    public int Directas => Materias.Count - PorEquivalencia;
}

/// <summary>Todo lo que la pantalla «Carrera y pénsum» necesita.</summary>
public class VistaCarrera
{
    /// <summary>Qué cubre tu histórico en cada pénsum de la lista (por clave). Vacío si todavía no hay histórico.</summary>
    public Dictionary<string, ResultadoConvalidacion> Convalidaciones { get; init; } = new();
    public bool HayHistorico { get; init; }
    /// <summary>El pénsum elegido del catálogo (null si se cargó desde un CSV o todavía no hay ninguno).</summary>
    public PensumActivo? Activo { get; init; }
    /// <summary>Cuántas materias tiene el pénsum cargado en la base (de cualquier origen).</summary>
    public int MateriasCargadas { get; init; }
    public int CreditosCargados { get; init; }
    public List<PensumEnCatalogo> Pensums { get; init; } = new();
    /// <summary>Todos los pénsums válidos (sin el filtro), para saber cuántos hay.</summary>
    public int TotalEnCatalogo { get; init; }
    public List<ReglasUniversidad> Universidades { get; init; } = new();
    /// <summary>Archivos del catálogo con errores, con su motivo.</summary>
    public List<string> Problemas { get; init; } = new();
    public bool CarpetaEncontrada { get; init; }

    public bool HayPensumCargado => MateriasCargadas > 0;
}

/// <summary>Elegir universidad, carrera y versión del plan: aplica el pénsum del catálogo y las reglas de su universidad.</summary>
public class CarreraService
{
    private readonly HistorialContext _db;
    private readonly ReglasUniversidadService _reglas;
    private readonly AcademicoService _academico;

    public CarreraService(HistorialContext db, ReglasUniversidadService reglas, AcademicoService academico)
    {
        _db = db;
        _reglas = reglas;
        _academico = academico;
    }

    public Task<PensumActivo?> ActivoAsync(CancellationToken ct = default) => _db.PensumActivo.AsNoTracking().FirstOrDefaultAsync(ct);

    /// <summary>El catálogo con el filtro aplicado: la universidad exacta y un texto que se busca (sin importar mayúsculas ni acentos) en la carrera y la universidad.</summary>
    public async Task<VistaCarrera> ObtenerAsync(string? universidad, string? busqueda, CancellationToken ct = default)
    {
        var catalogo = _reglas.LeerCatalogo();
        var activo = await ActivoAsync(ct);
        var cargadas = await _db.MateriasPensum.AsNoTracking().ToListAsync(ct);
        var clave = activo?.Clave;
        var texto = Normalizar(busqueda);

        var todos = catalogo.Pensums.Where(p => p.EsValida)
            .Select(p => (Definicion: p.Resultado.Definicion!, Universidad: catalogo.ReglasDe(p.Resultado.Definicion!.Universidad)!))
            .OrderBy(x => x.Universidad.Nombre, StringComparer.CurrentCultureIgnoreCase).ThenBy(x => x.Definicion.NombreCarrera, StringComparer.CurrentCultureIgnoreCase)
            .ThenByDescending(x => x.Definicion.Version, StringComparer.Ordinal).ToList();

        var filtrados = todos.Where(x =>
            (string.IsNullOrWhiteSpace(universidad) || x.Definicion.Universidad == universidad) &&
            (texto.Length == 0 || Normalizar($"{x.Definicion.NombreCarrera} {x.Definicion.Carrera} {x.Universidad.Nombre} {x.Definicion.Version}").Contains(texto))).ToList();

        // Qué cubriría tu histórico en cada uno (para decidir antes de cambiar).
        var periodosBanner = await _db.Periodos.AsNoTracking().Include(p => p.Materias).ToListAsync(ct);
        var cursosBanner = await _db.CursosEnProgreso.AsNoTracking().ToListAsync(ct);
        var manuales = await _db.MateriasManuales.AsNoTracking().ToListAsync(ct);
        // El histórico es el de Banner más lo que registraste a mano en los períodos que Banner no trae.
        var fusion = HistoricoManual.Fusionar(periodosBanner, cursosBanner, manuales, cargadas, _academico.Reglas);
        var periodos = fusion.Periodos;
        var convalidaciones = new Dictionary<string, ResultadoConvalidacion>();
        if (periodos.Count > 0)
        {
            var cursadas = periodos.SelectMany(p => p.Materias).ToList();
            var cursos = fusion.Cursos;
            var personales = await _db.Equivalencias.AsNoTracking().ToListAsync(ct);
            foreach (var x in filtrados) convalidaciones[x.Definicion.Clave] = Convalidar(x.Definicion, x.Universidad, cursadas, cursos, personales);
        }

        return new VistaCarrera
        {
            Convalidaciones = convalidaciones,
            HayHistorico = periodos.Count > 0,
            Activo = activo,
            MateriasCargadas = cargadas.Count,
            CreditosCargados = cargadas.Sum(m => m.Creditos),
            Pensums = filtrados.Select(x => new PensumEnCatalogo(x.Definicion, x.Universidad, x.Definicion.Clave == clave, Coincide(x.Definicion, cargadas))).ToList(),
            TotalEnCatalogo = todos.Count,
            Universidades = catalogo.UniversidadesValidas.OrderBy(u => u.Nombre, StringComparer.CurrentCultureIgnoreCase).ToList(),
            Problemas = catalogo.Problemas.ToList(),
            CarpetaEncontrada = _reglas.Carpeta is not null,
        };
    }

    /// <summary>
    /// Qué materias de este pénsum cubre el histórico dado, con las equivalencias que declara el propio pénsum y las personales,
    /// y con la escala de calificaciones de su universidad. No toca la base de datos.
    /// </summary>
    public static ResultadoConvalidacion Convalidar(
        PensumDefinicion definicion, ReglasUniversidad reglas, IReadOnlyList<MateriaCursada> cursadas,
        IReadOnlyList<CursoEnProgreso> cursos, IEnumerable<Equivalencia> personales)
    {
        var equivalencias = EquivalenciasEfectivas.Unir(definicion.AEquivalencias(), personales);
        var r = MotorEstadoPensum.Calcular(definicion.AMateriasPensum(), cursadas, cursos, equivalencias, reglas.Escala);
        var materias = r.Materias.Where(m => m.CuentaComoAprobada)
            .Select(m => new MateriaConvalidada(m.Materia.Codigo, m.Materia.Nombre, m.Materia.Creditos, m.Calificacion, m.PorEquivalencia, m.Estado == EstadoMateria.Exenta))
            .OrderBy(m => definicion.Materias.First(d => d.Codigo == m.Codigo).Cuatrimestre).ThenBy(m => m.Codigo, StringComparer.Ordinal).ToList();
        return new ResultadoConvalidacion(r.CreditosAprobados, r.CreditosTotales, r.PorcentajeAprobado,
            r.Materias.Count(m => m.Estado == EstadoMateria.Disponible), materias);
    }

    /// <summary>¿Las materias cargadas son exactamente las de este pénsum (mismos códigos, créditos, cuatrimestres y prerrequisitos)?</summary>
    public static bool Coincide(PensumDefinicion definicion, IReadOnlyCollection<MateriaPensum> cargadas)
    {
        if (cargadas.Count != definicion.Materias.Count) return false;
        var por = cargadas.ToDictionary(m => m.Codigo, StringComparer.OrdinalIgnoreCase);
        static string Prer(string? p) => string.Join(";", (p ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return definicion.Materias.All(d => por.TryGetValue(d.Codigo, out var m) &&
            m.Creditos == d.Creditos && m.Cuatrimestre == d.Cuatrimestre && m.EsElectiva == d.Electiva &&
            Prer(m.Prerrequisitos) == string.Join(";", d.Prerrequisitos));
    }

    /// <summary>
    /// Aplica el pénsum del catálogo en una transacción: reemplaza las materias del pénsum y guarda cuál es el activo. El histórico
    /// de Banner y las equivalencias no se tocan. Lo que quedó apuntando a materias que ya no existen (materias de los escenarios
    /// del planificador y materias marcadas para solicitar apertura) se quita, y se dice cuántas fueron. Las reglas de la
    /// universidad del pénsum pasan a ser las activas.
    /// </summary>
    public async Task<ResultadoOp> ActivarAsync(string? clave, CancellationToken ct = default)
    {
        var catalogo = _reglas.LeerCatalogo();
        var definicion = catalogo.PensumsValidos.FirstOrDefault(p => p.Clave == clave);
        if (definicion is null) return new(false, "Ese pénsum no está en el catálogo (o su archivo tiene errores).");
        if (catalogo.ReglasDe(definicion.Universidad) is null) return new(false, $"La universidad «{definicion.Universidad}» no tiene reglas válidas.");

        var codigos = definicion.Materias.Select(m => m.Codigo).ToList();
        int planificadas, aperturas;
        await using (var tx = await _db.Database.BeginTransactionAsync(ct))
        {
            _db.ChangeTracker.Clear();
            await _db.MateriasPensum.ExecuteDeleteAsync(ct);
            _db.MateriasPensum.AddRange(definicion.AMateriasPensum());

            var activo = await _db.PensumActivo.FirstOrDefaultAsync(ct);
            if (activo is null) _db.PensumActivo.Add(activo = new PensumActivo());
            activo.Universidad = definicion.Universidad;
            activo.Carrera = definicion.Carrera;
            activo.Version = definicion.Version;
            activo.NombreCarrera = definicion.NombreCarrera;
            activo.Aplicado = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            planificadas = await _db.MateriasPlanificadas.Where(m => !codigos.Contains(m.Codigo)).ExecuteDeleteAsync(ct);
            await _db.PeriodosPlanificados.Where(p => !p.Materias.Any()).ExecuteDeleteAsync(ct);   // no se guardan períodos vacíos
            aperturas = await _db.AperturasSolicitadas.Where(a => !codigos.Contains(a.Codigo)).ExecuteDeleteAsync(ct);
            await tx.CommitAsync(ct);
        }
        _reglas.Establecer(definicion.Universidad);

        var estado = await _academico.ObtenerAsync(ct);
        var partes = new List<string>
        {
            $"Pénsum activo: {definicion.NombreCarrera} (plan {definicion.Version}): {definicion.Materias.Count} materias, {definicion.TotalCreditos} créditos, {definicion.Cuatrimestres} cuatrimestres.",
        };
        if (estado.HayHistorico)
        {
            var disponibles = estado.Faltantes.Count(m => m.Estado == EstadoMateria.Disponible);
            partes.Add($"Con tu histórico llevas {estado.Pensum.CreditosAprobados} de {estado.Pensum.CreditosTotales} créditos aprobados " +
                       $"({estado.Pensum.PorcentajeAprobado.ToString("0.0", CultureInfo.GetCultureInfo("es-DO"))} %) y tienes " +
                       (disponibles == 1 ? "1 materia disponible." : $"{disponibles} materias disponibles."));

            var porEquivalencia = estado.Pensum.Materias.Where(m => m.PorEquivalencia is not null && m.CuentaComoAprobada).ToList();
            if (porEquivalencia.Count > 0)
                partes.Add((porEquivalencia.Count == 1 ? "Se convalida 1 materia por equivalencia: " : $"Se convalidan {porEquivalencia.Count} materias por equivalencia: ") +
                           string.Join(", ", porEquivalencia.Take(6).Select(m => $"{m.Materia.Codigo} (por {m.PorEquivalencia})")) +
                           (porEquivalencia.Count > 6 ? $" y {porEquivalencia.Count - 6} más." : "."));
        }
        if (planificadas > 0)
            partes.Add(planificadas == 1
                ? "Se quitó 1 materia de tus escenarios del planificador que no existe en este pénsum."
                : $"Se quitaron {planificadas} materias de tus escenarios del planificador que no existen en este pénsum.");
        if (aperturas > 0)
            partes.Add(aperturas == 1
                ? "Se quitó 1 materia de la lista de solicitar apertura que no existe en este pénsum."
                : $"Se quitaron {aperturas} materias de la lista de solicitar apertura que no existen en este pénsum.");
        return new(true, string.Join(" ", partes));
    }

    // ── Pénsum personal (pegado como texto) ───────────────────────────────────────────────

    public const int MaxNombreCarrera = 100;
    private static readonly System.Text.RegularExpressions.Regex VersionRx = new("^[A-Za-z0-9]+([._-][A-Za-z0-9]+)*$");

    /// <summary>Los metadatos del pénsum personal a partir de lo que escribió la persona; null y un motivo si algo no sirve.</summary>
    public (MetadatosPensum? Meta, string? Error) MetadatosPersonales(string? universidad, string? nombreCarrera, string? version)
    {
        var nombre = nombreCarrera?.Trim() ?? "";
        if (nombre.Length == 0) return (null, "Escribe el nombre de la carrera.");
        if (nombre.Length > MaxNombreCarrera) return (null, $"El nombre de la carrera puede tener como máximo {MaxNombreCarrera} caracteres.");
        var slug = ImportadorPensumTexto.Slug(nombre);
        if (slug.Length == 0) return (null, "El nombre de la carrera debe tener letras o números.");
        if (slug.Length > 40) slug = slug[..40].Trim('-');
        var v = version?.Trim() ?? "";
        if (!VersionRx.IsMatch(v) || v.Length > 20) return (null, "Escribe la versión del plan con letras, números y . _ - (por ejemplo 2022 o 2022-b).");
        var catalogo = _reglas.LeerCatalogo();
        if (catalogo.ReglasDe(universidad ?? "") is null) return (null, "Elige la universidad de cuyas reglas (escala de notas, períodos y límites) quieres usar.");
        return (new MetadatosPensum(universidad!, PensumDefinicion.PrefijoPersonal + slug, nombre, v), null);
    }

    /// <summary>Dónde se guardaría (o está) un pénsum personal; null si no se encontró la carpeta pensums/.</summary>
    private string? RutaPersonal(MetadatosPensum meta) =>
        _reglas.Carpeta is null ? null : Path.Combine(_reglas.Carpeta, meta.Universidad, $"{meta.Carrera}-{meta.Version}.json");

    public bool YaExistePersonal(MetadatosPensum meta) => RutaPersonal(meta) is { } r && File.Exists(r);

    /// <summary>
    /// Guarda el pénsum armado con la vista previa como pénsum personal: un archivo <c>pensums/&lt;universidad&gt;/personal-&lt;carrera&gt;-&lt;versión&gt;.json</c>
    /// (queda fuera de Git) que aparece en la lista de «Carrera y pénsum» como cualquier otro, y opcionalmente lo pone en uso.
    /// Solo se guarda si es válido; no pisa uno que ya existe salvo que se pida.
    /// </summary>
    public async Task<ResultadoOp> GuardarPersonalAsync(
        IReadOnlyList<FilaEditable> filas, string? universidad, string? nombreCarrera, string? version, bool reemplazar, bool usar, CancellationToken ct = default)
    {
        var (meta, error) = MetadatosPersonales(universidad, nombreCarrera, version);
        if (meta is null) return new(false, error!);

        var construccion = ImportadorPensumTexto.Construir(filas, meta);
        if (!construccion.EsValido) return new(false, "El pénsum tiene problemas; corrígelos en la vista previa antes de guardarlo.");

        var ruta = RutaPersonal(meta);
        if (ruta is null) return new(false, "No encontré la carpeta pensums/ de la aplicación, así que no puedo guardar el pénsum.");
        if (File.Exists(ruta) && !reemplazar)
            return new(false, $"Ya tienes un pénsum personal «{meta.NombreCarrera}» con la versión {meta.Version}. Marca «Reemplazarlo» para sustituirlo, o cambia la versión.");

        Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);
        await File.WriteAllTextAsync(ruta, PensumJson.Escribir(construccion.Resultado.Definicion!), new System.Text.UTF8Encoding(false), ct);

        var definicion = construccion.Resultado.Definicion!;
        var guardado = $"Pénsum personal guardado: {definicion.NombreCarrera} (plan {definicion.Version}), {definicion.Materias.Count} materias, {definicion.TotalCreditos} créditos.";
        if (!usar) return new(true, guardado + " Ya está en la lista de «Carrera y pénsum».");

        var activado = await ActivarAsync(definicion.Clave, ct);
        return new(activado.Ok, activado.Ok ? guardado + " " + activado.Mensaje : guardado + " No pude ponerlo en uso: " + activado.Mensaje);
    }

    /// <summary>Borra un pénsum personal (solo los personales, y no el que está en uso).</summary>
    public async Task<ResultadoOp> EliminarPersonalAsync(string? clave, CancellationToken ct = default)
    {
        var entrada = _reglas.LeerCatalogo().Pensums.FirstOrDefault(p => p.Resultado.Definicion?.Clave == clave);
        var definicion = entrada?.Resultado.Definicion;
        if (definicion is null) return new(false, "Ese pénsum no está en el catálogo.");
        if (!definicion.EsPersonal) return new(false, "Solo se pueden borrar los pénsums personales; los del catálogo compartido son parte del proyecto.");
        if ((await ActivoAsync(ct))?.Clave == clave) return new(false, "Ese pénsum está en uso. Elige otro pénsum antes de borrarlo.");

        File.Delete(entrada!.Ruta);
        return new(true, $"Pénsum personal «{definicion.NombreCarrera}» (plan {definicion.Version}) borrado.");
    }

    /// <summary>Minúsculas y sin acentos, para que «ingenieria» encuentre «Ingeniería».</summary>
    public static string Normalizar(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return "";
        var sb = new StringBuilder();
        foreach (var c in texto.Trim().Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }
}
