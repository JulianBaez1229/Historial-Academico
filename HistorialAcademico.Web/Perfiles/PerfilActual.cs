using HistorialAcademico.Core.Perfiles;

namespace HistorialAcademico.Web.Perfiles;

/// <summary>
/// El perfil con el que se está trabajando en esta petición: de él salen la base de datos, la sesión de Banner y la universidad activa.
/// Lo llena <see cref="PerfilMiddleware"/> al leer la cookie de perfil; con «Perfiles:BaseFija» (pruebas) queda lleno desde el principio.
/// </summary>
public class PerfilActual
{
    private readonly GestorPerfiles _gestor;

    public PerfilActual(GestorPerfiles gestor)
    {
        _gestor = gestor;
        if (gestor.EsFijo) EstablecerFijo();
    }

    /// <summary>El perfil real (null si todavía no se eligió uno, o en modo de base fija).</summary>
    public Perfil? Perfil { get; private set; }

    /// <summary>Identifica al perfil (para separar el estado en memoria); en modo de base fija es «fijo».</summary>
    public string? Id { get; private set; }

    public string? RutaBase { get; private set; }

    /// <summary>La carpeta con .auth/ y samples/ de este perfil.</summary>
    public string? CarpetaDatos { get; private set; }

    public string? UniversidadId { get; private set; }

    public bool Hay => RutaBase is not null;

    public string CadenaConexion => RutaBase is null
        ? throw new InvalidOperationException("Todavía no se eligió un perfil: no hay base de datos que abrir.")
        : $"Data Source={RutaBase}";

    public void Establecer(Perfil perfil)
    {
        Perfil = perfil;
        Id = perfil.Id;
        RutaBase = _gestor.Almacen.RutaBase(perfil.Id);
        CarpetaDatos = _gestor.Almacen.CarpetaDe(perfil.Id);
        UniversidadId = perfil.UniversidadId;
    }

    private void EstablecerFijo()
    {
        Id = "fijo";
        RutaBase = _gestor.BaseFija;
        CarpetaDatos = _gestor.CarpetaFija;
        UniversidadId = _gestor.UniversidadFija;
    }

    /// <summary>Recuerda la universidad del pénsum elegido: la próxima vez que se abra el perfil se usan sus reglas.</summary>
    public void GuardarUniversidad(string universidad)
    {
        UniversidadId = universidad;
        _gestor.GuardarUniversidad(this, universidad);
    }

    /// <summary>Trabaja con el mismo perfil que otro ámbito (el de una consulta que sigue en segundo plano).</summary>
    public void CopiarDe(PerfilActual otro)
    {
        Perfil = otro.Perfil;
        Id = otro.Id;
        RutaBase = otro.RutaBase;
        CarpetaDatos = otro.CarpetaDatos;
        UniversidadId = otro.UniversidadId;
    }
}
