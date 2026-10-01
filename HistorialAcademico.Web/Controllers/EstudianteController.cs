using HistorialAcademico.Core.Pensum;
using HistorialAcademico.Web.Models;
using HistorialAcademico.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace HistorialAcademico.Web.Controllers;

/// <summary>Las cuatro secciones del menú del laboratorio, ahora con datos sincronizados desde Banner.</summary>
public class EstudianteController : Controller
{
    private readonly AcademicoService _academico;

    public EstudianteController(AcademicoService academico) => _academico = academico;

    public async Task<IActionResult> DatosPersonales(CancellationToken ct) => View(await _academico.ObtenerAsync(ct));

    /// <summary>Materias cursadas agrupadas por período, más los cursos en progreso.</summary>
    public async Task<IActionResult> MateriasTomadas(CancellationToken ct) => View(await _academico.ObtenerAsync(ct));

    public async Task<IActionResult> DetalleMateriaTomada(string codigo, CancellationToken ct)
    {
        var estado = await _academico.ObtenerAsync(ct);
        var intentos = estado.Periodos
            .SelectMany(p => p.Materias)
            .Where(m => string.Equals(m.Codigo, codigo, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (intentos.Count == 0) return NotFound();

        // Los períodos vienen ordenados cronológicamente, así que el último intento es el más reciente.
        return View(new DetalleTomadaViewModel { Estado = estado, Intentos = intentos, Pensum = estado.BuscarPensum(codigo) });
    }

    /// <summary>Materias del pénsum que faltan (disponibles y bloqueadas), agrupadas por cuatrimestre.</summary>
    public async Task<IActionResult> MateriasFaltantes(CancellationToken ct) => View(await _academico.ObtenerAsync(ct));

    public async Task<IActionResult> DetalleMateriaFaltante(string codigo, CancellationToken ct)
    {
        var estado = await _academico.ObtenerAsync(ct);
        var materia = estado.Pensum.Buscar(codigo);
        if (materia is null) return NotFound();

        var requisitos = new List<RequisitoEstado>();
        foreach (var req in PrerrequisitoParser.Parse(materia.Materia.Prerrequisitos))
        {
            if (req.Materia is not null)
            {
                var previa = estado.Pensum.Buscar(req.Materia);
                var nombre = previa?.Materia.Nombre;
                var texto = nombre is null ? req.Materia : $"{req.Materia} – {nombre}";
                requisitos.Add(previa?.Estado switch
                {
                    EstadoMateria.Aprobada => new(texto, "Aprobada", "bg-success", true),
                    EstadoMateria.Exenta => new(texto, "Exenta", "bg-info", true),
                    EstadoMateria.EnCurso => new(texto, "En curso", "bg-primary", false),
                    _ => new(texto, "Pendiente", "bg-secondary", false),
                });
            }
            else
            {
                var pct = estado.Pensum.PorcentajeAprobado;
                var cumplido = pct >= req.Porcentaje!.Value;
                requisitos.Add(new($"{req.Porcentaje}% de los créditos del pénsum aprobados",
                    (cumplido ? "Cumplido" : "Pendiente") + $" (llevas {Helpers.Ui.Pct(pct)})",
                    cumplido ? "bg-success" : "bg-secondary", cumplido));
            }
        }

        var desbloquea = estado.Pensum.Materias
            .Where(m => PrerrequisitoParser.Parse(m.Materia.Prerrequisitos)
                .Any(r => string.Equals(r.Materia, materia.Materia.Codigo, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        return View(new DetalleFaltanteViewModel
        {
            Estado = estado,
            Materia = materia,
            Requisitos = requisitos,
            Desbloquea = desbloquea,
            Opciones = ElectivasReferencia.Opciones.TryGetValue(materia.Materia.Codigo, out var op) ? op : null,
        });
    }

    public async Task<IActionResult> IndiceAcademico(CancellationToken ct) => View(await _academico.ObtenerAsync(ct));
}
