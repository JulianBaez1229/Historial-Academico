using HistorialAcademico.Core.Entities;
using HistorialAcademico.Core.Manual;
using HistorialAcademico.Core.Planificacion;
using HistorialAcademico.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace HistorialAcademico.Web.Services;

/// <summary>Una materia registrada a mano, con lo que se sabe de ella para mostrarla.</summary>
public record FilaManualVista(int Id, string Codigo, string Nombre, int Creditos, string Periodo, string Calificacion, EstadoManual Estado)
{
    public bool EnCurso => string.IsNullOrEmpty(Calificacion);
}

public record ResultadoManual(bool Ok, string Mensaje, IReadOnlyList<string>? Detalles = null);

/// <summary>
/// Modo sin Banner: la persona registra a mano las materias que tomó (código del pénsum, período y calificación) o las importa de un CSV.
/// Se guardan aparte y se mezclan con el histórico al leerlo, así que nada de esto toca lo que trae Banner.
/// </summary>
public class MateriasManualesService
{
    private readonly HistorialContext _db;
    private readonly ReglasUniversidadService _reglas;

    public MateriasManualesService(HistorialContext db, ReglasUniversidadService reglas)
    {
        _db = db;
        _reglas = reglas;
    }

    private Task<List<MateriaPensum>> PensumAsync(CancellationToken ct) =>
        _db.MateriasPensum.AsNoTracking().OrderBy(m => m.Cuatrimestre).ThenBy(m => m.Codigo).ToListAsync(ct);

    /// <summary>Las materias registradas, las más recientes primero, y qué pasó con cada una al mezclarlas con el histórico.</summary>
    public async Task<List<FilaManualVista>> ListarAsync(CancellationToken ct = default)
    {
        var pensum = await PensumAsync(ct);
        var manuales = await _db.MateriasManuales.AsNoTracking().ToListAsync(ct);
        var periodosBanner = await _db.Periodos.AsNoTracking().Include(p => p.Materias).ToListAsync(ct);
        var cursosBanner = await _db.CursosEnProgreso.AsNoTracking().ToListAsync(ct);
        var estados = HistoricoManual.Fusionar(periodosBanner, cursosBanner, manuales, pensum, _reglas.Activa).Estados;

        var secuencia = _reglas.Activa.Periodos;
        return manuales
            .Select(m =>
            {
                var materia = pensum.FirstOrDefault(p => string.Equals(p.Codigo, m.Codigo, StringComparison.OrdinalIgnoreCase));
                return new FilaManualVista(m.Id, m.Codigo, materia?.Nombre ?? "", materia?.Creditos ?? 0, m.Periodo, m.Calificacion, estados[m.Id]);
            })
            .OrderByDescending(f => PeriodoAcademico.TryParse(f.Periodo, secuencia, out var p) ? p : default)
            .ThenBy(f => f.Codigo, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Registra una materia; si ya estaba registrada en ese período, le cambia la calificación.</summary>
    public async Task<ResultadoManual> AgregarAsync(string? codigo, string? periodo, string? calificacion, CancellationToken ct = default)
    {
        var (fila, error) = ValidadorManual.Normalizar(codigo, periodo, calificacion, await PensumAsync(ct), _reglas.Activa);
        if (fila is null) return new ResultadoManual(false, error!);

        var existente = await _db.MateriasManuales.FirstOrDefaultAsync(m => m.Codigo == fila.Codigo && m.Periodo == fila.Periodo, ct);
        if (existente is null) _db.MateriasManuales.Add(new MateriaManual { Codigo = fila.Codigo, Periodo = fila.Periodo, Calificacion = fila.Calificacion, Creada = DateTime.UtcNow });
        else existente.Calificacion = fila.Calificacion;
        await _db.SaveChangesAsync(ct);

        var detalle = fila.Calificacion.Length == 0 ? "en curso" : $"con {fila.Calificacion}";
        return new ResultadoManual(true, $"{(existente is null ? "Registrada" : "Actualizada")}: {fila.Codigo}, {fila.Periodo}, {detalle}.");
    }

    public async Task<ResultadoManual> EliminarAsync(int id, CancellationToken ct = default)
    {
        var quitadas = await _db.MateriasManuales.Where(m => m.Id == id).ExecuteDeleteAsync(ct);
        return quitadas > 0 ? new ResultadoManual(true, "Materia quitada.") : new ResultadoManual(false, "Esa materia ya no estaba registrada.");
    }

    /// <summary>
    /// Importa el CSV completo o nada: si alguna fila tiene un problema no se guarda ninguna y se dice cuáles son (con su línea),
    /// para corregir el archivo y volver a importarlo. Una materia que ya estaba en ese período cambia de calificación.
    /// </summary>
    public async Task<ResultadoManual> ImportarCsvAsync(string? texto, CancellationToken ct = default)
    {
        var leido = CsvManual.Leer(texto);
        var problemas = new List<string>(leido.Errores);
        var pensum = await PensumAsync(ct);
        var reglas = _reglas.Activa;

        var validas = new Dictionary<(string, string), FilaManual>();   // la última fila repetida gana
        foreach (var f in leido.Filas)
        {
            var (fila, error) = ValidadorManual.Normalizar(f.Codigo, f.Periodo, f.Calificacion, pensum, reglas);
            if (fila is null) problemas.Add($"Línea {f.Linea}: {error}");
            else validas[(fila.Codigo, fila.Periodo)] = fila;
        }

        if (problemas.Count > 0)
            return new ResultadoManual(false, $"No importé nada: hay {problemas.Count} {(problemas.Count == 1 ? "problema" : "problemas")} en el archivo. Corrígelos y vuelve a importarlo.", problemas.Take(30).ToList());

        var existentes = await _db.MateriasManuales.ToListAsync(ct);
        int nuevas = 0, actualizadas = 0;
        foreach (var fila in validas.Values)
        {
            var actual = existentes.FirstOrDefault(m => m.Codigo == fila.Codigo && m.Periodo == fila.Periodo);
            if (actual is null) { _db.MateriasManuales.Add(new MateriaManual { Codigo = fila.Codigo, Periodo = fila.Periodo, Calificacion = fila.Calificacion, Creada = DateTime.UtcNow }); nuevas++; }
            else if (actual.Calificacion != fila.Calificacion) { actual.Calificacion = fila.Calificacion; actualizadas++; }
        }
        await _db.SaveChangesAsync(ct);

        var iguales = validas.Count - nuevas - actualizadas;
        var partes = new List<string> { $"{nuevas} {(nuevas == 1 ? "materia nueva" : "materias nuevas")}" };
        if (actualizadas > 0) partes.Add($"{actualizadas} con la calificación cambiada");
        if (iguales > 0) partes.Add($"{iguales} que ya estaban igual");
        return new ResultadoManual(true, $"Importé el archivo: {string.Join(", ", partes)}.");
    }

    /// <summary>El contenido de la plantilla, con materias del pénsum de la persona como ejemplo.</summary>
    public async Task<string> PlantillaAsync(CancellationToken ct = default) =>
        CsvManual.Plantilla((await PensumAsync(ct)).Select(m => m.Codigo), _reglas.Activa, DateTime.Today.Year);
}
