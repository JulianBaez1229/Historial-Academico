using HistorialAcademico.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace HistorialAcademico.Web.Controllers;

/// <summary>
/// Paso 1 del asistente: la bienvenida y el aviso de privacidad. Vive aparte porque se abre antes de que exista un perfil, y por eso
/// no puede depender de ninguna base de datos.
/// </summary>
[Route("Asistente/Bienvenida")]
public class BienvenidaController : Controller
{
    [HttpGet("")]
    public IActionResult Index() => View("~/Views/Asistente/Bienvenida.cshtml", new PasosViewModel(1));
}
