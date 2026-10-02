using HistorialAcademico.Banner;
using HistorialAcademico.Core;
using HistorialAcademico.Core.Entities;
using HistorialAcademico.Core.Models;
using HistorialAcademico.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace HistorialAcademico.Web.Services;

public record ResultadoSync(bool Exito, string Mensaje, bool RequiereLogin = false);

/// <summary>
/// Sincroniza el histórico de Banner con la base de datos local. Solo a demanda.
/// Reemplaza los datos de Banner en una transacción; si algo falla no se toca la base y se registra el error.
/// Los datos propios (pénsum, equivalencias) no se tocan.
/// </summary>
public class SincronizacionService
{
    // Una sola sincronización a la vez (una persona, un computador).
    private static readonly SemaphoreSlim Candado = new(1, 1);

    private readonly HistorialContext _db;
    private readonly BannerClient _banner;

    public SincronizacionService(HistorialContext db, BannerClient banner)
    {
        _db = db;
        _banner = banner;
    }

    /// <summary>Captura el histórico con la sesión guardada y lo aplica. Si la sesión expiró, responde RequiereLogin.</summary>
    public Task<ResultadoSync> SincronizarDesdeBannerAsync(CancellationToken ct = default) => ActualizarAsync(permitirLogin: false, ct);

    /// <summary>
    /// Lo que hace el botón "Actualizar desde Banner": captura con la sesión guardada y, si expiró y se permite,
    /// abre Chromium visible para que el usuario inicie sesión y captura de inmediato (la sesión del Banner
    /// clásico dura pocos minutos). Un intento con sesión caducada que se resuelve con login no se registra como error.
    /// </summary>
    public async Task<ResultadoSync> ActualizarAsync(bool permitirLogin, CancellationToken ct = default)
    {
        await Candado.WaitAsync(ct);
        try
        {
            var r = await CapturarYAplicarAsync(registrarSiExpira: !permitirLogin, ct);
            if (!r.RequiereLogin || !permitirLogin) return r;

            try { await _banner.IniciarSesionAsync(ct); }
            catch (BannerException ex) { return await FallarAsync($"No se pudo iniciar sesión en Banner: {ex.Message}", ct); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return await FallarAsync($"No se pudo iniciar sesión en Banner: {ex.GetType().Name}: {ex.Message}", ct);
            }
            return await CapturarYAplicarAsync(registrarSiExpira: true, ct);
        }
        finally { Candado.Release(); }
    }

    private async Task<ResultadoSync> CapturarYAplicarAsync(bool registrarSiExpira, CancellationToken ct)
    {
        string html;
        try
        {
            var ruta = await _banner.CapturarHistoricoAsync(visible: false, ct);
            html = await File.ReadAllTextAsync(ruta, ct);
        }
        catch (BannerSesionExpiradaException ex)
        {
            if (registrarSiExpira) await RegistrarErrorAsync(ex.Message, ct);
            return new ResultadoSync(false, ex.Message, RequiereLogin: true);
        }
        catch (BannerException ex)
        {
            return await FallarAsync(ex.Message, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Cualquier otro fallo (disco, permisos, navegador) queda registrado en vez de romper la pantalla.
            return await FallarAsync($"Error inesperado al traer el histórico de Banner: {ex.GetType().Name}: {ex.Message}", ct);
        }
        return await AplicarAsync(html, ct);
    }

    /// <summary>Aplica un HTML de histórico ya capturado (lo usan las pruebas y una futura carga manual).</summary>
    public async Task<ResultadoSync> AplicarHtmlAsync(string html, CancellationToken ct = default)
    {
        await Candado.WaitAsync(ct);
        try { return await AplicarAsync(html, ct); }
        finally { Candado.Release(); }
    }

    private async Task<ResultadoSync> AplicarAsync(string html, CancellationToken ct)
    {
        HistoricoBanner historico;
        try
        {
            historico = HistoricoParser.Parse(html);
        }
        catch (HistoricoParseException ex)
        {
            return await FallarAsync($"No pude leer el histórico: {ex.Message}", ct);
        }

        var problemas = ValidadorHistorico.Validar(historico);
        if (problemas.Count > 0)
            return await FallarAsync(
                $"Los totales de Banner no cuadran, no guardé nada. {string.Join(" ", problemas.Take(3))}" +
                (problemas.Count > 3 ? $" (+{problemas.Count - 3} más)" : ""), ct);

        try
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);

            await _db.MateriasCursadas.ExecuteDeleteAsync(ct);
            await _db.Periodos.ExecuteDeleteAsync(ct);
            await _db.CursosEnProgreso.ExecuteDeleteAsync(ct);
            await _db.DatosAlumno.ExecuteDeleteAsync(ct);
            _db.ChangeTracker.Clear();

            _db.DatosAlumno.Add(Mapear(historico.Alumno, historico.TotalGlobal!));
            _db.Periodos.AddRange(historico.Periodos.Select((p, i) => Mapear(p, i + 1)));
            _db.CursosEnProgreso.AddRange(historico.EnProgreso.SelectMany(pe => pe.Cursos.Select(c => Mapear(pe, c))));

            var materias = historico.Periodos.Sum(p => p.Materias.Count);
            var cursos = historico.EnProgreso.Sum(pe => pe.Cursos.Count);
            var mensaje = $"Sincronizado: {historico.Periodos.Count} períodos, {materias} materias, {cursos} cursos en progreso. " +
                          $"Índice según Banner: {historico.TotalGlobal!.Pga:0.00}.";
            // No impiden guardar: Banner a veces lista materias que no cuenta como intentadas (por ejemplo, tras un cambio de pénsum).
            var avisos = ValidadorHistorico.Avisos(historico);
            if (avisos.Count > 0)
                mensaje += $" Ojo, en {avisos.Count} {(avisos.Count == 1 ? "período" : "períodos")} Banner cuenta menos horas intentadas que las materias que lista; " +
                           $"tus puntos y tu índice sí cuadran. {string.Join(" ", avisos.Take(2))}" + (avisos.Count > 2 ? $" (+{avisos.Count - 2} más)" : "");
            _db.Sincronizaciones.Add(Nueva(ResultadoSincronizacion.Exito, mensaje));

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return new ResultadoSync(true, mensaje);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // La transacción se revierte al salir del using: la base queda como estaba.
            return await FallarAsync($"Error al guardar en la base de datos: {ex.GetBaseException().Message}", ct);
        }
    }

    private async Task<ResultadoSync> FallarAsync(string mensaje, CancellationToken ct)
    {
        await RegistrarErrorAsync(mensaje, ct);
        return new ResultadoSync(false, mensaje);
    }

    private async Task RegistrarErrorAsync(string mensaje, CancellationToken ct)
    {
        _db.ChangeTracker.Clear(); // descarta lo que haya quedado a medias del intento fallido
        _db.Sincronizaciones.Add(Nueva(ResultadoSincronizacion.Error, mensaje));
        await _db.SaveChangesAsync(ct);
    }

    private static Sincronizacion Nueva(ResultadoSincronizacion resultado, string mensaje) =>
        new() { Fecha = DateTime.UtcNow, Resultado = resultado, Mensaje = mensaje.Length > 1000 ? mensaje[..1000] : mensaje };

    private static DatosAlumno Mapear(DatosAlumnoBanner a, TotalesBanner g) => new()
    {
        Nombre = a.Nombre, FechaNacimiento = a.FechaNacimiento, TipoAlumno = a.TipoAlumno, Programa = a.Programa, Escuela = a.Escuela,
        Campus = a.Campus, Carrera = a.Carrera, GradoAObtener = a.GradoAObtener, EstadoAcademico = a.EstadoAcademico,
        TotalHorasIntentadas = g.HorasIntentadas, TotalHorasAprobadas = g.HorasAprobadas, TotalHorasGanadas = g.HorasGanadas,
        TotalHorasPga = g.HorasPga, TotalPuntosCalidad = g.PuntosCalidad, TotalPga = g.Pga,
    };

    private static Periodo Mapear(PeriodoHistorico p, int orden)
    {
        var t = p.TotalesPeriodo!;
        var a = p.TotalesAcumulados!;
        return new Periodo
        {
            Orden = orden, Nombre = p.Nombre, Nivel = p.Nivel, Escuela = p.Escuela, Carrera = p.Carrera,
            TipoAlumno = p.TipoAlumno, EstadoAcademico = p.EstadoAcademico,
            HorasIntentadas = t.HorasIntentadas, HorasAprobadas = t.HorasAprobadas, HorasGanadas = t.HorasGanadas,
            HorasPga = t.HorasPga, PuntosCalidad = t.PuntosCalidad, Pga = t.Pga,
            AcumHorasIntentadas = a.HorasIntentadas, AcumHorasAprobadas = a.HorasAprobadas, AcumHorasGanadas = a.HorasGanadas,
            AcumHorasPga = a.HorasPga, AcumPuntosCalidad = a.PuntosCalidad, AcumPga = a.Pga,
            Materias = p.Materias.Select(m => new MateriaCursada
            {
                Codigo = m.Codigo, Materia = m.Materia, Curso = m.Curso, Titulo = m.Titulo, Calificacion = m.Calificacion,
                HorasCredito = m.HorasCredito, PuntosCalidad = m.PuntosCalidad, Campus = m.Campus, Nivel = m.Nivel,
            }).ToList(),
        };
    }

    private static CursoEnProgreso Mapear(PeriodoEnProgreso pe, CursoEnProgresoHistorico c) => new()
    {
        Periodo = pe.Nombre, Codigo = c.Codigo, Materia = c.Materia, Curso = c.Curso, Titulo = c.Titulo,
        HorasCredito = c.HorasCredito, Campus = c.Campus, Nivel = c.Nivel,
    };
}
