using HistorialAcademico.Core.Pensum;
using HistorialAcademico.Web.Data;
using HistorialAcademico.Web.Models;
using HistorialAcademico.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HistorialAcademico.Web.Controllers;

public class PensumController : Controller
{
    private const long TamanoMaximo = 1024 * 1024;   // 1 MB sobra para un CSV de ~75 líneas

    private readonly HistorialContext _db;
    private readonly AcademicoService _academico;

    public PensumController(HistorialContext db, AcademicoService academico)
    {
        _db = db;
        _academico = academico;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct) => View(new PensumViewModel
    {
        Estado = await _academico.ObtenerAsync(ct),
        Mensaje = TempData["Mensaje"] as string,
        Errores = Lista(TempData["Errores"]),
        Advertencias = Lista(TempData["Advertencias"]),
    });

    /// <summary>Mapa del pénsum: 12 cuatrimestres como columnas y cada materia como tarjeta coloreada por estado.</summary>
    [HttpGet]
    public async Task<IActionResult> Mapa(CancellationToken ct) => View(await _academico.ObtenerAsync(ct));

    /// <summary>Materias Disponibles (todos sus prerrequisitos aprobados), ordenadas por cuatrimestre.</summary>
    [HttpGet]
    public async Task<IActionResult> QueInscribir(CancellationToken ct) => View(await _academico.ObtenerAsync(ct));

    /// <summary>Sube pensum_iso_unapec.csv. Si tiene errores no se guarda nada; se reemplaza todo el pénsum en una transacción.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Subir(IFormFile? archivo, CancellationToken ct)
    {
        if (archivo is null || archivo.Length == 0)
            return Falla("Elige un archivo CSV.");
        if (archivo.Length > TamanoMaximo)
            return Falla("El archivo es demasiado grande (máximo 1 MB).");
        if (!archivo.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            return Falla("El archivo debe ser un .csv.");

        string texto;
        using (var lector = new StreamReader(archivo.OpenReadStream()))
            texto = await lector.ReadToEndAsync(ct);

        var r = PensumCsvParser.Parse(texto);
        if (!r.EsValido)
            return Falla("El CSV tiene errores; no se guardó nada.", r.Errores.Take(20).ToList());

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        await _db.MateriasPensum.ExecuteDeleteAsync(ct);
        await _db.PensumActivo.ExecuteDeleteAsync(ct);   // un CSV subido a mano ya no es el pénsum elegido del catálogo
        _db.MateriasPensum.AddRange(r.Materias);
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        TempData["Mensaje"] = $"Pénsum cargado desde el CSV: {r.Materias.Count} asignaturas, {r.Materias.Sum(m => m.Creditos)} créditos. " +
                              "Para usar uno del catálogo, elígelo en «Carrera y pénsum».";
        if (r.Advertencias.Count > 0) TempData["Advertencias"] = string.Join("\n", r.Advertencias);
        return RedirectToAction(nameof(Index));
    }

    private IActionResult Falla(string mensaje, List<string>? detalle = null)
    {
        TempData["Errores"] = string.Join("\n", new[] { mensaje }.Concat(detalle ?? new()));
        return RedirectToAction(nameof(Index));
    }

    private static List<string> Lista(object? valor) =>
        valor is string s && s.Length > 0 ? s.Split('\n').ToList() : new();
}
