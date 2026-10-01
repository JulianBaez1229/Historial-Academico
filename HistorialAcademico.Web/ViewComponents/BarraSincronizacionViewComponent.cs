using HistorialAcademico.Core.Entities;
using HistorialAcademico.Web.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HistorialAcademico.Web.ViewComponents;

public record BarraSincronizacionModelo(Sincronizacion? UltimaExitosa, Sincronizacion? UltimoIntento);

/// <summary>Barra superior: fecha de la última sincronización y botón "Actualizar desde Banner".</summary>
public class BarraSincronizacionViewComponent : ViewComponent
{
    private readonly HistorialContext _db;

    public BarraSincronizacionViewComponent(HistorialContext db) => _db = db;

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var ultimoIntento = (await _db.Sincronizaciones.AsNoTracking().ToListAsync()).OrderByDescending(s => s.Id).FirstOrDefault();
        var ultimaExitosa = (await _db.Sincronizaciones.AsNoTracking().Where(s => s.Resultado == ResultadoSincronizacion.Exito).ToListAsync())
            .OrderByDescending(s => s.Id).FirstOrDefault();
        return View(new BarraSincronizacionModelo(ultimaExitosa, ultimoIntento));
    }
}
