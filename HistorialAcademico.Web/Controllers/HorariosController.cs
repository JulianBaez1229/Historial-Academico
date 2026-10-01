using HistorialAcademico.Core.Horarios;
using HistorialAcademico.Core.Pensum;
using HistorialAcademico.Core.Planificacion;
using HistorialAcademico.Web.Models;
using HistorialAcademico.Web.Perfiles;
using HistorialAcademico.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace HistorialAcademico.Web.Controllers;

/// <summary>
/// Secciones publicadas en Banner de las materias que te faltan (profesor, horario, aula, cupos).
/// Banner solo se consulta cuando pulsas el botón; nada corre en segundo plano.
/// </summary>
public partial class HorariosController : Controller
{
    private readonly AcademicoService _academico;
    private readonly HorariosService _horarios;
    private readonly ConsultaMasivaService _masiva;
    private readonly HorarioTentativoService _tentativo;
    private readonly AperturaService _apertura;
    private readonly PerfilActual _perfil;

    public HorariosController(AcademicoService academico, HorariosService horarios, ConsultaMasivaService masiva,
        HorarioTentativoService tentativo, AperturaService apertura, PerfilActual perfil)
    {
        _perfil = perfil;
        _academico = academico;
        _horarios = horarios;
        _masiva = masiva;
        _tentativo = tentativo;
        _apertura = apertura;
    }

    /// <summary>Los períodos que se ofrecen (el más futuro primero) y el elegido; si el pedido no está en la lista, el primero en que puedes inscribir.</summary>
    private static (List<OpcionPeriodo> Periodos, string Elegido) OpcionesDePeriodo(EstadoAcademico estado, string? pedido)
    {
        var referencia = PeriodoDeReferencia(estado);
        var periodos = Enumerable.Range(-3, 6).Reverse()
            .Select(k => referencia.Avanzar(k))
            .Select(p => new OpcionPeriodo(MapeoBanner.CodigoDePeriodo(p), p.Nombre)).ToList();
        return (periodos, periodos.Any(p => p.Codigo == pedido) ? pedido! : MapeoBanner.CodigoDePeriodo(referencia));
    }

    /// <summary>Solo se vuelve a una ruta local (evita redirecciones abiertas).</summary>
    private IActionResult Volver(string? volverA, string accion, object? valores = null) =>
        !string.IsNullOrEmpty(volverA) && Url.IsLocalUrl(volverA) ? LocalRedirect(volverA) : RedirectToAction(accion, valores);

    /// <summary>Materias que ya puedes inscribir (cumples sus requisitos) y se pueden consultar por horario.</summary>
    private static List<(string Codigo, string Nombre)> Disponibles(EstadoAcademico estado) =>
        estado.Faltantes.Where(m => m.Estado == EstadoMateria.Disponible)
            .Where(m => MapeoBanner.ConsultasDe(m.Materia.Codigo).Consultas.Count > 0)
            .Select(m => (m.Materia.Codigo, m.Materia.Nombre)).ToList();

    [HttpGet]
    public async Task<IActionResult> Index(string? materia, string? periodo, CancellationToken ct)
    {
        var estado = await _academico.ObtenerAsync(ct);
        var opciones = estado.Faltantes
            .Select(m => new OpcionMateria(m.Materia.Codigo, m.Materia.Nombre, m.Materia.Cuatrimestre, MapeoBanner.ConsultasDe(m.Materia.Codigo).Consultas.Count > 0))
            .ToList();

        var (periodos, elegido) = OpcionesDePeriodo(estado, periodo);

        var codigo = opciones.FirstOrDefault(o => o.Consultable && string.Equals(o.Codigo, materia, StringComparison.OrdinalIgnoreCase))?.Codigo;

        return View(new HorariosViewModel
        {
            HayPensum = estado.HayPensum,
            HayHistorico = estado.HayHistorico,
            Materias = opciones.Where(o => o.Consultable).ToList(),
            SinHorario = opciones.Where(o => !o.Consultable).ToList(),
            Periodos = periodos,
            Materia = codigo,
            Periodo = elegido,
            Vista = codigo is null ? null : await _horarios.VerAsync(codigo, elegido, ct),
            Previa = codigo is null ? new() : await _horarios.OfertaPreviaAsync(codigo, ct),
            Profesores = codigo is null ? new() : await _horarios.ProfesoresDeAsync(codigo, ct),
            AperturaMarcadas = await _apertura.MarcadasAsync(ct),
            Panorama = await _horarios.PanoramaAsync(Disponibles(estado), elegido, ct),
            Masiva = _masiva.EstadoDe(_perfil),
            Mensaje = TempData["Mensaje"] as string,
            Error = TempData["Error"] as string,
        });
    }

    /// <summary>
    /// Consulta en Banner, una tras otra y con pausa, todas las materias disponibles del período. Corre mientras la
    /// aplicación está abierta; el avance se ve en la pantalla y se puede cancelar.
    /// </summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ConsultarTodas(string? periodo, bool omitirConsultadas, CancellationToken ct)
    {
        var estado = await _academico.ObtenerAsync(ct);
        var (iniciada, mensaje) = _masiva.Iniciar(Disponibles(estado), periodo ?? "", omitirConsultadas, _perfil);
        TempData[iniciada ? "Mensaje" : "Error"] = mensaje;
        return RedirectToAction(nameof(Index), new { periodo });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult Cancelar(string? periodo)
    {
        var cancelando = _masiva.Cancelar(_perfil);
        TempData[cancelando ? "Mensaje" : "Error"] = cancelando
            ? "Cancelando: terminará la materia que va y no seguirá con las demás."
            : "No hay ninguna consulta en curso.";
        return RedirectToAction(nameof(Index), new { periodo });
    }

    /// <summary>El avance de la consulta de varias materias, para la barra de progreso (se consulta cada par de segundos).</summary>
    [HttpGet]
    public IActionResult Progreso()
    {
        var e = _masiva.EstadoDe(_perfil);
        if (e is null) return Json(new { hay = false });
        return Json(new
        {
            hay = true, activa = e.Activa, terminada = e.Terminada, cancelada = e.Cancelada, exito = e.Exito, requiereLogin = e.RequiereLogin,
            periodo = e.Periodo, periodoNombre = e.PeriodoNombre, total = e.Total, hechas = e.Hechas, porcentaje = e.Porcentaje,
            fase = e.Fase, actual = e.Actual, mensaje = e.Mensaje,
            conSecciones = e.ConSecciones, sinSecciones = e.SinSecciones, conError = e.ConError, omitidas = e.Omitidas, totalSecciones = e.TotalSecciones,
            items = e.Items.Select(i => new { codigo = i.Codigo, nombre = i.Nombre, resultado = i.Resultado.ToString(), secciones = i.Secciones, mensaje = i.Mensaje }),
        });
    }

    /// <summary>
    /// Consulta la materia en el período. Si la sesión de Banner caducó, abre Chromium para que inicies sesión y
    /// reintenta. La petición dura mientras esperas el login (hasta 5 minutos).
    /// </summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Consultar(string? materia, string? periodo, CancellationToken ct)
    {
        var r = await _horarios.ConsultarAsync(materia ?? "", periodo ?? "", permitirLogin: true, ct);
        TempData[r.Exito ? "Mensaje" : "Error"] = r.Mensaje;
        return RedirectToAction(nameof(Index), new { materia, periodo });
    }

    /// <summary>Revisa los últimos períodos para saber cuándo se ofreció la materia por última vez.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> BuscarAnteriores(string? materia, string? periodo, CancellationToken ct)
    {
        var r = await _horarios.BuscarAnterioresAsync(materia ?? "", periodo ?? "", permitirLogin: true, ct);
        TempData[r.Exito ? "Mensaje" : "Error"] = r.Mensaje;
        return RedirectToAction(nameof(Index), new { materia, periodo });
    }

    /// <summary>El período que sigue al último con cursos en progreso: el primero en el que puedes inscribir.</summary>
    internal static PeriodoAcademico PeriodoDeReferencia(EstadoAcademico estado)
    {
        PeriodoAcademico? enProgreso = estado.CursosEnProgreso.Select(c => c.Periodo).Distinct()
            .Where(n => PeriodoAcademico.TryParse(n, out _)).Select(PeriodoAcademico.Parse)
            .OrderBy(p => p).Select(p => (PeriodoAcademico?)p).LastOrDefault();
        var ultimoCerrado = estado.Periodos.OrderBy(p => p.Orden).LastOrDefault()?.Nombre;

        return PeriodoAcademico.Primero(enProgreso?.Nombre, ultimoCerrado, asumirEnCurso: true) ?? MapeoPeriodoActual();
    }

    private static PeriodoAcademico MapeoPeriodoActual()
    {
        var hoy = DateTime.Today;
        return new PeriodoAcademico(hoy.Year, hoy.Month <= 4 ? 0 : hoy.Month <= 8 ? 1 : 2);
    }
}
