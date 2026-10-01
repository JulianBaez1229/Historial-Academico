using HistorialAcademico.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace HistorialAcademico.Web.Data;

public class HistorialContext : DbContext
{
    public HistorialContext(DbContextOptions<HistorialContext> options) : base(options) { }

    public DbSet<DatosAlumno> DatosAlumno => Set<DatosAlumno>();
    public DbSet<Periodo> Periodos => Set<Periodo>();
    public DbSet<MateriaCursada> MateriasCursadas => Set<MateriaCursada>();
    public DbSet<CursoEnProgreso> CursosEnProgreso => Set<CursoEnProgreso>();
    public DbSet<MateriaPensum> MateriasPensum => Set<MateriaPensum>();
    public DbSet<Equivalencia> Equivalencias => Set<Equivalencia>();
    public DbSet<Sincronizacion> Sincronizaciones => Set<Sincronizacion>();
    public DbSet<ConfiguracionPlanificador> ConfiguracionPlanificador => Set<ConfiguracionPlanificador>();
    public DbSet<PlanEstudio> PlanesEstudio => Set<PlanEstudio>();
    public DbSet<PeriodoPlanificado> PeriodosPlanificados => Set<PeriodoPlanificado>();
    public DbSet<MateriaPlanificada> MateriasPlanificadas => Set<MateriaPlanificada>();

    public DbSet<SeccionOfertada> SeccionesOfertadas => Set<SeccionOfertada>();
    public DbSet<ConsultaSecciones> ConsultasSecciones => Set<ConsultaSecciones>();
    public DbSet<PensumActivo> PensumActivo => Set<PensumActivo>();
    public DbSet<HorarioTentativo> HorariosTentativos => Set<HorarioTentativo>();
    public DbSet<SeccionElegida> SeccionesElegidas => Set<SeccionElegida>();
    public DbSet<BloqueNoDisponible> BloquesNoDisponibles => Set<BloqueNoDisponible>();
    public DbSet<AperturaSolicitada> AperturasSolicitadas => Set<AperturaSolicitada>();
    public DbSet<MateriaManual> MateriasManuales => Set<MateriaManual>();
    public DbSet<NotaEsperada> NotasEsperadas => Set<NotaEsperada>();
    public DbSet<AvisoPlan> AvisosPlan => Set<AvisoPlan>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<SeccionOfertada>(e =>
        {
            e.Property(s => s.Periodo).HasMaxLength(6).IsRequired();
            e.Property(s => s.Nrc).HasMaxLength(10).IsRequired();
            e.Property(s => s.Codigo).HasMaxLength(20).IsRequired();
            e.Property(s => s.Titulo).HasMaxLength(120);
            e.Property(s => s.Seccion).HasMaxLength(10);
            e.Property(s => s.Profesor).HasMaxLength(200);
            e.Property(s => s.Creditos).HasPrecision(4, 1);
            e.HasIndex(s => new { s.Periodo, s.Nrc }).IsUnique();
            e.HasIndex(s => new { s.Codigo, s.Periodo });
        });

        b.Entity<PensumActivo>(e =>
        {
            e.Property(p => p.Universidad).HasMaxLength(60).IsRequired();
            e.Property(p => p.Carrera).HasMaxLength(60).IsRequired();
            e.Property(p => p.Version).HasMaxLength(40).IsRequired();
            e.Property(p => p.NombreCarrera).HasMaxLength(120);
            e.Ignore(p => p.Clave);
        });

        b.Entity<HorarioTentativo>(e =>
        {
            e.Property(h => h.Nombre).HasMaxLength(60).IsRequired();
            e.Property(h => h.Periodo).HasMaxLength(6).IsRequired();
            e.HasIndex(h => h.Nombre).IsUnique();
            e.HasOne<PlanEstudio>().WithMany().HasForeignKey(h => h.PlanEstudioId).OnDelete(DeleteBehavior.SetNull);
            e.HasMany(h => h.Secciones).WithOne().HasForeignKey(s => s.HorarioTentativoId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<SeccionElegida>(e =>
        {
            e.Property(s => s.Nrc).HasMaxLength(10).IsRequired();
            e.Property(s => s.Codigo).HasMaxLength(20).IsRequired();
            e.Property(s => s.Etiqueta).HasMaxLength(120);
            e.HasIndex(s => new { s.HorarioTentativoId, s.Codigo }).IsUnique();   // una sección por materia
        });

        b.Entity<BloqueNoDisponible>(e => e.HasIndex(x => new { x.Dia, x.DesdeMin }));

        b.Entity<AvisoPlan>(e =>
        {
            e.Property(a => a.PlanNombre).HasMaxLength(60).IsRequired();
            e.Property(a => a.Texto).HasMaxLength(1000).IsRequired();
            e.HasIndex(a => a.Leido);
        });

        b.Entity<NotaEsperada>(e =>
        {
            e.Property(n => n.Codigo).HasMaxLength(20).IsRequired();
            e.Property(n => n.Letra).HasMaxLength(5).IsRequired();
            e.HasIndex(n => n.Codigo).IsUnique();
        });

        b.Entity<MateriaManual>(e =>
        {
            e.Property(m => m.Codigo).HasMaxLength(20).IsRequired();
            e.Property(m => m.Periodo).HasMaxLength(20).IsRequired();
            e.Property(m => m.Calificacion).HasMaxLength(5).IsRequired();
            e.HasIndex(m => new { m.Codigo, m.Periodo }).IsUnique();   // una vez por materia y período
        });

        b.Entity<AperturaSolicitada>(e =>
        {
            e.Property(a => a.Codigo).HasMaxLength(20).IsRequired();
            e.Property(a => a.Nota).HasMaxLength(300);
            e.HasIndex(a => a.Codigo).IsUnique();
        });

        b.Entity<ConsultaSecciones>(e =>
        {
            e.Property(c => c.Periodo).HasMaxLength(6).IsRequired();
            e.Property(c => c.Codigo).HasMaxLength(20).IsRequired();
            e.HasIndex(c => new { c.Periodo, c.Codigo }).IsUnique();
        });

        b.Entity<Periodo>(e =>
        {
            e.Property(p => p.Nombre).HasMaxLength(40).IsRequired();
            e.HasIndex(p => p.Nombre).IsUnique();
            e.HasIndex(p => p.Orden).IsUnique();
            e.HasMany(p => p.Materias).WithOne(m => m.Periodo!).HasForeignKey(m => m.PeriodoId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<MateriaCursada>(e =>
        {
            e.Property(m => m.Codigo).HasMaxLength(20).IsRequired();
            e.HasIndex(m => m.Codigo);
        });

        b.Entity<CursoEnProgreso>(e =>
        {
            e.Property(c => c.Codigo).HasMaxLength(20).IsRequired();
            e.HasIndex(c => c.Codigo);
        });

        b.Entity<MateriaPensum>(e =>
        {
            e.Property(m => m.Codigo).HasMaxLength(20).IsRequired();
            e.HasIndex(m => m.Codigo).IsUnique();
        });

        b.Entity<Equivalencia>(e =>
        {
            e.Property(x => x.CodigoBanner).HasMaxLength(20).IsRequired();
            e.Property(x => x.CodigoPensum).HasMaxLength(20);
            e.HasIndex(x => new { x.CodigoBanner, x.CodigoPensum }).IsUnique();
        });

        b.Entity<PlanEstudio>(e =>
        {
            e.Property(p => p.Nombre).HasMaxLength(60).IsRequired();
            e.HasIndex(p => p.Nombre).IsUnique();
            e.HasMany(p => p.Periodos).WithOne().HasForeignKey(p => p.PlanEstudioId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<PeriodoPlanificado>(e =>
        {
            e.Property(p => p.Nombre).HasMaxLength(20).IsRequired();
            e.HasIndex(p => new { p.PlanEstudioId, p.Nombre }).IsUnique();
            e.HasMany(p => p.Materias).WithOne().HasForeignKey(m => m.PeriodoPlanificadoId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<MateriaPlanificada>(e =>
        {
            e.Property(m => m.Codigo).HasMaxLength(20).IsRequired();
            e.Property(m => m.Razon).HasMaxLength(500);
            e.HasIndex(m => new { m.PeriodoPlanificadoId, m.Codigo }).IsUnique();
        });

        b.Entity<Sincronizacion>(e =>
        {
            e.Property(s => s.Resultado).HasConversion<string>().HasMaxLength(10);
            e.HasIndex(s => s.Fecha);
        });
    }
}
