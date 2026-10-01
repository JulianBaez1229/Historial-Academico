using System.Text;
using HistorialAcademico.Core.Planificacion;
using HistorialAcademico.Web.Data;
using HistorialAcademico.Web.Models;
using HistorialAcademico.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HistorialAcademico.Web.Controllers;

/// <summary>Modo sin Banner: registrar a mano las materias que tomaste, o importarlas de un CSV con una plantilla descargable.</summary>
public class MateriasManualesController : Controller
{
    /// <summary>Un CSV de miles de materias pesa muy poco; más que esto no es un CSV de materias.</summary>
    public const int MaxBytesCsv = 300 * 1024;

    private readonly MateriasManualesService _manuales;
    private readonly ReglasUniversidadService _reglas;
    private readonly HistorialContext _db;

    public MateriasManualesController(MateriasManualesService manuales, ReglasUniversidadService reglas, HistorialContext db)
    {
        _manuales = manuales;
        _reglas = reglas;
        _db = db;
    }

    private async Task<MateriasManualesViewModel> ArmarAsync(
        string? codigo = null, string? periodo = null, string? calificacion = null, string? error = null, IReadOnlyList<string>? detalles = null, string? mensaje = null,
        CancellationToken ct = default)
    {
        var reglas = _reglas.Activa;
        var pensum = await _db.MateriasPensum.AsNoTracking().OrderBy(m => m.Cuatrimestre).ThenBy(m => m.Codigo).ToListAsync(ct);
        var anio = DateTime.Today.Year;
        var periodos = Enumerable.Range(anio - 6, 8).Reverse()
            .SelectMany(a => Enumerable.Range(0, reglas.Periodos.PorAnio).Reverse().Select(t => new PeriodoAcademico(a, t, reglas.Periodos).Nombre)).ToList();

        return new MateriasManualesViewModel
        {
            Filas = await _manuales.ListarAsync(ct),
            HayPensum = pensum.Count > 0,
            NombreUniversidad = reglas.Nombre,
            Pensum = pensum.Select(m => (m.Codigo, m.Nombre)).ToList(),
            Periodos = periodos,
            EjemploPeriodo = $"{reglas.Periodos.Nombre(0)} {anio}",
            Letras = reglas.Escala.Letras.Select(l => l.Letra).ToList(),
            Codigo = codigo, Periodo = periodo, Calificacion = calificacion,
            Mensaje = mensaje ?? TempData["Mensaje"] as string,
            Error = error ?? TempData["Error"] as string,
            Detalles = detalles ?? Array.Empty<string>(),
        };
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct) => View(await ArmarAsync(ct: ct));

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Agregar(string? codigo, string? periodo, string? calificacion, CancellationToken ct)
    {
        var r = await _manuales.AgregarAsync(codigo, periodo, calificacion, ct);
        if (!r.Ok) return View(nameof(Index), await ArmarAsync(codigo, periodo, calificacion, error: r.Mensaje, ct: ct));

        TempData["Mensaje"] = r.Mensaje;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Eliminar(int id, CancellationToken ct)
    {
        var r = await _manuales.EliminarAsync(id, ct);
        TempData[r.Ok ? "Mensaje" : "Error"] = r.Mensaje;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    [RequestSizeLimit(MaxBytesCsv + 64 * 1024)]   // el archivo más lo que ocupan el resto de los campos del formulario
    public async Task<IActionResult> Importar(IFormFile? archivo, CancellationToken ct)
    {
        if (archivo is null || archivo.Length == 0)
            return View(nameof(Index), await ArmarAsync(error: "Elige un archivo CSV para importar.", ct: ct));
        if (archivo.Length > MaxBytesCsv)
            return View(nameof(Index), await ArmarAsync(error: $"El archivo es demasiado grande ({archivo.Length / 1024} KB); el máximo es {MaxBytesCsv / 1024} KB.", ct: ct));

        string texto;
        using (var lector = new StreamReader(archivo.OpenReadStream(), new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true))
            texto = await lector.ReadToEndAsync(ct);

        var r = await _manuales.ImportarCsvAsync(texto, ct);
        if (!r.Ok) return View(nameof(Index), await ArmarAsync(error: r.Mensaje, detalles: r.Detalles, ct: ct));

        TempData["Mensaje"] = r.Mensaje;
        return RedirectToAction(nameof(Index));
    }

    /// <summary>La plantilla para llenar en Excel o en cualquier editor. Lleva la marca UTF-8 para que Excel respete las tildes.</summary>
    [HttpGet]
    public async Task<IActionResult> Plantilla(CancellationToken ct)
    {
        var contenido = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(await _manuales.PlantillaAsync(ct))).ToArray();
        return File(contenido, "text/csv; charset=utf-8", "plantilla-materias.csv");
    }
}
