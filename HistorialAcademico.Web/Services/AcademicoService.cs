using HistorialAcademico.Core.Entities;
using HistorialAcademico.Core.Indice;
using HistorialAcademico.Core.Manual;
using HistorialAcademico.Core.Pensum;
using HistorialAcademico.Core.Universidad;
using HistorialAcademico.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace HistorialAcademico.Web.Services;

/// <summary>Todo lo que las pantallas necesitan saber, calculado a partir de la base de datos.</summary>
public record EstadoAcademico(
    ResultadoPensum Pensum,
    ResultadoIndice Indice,
    List<string> Advertencias,
    DatosAlumno? Alumno,
    List<Periodo> Periodos,
    List<CursoEnProgreso> CursosEnProgreso,
    List<Equivalencia> Equivalencias,
    List<MateriaPensum> MateriasPensum,
    bool HayPensum,
    bool HayHistorico,
    ReglasUniversidad Reglas,
    List<Equivalencia> EquivalenciasDeclaradas)
{
    /// <summary>Hay materias registradas a mano que cuentan en este estado (períodos que Banner no trae).</summary>
    public bool HayManuales { get; init; }

    public IEnumerable<MateriaCursada> Cursadas => Periodos.SelectMany(p => p.Materias);

    public MateriaPensum? BuscarPensum(string codigo) =>
        MateriasPensum.FirstOrDefault(m => string.Equals(m.Codigo, codigo, StringComparison.OrdinalIgnoreCase));

    /// <summary>Primer período del histórico de Banner.</summary>
    public string? PeriodoIngreso => Periodos.OrderBy(p => p.Orden).FirstOrDefault()?.Nombre;

    /// <summary>Materias del pénsum que no están aprobadas, exentas ni en curso (Disponibles o Bloqueadas).</summary>
    public IEnumerable<EstadoMateriaPensum> Faltantes =>
        Pensum.Materias.Where(m => m.Estado is EstadoMateria.Disponible or EstadoMateria.Bloqueada);

    /// <summary>Códigos del pénsum a los que equivale una materia de Banner (p. ej. ING701 → ING716, ING717).</summary>
    public List<string> EquivalentesDe(string codigoBanner) =>
        Equivalencias.Where(e => e.CodigoPensum is not null && string.Equals(e.CodigoBanner, codigoBanner, StringComparison.OrdinalIgnoreCase))
            .Select(e => e.CodigoPensum!).ToList();

    public bool SinEquivalente(string codigoBanner) =>
        Equivalencias.Any(e => e.CodigoPensum is null && string.Equals(e.CodigoBanner, codigoBanner, StringComparison.OrdinalIgnoreCase));
}

public class AcademicoService
{
    private readonly HistorialContext _db;
    private readonly ReglasUniversidadService? _reglas;

    /// <param name="reglas">Las reglas de la universidad activa; sin ellas (pruebas) se usan las de UNAPEC.</param>
    public AcademicoService(HistorialContext db, ReglasUniversidadService? reglas = null)
    {
        _db = db;
        _reglas = reglas;
    }

    /// <summary>Las reglas de la universidad activa (escala, períodos y límites de créditos).</summary>
    public ReglasUniversidad Reglas => _reglas?.Activa ?? ReglasUniversidad.Unapec;

    public async Task<EstadoAcademico> ObtenerAsync(CancellationToken ct = default)
    {
        var pensum = (await _db.MateriasPensum.AsNoTracking().ToListAsync(ct))
            .OrderBy(m => m.Cuatrimestre).ThenBy(m => m.Codigo).ToList();
        var periodos = await _db.Periodos.AsNoTracking().Include(p => p.Materias).OrderBy(p => p.Orden).ToListAsync(ct);
        var cursos = await _db.CursosEnProgreso.AsNoTracking().ToListAsync(ct);
        var personales = await _db.Equivalencias.AsNoTracking().ToListAsync(ct);
        var alumno = await _db.DatosAlumno.AsNoTracking().FirstOrDefaultAsync(ct);

        // Las equivalencias que declara el pénsum elegido del catálogo y las personales, juntas.
        var clave = (await _db.PensumActivo.AsNoTracking().FirstOrDefaultAsync(ct))?.Clave;
        var declaradas = _reglas?.EquivalenciasDeclaradas(clave) ?? new List<Equivalencia>();
        var equivalencias = EquivalenciasEfectivas.Unir(declaradas, personales);

        var reglas = Reglas;

        // Lo que Banner publica se compara con lo calculado ANTES de mezclar lo escrito a mano: así los avisos de diferencias
        // solo hablan de períodos que trae Banner, y una materia a mano nunca parece un error de Banner.
        var advertencias = ComparadorConBanner.Comparar(CalculadoraIndice.Calcular(periodos, reglas.Escala), periodos, alumno);

        var manuales = await _db.MateriasManuales.AsNoTracking().ToListAsync(ct);
        var fusion = HistoricoManual.Fusionar(periodos, cursos, manuales, pensum, reglas);
        periodos = fusion.Periodos;
        cursos = fusion.Cursos;

        var cursadas = periodos.SelectMany(p => p.Materias).ToList();
        var resultadoPensum = MotorEstadoPensum.Calcular(pensum, cursadas, cursos, equivalencias, reglas.Escala);
        var indice = CalculadoraIndice.Calcular(periodos, reglas.Escala);

        return new EstadoAcademico(resultadoPensum, indice, advertencias, alumno, periodos, cursos, equivalencias, pensum,
            pensum.Count > 0, periodos.Count > 0, reglas, declaradas)
        {
            HayManuales = fusion.Estados.Values.Any(e => e == EstadoManual.Aplicada),
        };
    }
}
