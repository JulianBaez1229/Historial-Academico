using HistorialAcademico.Core.Pensum;
using HistorialAcademico.Web.Helpers;
using HistorialAcademico.Web.Models;
using HistorialAcademico.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace HistorialAcademico.Web.Controllers;

/// <summary>Elegir universidad, carrera y versión del plan de estudios: todo el pénsum, el índice y el planificador se personalizan según ese pénsum.</summary>
public class CarreraController : Controller
{
    private readonly CarreraService _carrera;
    private readonly ReglasUniversidadService _reglas;

    public CarreraController(CarreraService carrera, ReglasUniversidadService reglas)
    {
        _carrera = carrera;
        _reglas = reglas;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? universidad, string? q, CancellationToken ct) => View(new CarreraViewModel
    {
        Vista = await _carrera.ObtenerAsync(universidad, q, ct),
        Universidad = universidad,
        Busqueda = q,
        Reglas = _reglas.Activa,
        Mensaje = TempData["Mensaje"] as string,
        Error = TempData["Error"] as string,
    });

    /// <summary>Usa el pénsum elegido. Reemplaza las materias del pénsum; el histórico de Banner no se toca.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Activar(string? clave, CancellationToken ct)
    {
        var r = await _carrera.ActivarAsync(clave, ct);
        TempData[r.Ok ? "Mensaje" : "Error"] = r.Mensaje;
        return RedirectToAction(nameof(Index));
    }

    // ── Importar un pénsum pegando el texto ───────────────────────────────────────────────

    [HttpGet]
    public IActionResult Importar() => View(new ImportarViewModel
    {
        Universidades = _reglas.LeerCatalogo().UniversidadesValidas.OrderBy(u => u.Nombre, StringComparer.CurrentCultureIgnoreCase).ToList(),
        Universidad = _reglas.Activa.Id,
        Version = DateTime.Today.Year.ToString(),
    });

    /// <summary>Lee el texto pegado y muestra la vista previa editable.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult Convertir(string? texto, string? universidad, string? nombreCarrera, string? version)
    {
        var analisis = ImportadorPensumTexto.Analizar(texto);
        if (analisis.Filas.Count == 0)
            return View(nameof(Importar), new ImportarViewModel
            {
                Universidades = _reglas.LeerCatalogo().UniversidadesValidas.OrderBy(u => u.Nombre, StringComparer.CurrentCultureIgnoreCase).ToList(),
                Texto = texto, Universidad = universidad, NombreCarrera = nombreCarrera, Version = version ?? "",
                Error = string.Join(" ", analisis.Avisos.Take(3)),
            });

        var filas = analisis.Filas.Concat(Enumerable.Range(0, 3).Select(_ => new FilaEditable())).ToList();   // tres filas en blanco para agregar a mano
        return View(nameof(Revisar), ArmarRevision(filas, universidad, nombreCarrera, version, analisis.Avisos, reemplazar: false, usar: true, error: null));
    }

    /// <summary>Revisa lo editado, agrega filas en blanco o guarda el pénsum personal, según el botón pulsado.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Revisar(
        List<FilaEditable>? filas, string? universidad, string? nombreCarrera, string? version, string? accion, bool reemplazar, bool usar, CancellationToken ct)
    {
        filas ??= new();
        if (filas.Count > ImportadorPensumTexto.MaxFilas + 10)
            return View(nameof(Revisar), ArmarRevision(filas.Take(ImportadorPensumTexto.MaxFilas + 10).ToList(), universidad, nombreCarrera, version, new(), reemplazar, usar, "Son demasiadas filas."));

        string? error = null;
        if (accion == "agregar") filas.AddRange(Enumerable.Range(0, 3).Select(_ => new FilaEditable()));
        else if (accion == "guardar")
        {
            var r = await _carrera.GuardarPersonalAsync(filas, universidad, nombreCarrera, version, reemplazar, usar, ct);
            if (r.Ok)
            {
                TempData["Mensaje"] = r.Mensaje;
                return RedirectToAction(nameof(Index));
            }
            error = r.Mensaje;
        }
        return View(nameof(Revisar), ArmarRevision(filas, universidad, nombreCarrera, version, new(), reemplazar, usar, error));
    }

    private RevisarViewModel ArmarRevision(
        List<FilaEditable> filas, string? universidad, string? nombreCarrera, string? version, List<string> avisos, bool reemplazar, bool usar, string? error)
    {
        var (meta, errorDatos) = _carrera.MetadatosPersonales(universidad, nombreCarrera, version);
        return new RevisarViewModel
        {
            Filas = filas,
            Universidades = _reglas.LeerCatalogo().UniversidadesValidas.OrderBy(u => u.Nombre, StringComparer.CurrentCultureIgnoreCase).ToList(),
            Universidad = universidad, NombreCarrera = nombreCarrera, Version = version ?? "",
            Avisos = avisos,
            // Se valida contra la universidad indicada, o contra la activa mientras la persona todavía no la elige.
            Construccion = ImportadorPensumTexto.Construir(filas, meta ?? new MetadatosPensum(_reglas.Activa.Id, "personal-borrador", "Borrador", "1")),
            ErrorDatos = errorDatos,
            Existe = meta is not null && _carrera.YaExistePersonal(meta),
            Reemplazar = reemplazar, Usar = usar, Error = error,
        };
    }

    /// <summary>Borra un pénsum personal (los del catálogo compartido no se pueden borrar desde aquí).</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> EliminarPersonal(string? clave, CancellationToken ct)
    {
        var r = await _carrera.EliminarPersonalAsync(clave, ct);
        TempData[r.Ok ? "Mensaje" : "Error"] = r.Mensaje;
        return RedirectToAction(nameof(Index));
    }

    /// <summary>La guía para agregar un pénsum nuevo (el enlace de «Si mi carrera no está»).</summary>
    [HttpGet]
    public IActionResult Guia()
    {
        var raiz = _reglas.Carpeta is null ? null : Path.GetDirectoryName(_reglas.Carpeta);
        var candidatas = new[]
        {
            raiz is null ? null : Path.Combine(raiz, "CONTRIBUTING-pensums.md"),
            _reglas.Carpeta is null ? null : Path.Combine(_reglas.Carpeta, "README.md"),
        };
        var archivo = candidatas.FirstOrDefault(c => c is not null && System.IO.File.Exists(c));
        return View(new GuiaViewModel
        {
            Html = archivo is null ? null : MarkdownSencillo.AHtml(System.IO.File.ReadAllText(archivo)),
            Archivo = archivo is null ? null : Path.GetFileName(archivo),
        });
    }
}
