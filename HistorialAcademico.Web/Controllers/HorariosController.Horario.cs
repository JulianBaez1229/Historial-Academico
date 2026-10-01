using System.Text;
using HistorialAcademico.Core.Horarios;
using HistorialAcademico.Web.Models;
using HistorialAcademico.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace HistorialAcademico.Web.Controllers;

// Horario semanal tentativo, horas no disponibles y materias a solicitar. Todo se guarda en la base local; ninguna acción
// de este archivo habla con Banner.
public partial class HorariosController
{
    // ── Horario tentativo ─────────────────────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> Tentativo(int? id, CancellationToken ct)
    {
        var estado = await _academico.ObtenerAsync(ct);
        var horarios = await _tentativo.ListarAsync(ct);
        var elegido = horarios.FirstOrDefault(h => h.Id == id) ?? horarios.FirstOrDefault();
        var (periodos, porDefecto) = OpcionesDePeriodo(estado, null);

        return View(new TentativoViewModel
        {
            HayPensum = estado.HayPensum,
            Horarios = horarios,
            Vista = elegido is null ? null : await _tentativo.ObtenerAsync(elegido.Id, ct),
            Planes = await _tentativo.PlanesAsync(ct),
            Periodos = periodos,
            PeriodoNuevo = porDefecto,
            Mensaje = TempData["Mensaje"] as string,
            Error = TempData["Error"] as string,
        });
    }

    private IActionResult ResultadoTentativo(ResultadoOp r, int? id)
    {
        TempData[r.Ok ? "Mensaje" : "Error"] = r.Mensaje;
        return RedirectToAction(nameof(Tentativo), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CrearTentativo(string? nombre, string? periodo, CancellationToken ct)
    {
        var r = await _tentativo.CrearAsync(nombre, periodo, ct);
        return ResultadoTentativo(r, r.Id);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RenombrarTentativo(int id, string? nombre, CancellationToken ct) =>
        ResultadoTentativo(await _tentativo.RenombrarAsync(id, nombre, ct), id);

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> EliminarTentativo(int id, CancellationToken ct) =>
        ResultadoTentativo(await _tentativo.EliminarAsync(id, ct), null);

    /// <summary>Asocia el horario a un escenario del planificador (planId vacío = quitar la asociación).</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AsociarPlan(int id, int? planId, CancellationToken ct) =>
        ResultadoTentativo(await _tentativo.AsociarPlanAsync(id, planId, ct), id);

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AgregarSeccion(int id, string? nrc, CancellationToken ct) =>
        ResultadoTentativo(await _tentativo.AgregarAsync(id, nrc, ct), id);

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> QuitarSeccion(int id, string? nrc, CancellationToken ct) =>
        ResultadoTentativo(await _tentativo.QuitarAsync(id, nrc, ct), id);

    // ── Horas no disponibles ──────────────────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> NoDisponible(CancellationToken ct) => View(new NoDisponibleViewModel
    {
        Celdas = HorarioTentativoService.Celdas(await _tentativo.NoDisponiblesAsync(ct)),
        Mensaje = TempData["Mensaje"] as string,
        Error = TempData["Error"] as string,
    });

    /// <summary>Guarda las celdas marcadas de la cuadrícula. Cada valor es «Lunes-8» (día y hora de inicio).</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> GuardarNoDisponible(string[]? celdas, CancellationToken ct)
    {
        var marcadas = new List<(DiasSemana, int)>();
        foreach (var c in celdas ?? Array.Empty<string>())
        {
            var partes = c.Split('-');
            if (partes.Length == 2 && Enum.TryParse<DiasSemana>(partes[0], out var dia) && Enum.IsDefined(dia) && int.TryParse(partes[1], out var hora))
                marcadas.Add((dia, hora));
        }
        var r = await _tentativo.GuardarNoDisponiblesAsync(marcadas, ct);
        TempData[r.Ok ? "Mensaje" : "Error"] = r.Mensaje;
        return RedirectToAction(nameof(NoDisponible));
    }

    // ── Materias a solicitar ──────────────────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> Apertura(string? periodo, CancellationToken ct)
    {
        var estado = await _academico.ObtenerAsync(ct);
        var (periodos, elegido) = OpcionesDePeriodo(estado, periodo);
        var nombre = periodos.First(p => p.Codigo == elegido).Nombre;
        var items = await _apertura.ListarAsync(ct);
        var marcadas = items.Select(i => i.Codigo).ToHashSet(StringComparer.OrdinalIgnoreCase);

        return View(new AperturaViewModel
        {
            Items = items,
            Periodos = periodos,
            Periodo = elegido,
            PeriodoNombre = nombre,
            Candidatas = estado.Faltantes.Where(m => !marcadas.Contains(m.Materia.Codigo))
                .Where(m => MapeoBanner.ConsultasDe(m.Materia.Codigo).Consultas.Count > 0)
                .Select(m => new OpcionMateria(m.Materia.Codigo, m.Materia.Nombre, m.Materia.Cuatrimestre, true)).ToList(),
            Texto = await _apertura.TextoAsync(nombre, ct),
            Mensaje = TempData["Mensaje"] as string,
            Error = TempData["Error"] as string,
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> MarcarApertura(string? codigo, string? nota, string? periodo, string? volverA, CancellationToken ct)
    {
        var r = await _apertura.MarcarAsync(codigo, nota, ct);
        TempData[r.Ok ? "Mensaje" : "Error"] = r.Mensaje;
        return Volver(volverA, nameof(Apertura), new { periodo });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> QuitarApertura(string? codigo, string? periodo, string? volverA, CancellationToken ct)
    {
        var r = await _apertura.QuitarAsync(codigo, ct);
        TempData[r.Ok ? "Mensaje" : "Error"] = r.Mensaje;
        return Volver(volverA, nameof(Apertura), new { periodo });
    }

    /// <summary>El texto de la solicitud como archivo, para adjuntarlo o abrirlo en el correo.</summary>
    [HttpGet]
    public async Task<IActionResult> AperturaTexto(string? periodo, CancellationToken ct)
    {
        var estado = await _academico.ObtenerAsync(ct);
        var (periodos, elegido) = OpcionesDePeriodo(estado, periodo);
        var texto = await _apertura.TextoAsync(periodos.First(p => p.Codigo == elegido).Nombre, ct);
        return File(new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetBytes(texto), "text/plain; charset=utf-8", "solicitud-apertura.txt");
    }
}
