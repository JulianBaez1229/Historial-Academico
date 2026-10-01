using System.Diagnostics;
using HistorialAcademico.Web.Models;
using HistorialAcademico.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace HistorialAcademico.Web.Controllers;

public class HomeController : Controller
{
    private readonly AcademicoService _academico;
    private readonly PlanificadorService _planificador;

    public HomeController(AcademicoService academico, PlanificadorService planificador)
    {
        _academico = academico;
        _planificador = planificador;
    }

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var estado = await _academico.ObtenerAsync(ct);
        return View(new InicioViewModel(estado, await _planificador.EstimarAsync(estado, ct)));
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() => View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
}

/// <summary>Datos de la portada: el estado académico y la estimación de graduación (null si aún no se puede calcular).</summary>
public record InicioViewModel(EstadoAcademico Estado, EstimacionGraduacion? Estimacion);
