using HistorialAcademico.Core.Entities;
using HistorialAcademico.Core.Pensum;
using HistorialAcademico.Web.Data;
using HistorialAcademico.Web.Models;
using HistorialAcademico.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HistorialAcademico.Web.Controllers;

/// <summary>Equivalencias entre materias de Banner (p. ej. plan anterior) y del pénsum, editables desde la app.</summary>
public class EquivalenciasController : Controller
{
    private readonly HistorialContext _db;
    private readonly ReglasUniversidadService _reglas;

    public EquivalenciasController(HistorialContext db, ReglasUniversidadService reglas)
    {
        _db = db;
        _reglas = reglas;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var lista = await _db.Equivalencias.AsNoTracking().OrderBy(e => e.CodigoBanner).ThenBy(e => e.CodigoPensum).ToListAsync(ct);
        var materias = await _db.MateriasPensum.AsNoTracking().OrderBy(m => m.Codigo).Select(m => new { m.Codigo, m.Nombre }).ToDictionaryAsync(m => m.Codigo, m => m.Nombre, ct);
        var activo = await _db.PensumActivo.AsNoTracking().FirstOrDefaultAsync(ct);
        var declaradas = _reglas.EquivalenciasDeclaradas(activo?.Clave).OrderBy(e => e.CodigoBanner, StringComparer.Ordinal).ThenBy(e => e.CodigoPensum, StringComparer.Ordinal).ToList();

        return View(new EquivalenciasViewModel
        {
            Lista = lista,
            MateriasPensum = materias,
            Declaradas = declaradas,
            Activo = activo,
            // Sin un pénsum cargado no se puede saber si el destino existe: no se marca nada.
            SinEfecto = materias.Count == 0 ? new() : lista.Where(e => e.CodigoPensum is not null && !materias.ContainsKey(e.CodigoPensum)).Select(e => e.Id).ToHashSet(),
            YaDeclaradas = lista.Where(e => EquivalenciasEfectivas.EstaEn(e, declaradas)).Select(e => e.Id).ToHashSet(),
            Mensaje = TempData["Mensaje"] as string,
            Error = TempData["Error"] as string,
        });
    }

    /// <summary>Crea (Id = 0) o edita una equivalencia. CodigoPensum vacío significa "sin equivalente".</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Guardar(int id, string? codigoBanner, string? codigoPensum, string? nota, CancellationToken ct)
    {
        var banner = codigoBanner?.Trim().ToUpperInvariant() ?? "";
        var pensum = string.IsNullOrWhiteSpace(codigoPensum) ? null : codigoPensum.Trim().ToUpperInvariant();

        if (banner.Length == 0) return Falla("El código de Banner es obligatorio.");
        if (pensum is not null && await _db.MateriasPensum.AnyAsync() && !await _db.MateriasPensum.AnyAsync(m => m.Codigo == pensum, ct))
            return Falla($"{pensum} no existe en el pénsum cargado.");
        if (await _db.Equivalencias.AnyAsync(e => e.Id != id && e.CodigoBanner == banner && e.CodigoPensum == pensum, ct))
            return Falla("Esa equivalencia ya existe.");

        Equivalencia e;
        if (id == 0)
        {
            e = new Equivalencia();
            _db.Equivalencias.Add(e);
        }
        else
        {
            e = await _db.Equivalencias.FindAsync(new object[] { id }, ct) ?? throw new InvalidOperationException("Equivalencia no encontrada.");
        }
        e.CodigoBanner = banner;
        e.CodigoPensum = pensum;
        e.Nota = string.IsNullOrWhiteSpace(nota) ? null : nota.Trim();
        await _db.SaveChangesAsync(ct);

        TempData["Mensaje"] = id == 0 ? "Equivalencia agregada." : "Equivalencia actualizada.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Eliminar(int id, CancellationToken ct)
    {
        var borradas = await _db.Equivalencias.Where(e => e.Id == id).ExecuteDeleteAsync(ct);
        TempData["Mensaje"] = borradas > 0 ? "Equivalencia eliminada." : "La equivalencia ya no existía.";
        return RedirectToAction(nameof(Index));
    }

    private IActionResult Falla(string mensaje)
    {
        TempData["Error"] = mensaje;
        return RedirectToAction(nameof(Index));
    }
}
