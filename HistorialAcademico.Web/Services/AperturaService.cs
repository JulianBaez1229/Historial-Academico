using System.Text;
using HistorialAcademico.Core.Entities;
using HistorialAcademico.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace HistorialAcademico.Web.Services;

/// <summary>Una materia marcada para solicitar su apertura, con su nombre del pénsum.</summary>
public record AperturaItem(string Codigo, string Nombre, decimal Creditos, string Nota, DateTime Marcada);

/// <summary>Las materias sin secciones que necesito que abran, para recordar pedirlo en la escuela (y enviarlo por correo).</summary>
public class AperturaService
{
    public const int MaxNota = 300;

    private readonly HistorialContext _db;

    public AperturaService(HistorialContext db) => _db = db;

    public async Task<List<AperturaItem>> ListarAsync(CancellationToken ct = default)
    {
        var pensum = await _db.MateriasPensum.AsNoTracking().ToDictionaryAsync(m => m.Codigo, StringComparer.OrdinalIgnoreCase, ct);
        var filas = await _db.AperturasSolicitadas.AsNoTracking().OrderBy(a => a.Marcada).ThenBy(a => a.Codigo).ToListAsync(ct);
        return filas.Select(a => pensum.TryGetValue(a.Codigo, out var m)
            ? new AperturaItem(a.Codigo, m.Nombre, m.Creditos, a.Nota, a.Marcada)
            : new AperturaItem(a.Codigo, a.Codigo, 0, a.Nota, a.Marcada)).ToList();
    }

    public async Task<HashSet<string>> MarcadasAsync(CancellationToken ct = default) =>
        (await _db.AperturasSolicitadas.AsNoTracking().Select(a => a.Codigo).ToListAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Marca la materia (o actualiza su nota si ya estaba marcada). Solo materias del pénsum.</summary>
    public async Task<ResultadoOp> MarcarAsync(string? codigo, string? nota, CancellationToken ct = default)
    {
        var c = codigo?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(c) || !await _db.MateriasPensum.AsNoTracking().AnyAsync(m => m.Codigo == c, ct))
            return new(false, "Esa materia no está en tu pénsum.");
        var texto = (nota ?? "").Trim();
        if (texto.Length > MaxNota) return new(false, $"La nota puede tener como máximo {MaxNota} caracteres.");

        var existente = await _db.AperturasSolicitadas.FirstOrDefaultAsync(a => a.Codigo == c, ct);
        if (existente is null) _db.AperturasSolicitadas.Add(new AperturaSolicitada { Codigo = c, Nota = texto, Marcada = DateTime.UtcNow });
        else existente.Nota = texto;
        await _db.SaveChangesAsync(ct);
        return new(true, existente is null ? $"{c} marcada para solicitar su apertura." : $"Nota de {c} actualizada.");
    }

    public async Task<ResultadoOp> QuitarAsync(string? codigo, CancellationToken ct = default)
    {
        var c = codigo?.Trim().ToUpperInvariant();
        var existente = await _db.AperturasSolicitadas.FirstOrDefaultAsync(a => a.Codigo == c, ct);
        if (existente is null) return new(false, "Esa materia no estaba en la lista.");
        _db.AperturasSolicitadas.Remove(existente);
        await _db.SaveChangesAsync(ct);
        return new(true, $"{c} quitada de la lista.");
    }

    /// <summary>
    /// El texto listo para pegar en un correo a la escuela. Solo lleva tu nombre y tu programa (tus propios datos), las
    /// materias con su nombre y créditos y tus notas; nada de matrícula ni de terceros.
    /// </summary>
    public async Task<string> TextoAsync(string periodoNombre, CancellationToken ct = default)
    {
        var items = await ListarAsync(ct);
        var alumno = await _db.DatosAlumno.AsNoTracking().FirstOrDefaultAsync(ct);

        var sb = new StringBuilder();
        sb.AppendLine($"Solicitud de apertura de secciones – {periodoNombre}");
        if (!string.IsNullOrWhiteSpace(alumno?.Nombre)) sb.AppendLine($"Estudiante: {alumno!.Nombre}");
        if (!string.IsNullOrWhiteSpace(alumno?.Programa)) sb.AppendLine($"Programa: {alumno!.Programa}");
        sb.AppendLine();
        if (items.Count == 0)
        {
            sb.AppendLine("(No hay materias marcadas.)");
            return sb.ToString();
        }

        sb.AppendLine(items.Count == 1 ? "Solicito la apertura de sección de la siguiente materia:" : "Solicito la apertura de sección de las siguientes materias:");
        sb.AppendLine();
        var n = 0;
        foreach (var i in items)
        {
            var creditos = i.Creditos > 0 ? $" ({i.Creditos:0.##} {(i.Creditos == 1 ? "crédito" : "créditos")})" : "";
            sb.AppendLine($"{++n}. {i.Codigo} – {i.Nombre}{creditos}");
            if (!string.IsNullOrWhiteSpace(i.Nota)) sb.AppendLine($"   Nota: {i.Nota}");
        }
        sb.AppendLine();
        sb.AppendLine("Gracias.");
        return sb.ToString();
    }
}
