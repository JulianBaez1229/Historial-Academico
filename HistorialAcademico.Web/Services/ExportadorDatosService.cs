using System.Text.Encodings.Web;
using System.Text.Json;
using HistorialAcademico.Web.Data;
using HistorialAcademico.Web.Perfiles;
using Microsoft.EntityFrameworkCore;

namespace HistorialAcademico.Web.Services;

/// <summary>
/// Arma el archivo JSON con todo lo tuyo: perfil, pénsum, histórico, materias a mano, equivalencias, planes y horarios. Es una copia para
/// que la guardes o te la lleves: no incluye el PIN (ni siquiera su versión cifrada), la sesión de Banner ni las secciones que se
/// consultaron a Banner (son información pública de la universidad y se vuelven a consultar).
/// </summary>
public class ExportadorDatosService
{
    public const string Formato = "historial-academico-exportacion";
    public const int Version = 1;

    private static readonly JsonSerializerOptions Opciones = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,   // las tildes se escriben tal cual: el archivo se puede leer a simple vista
    };

    private readonly HistorialContext _db;
    private readonly PerfilActual _perfil;
    private readonly ReglasUniversidadService _reglas;

    public ExportadorDatosService(HistorialContext db, PerfilActual perfil, ReglasUniversidadService reglas)
    {
        _db = db;
        _perfil = perfil;
        _reglas = reglas;
    }

    /// <summary>El nombre del archivo: historial-academico-ana-2027-03-01.json (sin caracteres que un sistema de archivos no acepte).</summary>
    public string NombreDeArchivo(DateTime ahora)
    {
        var nombre = new string((_perfil.Perfil?.Nombre ?? "datos").ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) && c < 128 ? c : '-').ToArray()).Trim('-');
        while (nombre.Contains("--")) nombre = nombre.Replace("--", "-");
        return $"historial-academico-{(nombre.Length == 0 ? "datos" : nombre)}-{ahora:yyyy-MM-dd}.json";
    }

    public async Task<byte[]> ExportarAsync(DateTime ahora, CancellationToken ct = default)
    {
        var periodos = await _db.Periodos.AsNoTracking().Include(p => p.Materias).OrderBy(p => p.Orden).ToListAsync(ct);
        var planes = await _db.PlanesEstudio.AsNoTracking().Include(p => p.Periodos).ThenInclude(p => p.Materias).OrderBy(p => p.Id).ToListAsync(ct);
        var horarios = await _db.HorariosTentativos.AsNoTracking().Include(h => h.Secciones).OrderBy(h => h.Id).ToListAsync(ct);
        var config = await _db.ConfiguracionPlanificador.AsNoTracking().FirstOrDefaultAsync(ct);
        var activo = await _db.PensumActivo.AsNoTracking().FirstOrDefaultAsync(ct);
        var alumno = await _db.DatosAlumno.AsNoTracking().FirstOrDefaultAsync(ct);
        var reglas = _reglas.Activa;

        var documento = new
        {
            formato = Formato,
            version = Version,
            exportado = ahora.ToUniversalTime().ToString("O"),
            perfil = _perfil.Perfil is null ? null : new
            {
                nombre = _perfil.Perfil.Nombre,
                creado = _perfil.Perfil.Creado.ToString("O"),
                protegidoConPin = _perfil.Perfil.TienePin,   // solo si lo tiene: nunca el PIN ni su hash
            },
            universidad = new { id = reglas.Id, nombre = reglas.Nombre },
            pensumElegido = activo is null ? null : new { activo.Universidad, activo.Carrera, activo.Version, activo.NombreCarrera, aplicado = activo.Aplicado.ToString("O") },
            pensum = (await _db.MateriasPensum.AsNoTracking().OrderBy(m => m.Cuatrimestre).ThenBy(m => m.Codigo).ToListAsync(ct))
                .Select(m => new { m.Codigo, m.Nombre, m.Creditos, m.Cuatrimestre, m.Prerrequisitos, m.EsElectiva }),
            alumno = alumno is null ? null : new
            {
                alumno.Nombre, fechaNacimiento = alumno.FechaNacimiento?.ToString("yyyy-MM-dd"), alumno.TipoAlumno, alumno.Programa, alumno.Escuela,
                alumno.Campus, alumno.Carrera, alumno.GradoAObtener, alumno.EstadoAcademico,
                totalesBanner = new
                {
                    alumno.TotalHorasIntentadas, alumno.TotalHorasAprobadas, alumno.TotalHorasGanadas, alumno.TotalHorasPga, alumno.TotalPuntosCalidad, alumno.TotalPga,
                },
            },
            historico = periodos.Select(p => new
            {
                p.Orden, p.Nombre, p.Nivel, p.Escuela, p.Carrera, p.TipoAlumno, p.EstadoAcademico,
                p.HorasIntentadas, p.HorasAprobadas, p.HorasGanadas, p.HorasPga, p.PuntosCalidad, p.Pga,
                acumulado = new { p.AcumHorasIntentadas, p.AcumHorasAprobadas, p.AcumHorasGanadas, p.AcumHorasPga, p.AcumPuntosCalidad, p.AcumPga },
                materias = p.Materias.OrderBy(m => m.Id).Select(m => new { m.Codigo, m.Materia, m.Curso, m.Titulo, m.Calificacion, m.HorasCredito, m.PuntosCalidad, m.Campus, m.Nivel }),
            }),
            cursosEnProgreso = (await _db.CursosEnProgreso.AsNoTracking().OrderBy(c => c.Id).ToListAsync(ct))
                .Select(c => new { c.Periodo, c.Codigo, c.Materia, c.Curso, c.Titulo, c.HorasCredito, c.Campus, c.Nivel }),
            materiasManuales = (await _db.MateriasManuales.AsNoTracking().OrderBy(m => m.Id).ToListAsync(ct))
                .Select(m => new { m.Codigo, m.Periodo, m.Calificacion }),
            equivalencias = (await _db.Equivalencias.AsNoTracking().OrderBy(e => e.Id).ToListAsync(ct))
                .Select(e => new { e.CodigoBanner, e.CodigoPensum, e.Nota }),
            planificador = new
            {
                configuracion = config is null ? null : new { config.LimiteBase, config.LimiteAlto, config.UmbralIndice, config.Minimo, config.AsumirEnCurso },
                escenarios = planes.Select(p => new
                {
                    p.Nombre, creado = p.Creado.ToString("O"), actualizado = p.Actualizado.ToString("O"),
                    periodos = p.Periodos.OrderBy(x => x.Id).Select(x => new { x.Nombre, materias = x.Materias.OrderBy(m => m.Id).Select(m => new { m.Codigo, m.Razon }) }),
                }),
            },
            horariosTentativos = horarios.Select(h => new
            {
                h.Nombre, h.Periodo, escenario = planes.FirstOrDefault(p => p.Id == h.PlanEstudioId)?.Nombre,
                creado = h.Creado.ToString("O"), actualizado = h.Actualizado.ToString("O"),
                secciones = h.Secciones.OrderBy(s => s.Id).Select(s => new { s.Nrc, s.Codigo, s.Etiqueta }),
            }),
            horasNoDisponibles = (await _db.BloquesNoDisponibles.AsNoTracking().OrderBy(b => b.Id).ToListAsync(ct))
                .Select(b => new { dia = b.Dia.ToString(), b.DesdeMin, b.HastaMin }),
            aperturasSolicitadas = (await _db.AperturasSolicitadas.AsNoTracking().OrderBy(a => a.Id).ToListAsync(ct))
                .Select(a => new { a.Codigo, a.Nota, marcada = a.Marcada.ToString("O") }),
        };

        return JsonSerializer.SerializeToUtf8Bytes(documento, Opciones);
    }
}
