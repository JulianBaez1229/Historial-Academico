using HistorialAcademico.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace HistorialAcademico.Web.ViewComponents;

/// <summary>Una franja discreta arriba del contenido cuando hay una versión más nueva del programa. Sin aviso no dibuja nada.</summary>
public class AvisoActualizacionViewComponent : ViewComponent
{
    private readonly ActualizacionesService _actualizaciones;

    public AvisoActualizacionViewComponent(ActualizacionesService actualizaciones) => _actualizaciones = actualizaciones;

    public IViewComponentResult Invoke() =>
        _actualizaciones.Aviso is { } aviso ? View(aviso) : Content("");
}
