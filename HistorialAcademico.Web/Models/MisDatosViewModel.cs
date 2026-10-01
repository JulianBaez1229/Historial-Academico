namespace HistorialAcademico.Web.Models;

/// <summary>Cuánto hay guardado de cada cosa (para que la persona sepa qué se exporta y qué se borraría).</summary>
public record ConteoDatos(int Periodos, int MateriasCursadas, int CursosEnProgreso, int MateriasManuales, int MateriasPensum, int Planes, int Horarios, int Equivalencias)
{
    public bool HayAlgo => Periodos + MateriasCursadas + CursosEnProgreso + MateriasManuales + MateriasPensum + Planes + Horarios + Equivalencias > 0;
}

/// <summary>Cómo están los avisos de versiones nuevas (para mostrarlos en «Mis datos»).</summary>
/// <param name="Disponible">Este programa puede consultar: es uno publicado y sabe de qué repositorio salió.</param>
/// <param name="Avisar">La persona no apagó los avisos.</param>
public record InfoActualizaciones(string Version, string? Repositorio, bool Disponible, bool Avisar);

public class MisDatosViewModel
{
    public InfoActualizaciones Actualizaciones { get; init; } = new("", null, false, false);
    /// <summary>Null cuando la aplicación corre con una base fija (pruebas y desarrollo): no hay perfil que borrar.</summary>
    public string? NombrePerfil { get; init; }
    public bool TienePin { get; init; }
    public string Carpeta { get; init; } = "";
    public long BytesBase { get; init; }
    public bool HaySesionBanner { get; init; }
    public bool HayCapturasBanner { get; init; }
    public ConteoDatos Conteo { get; init; } = new(0, 0, 0, 0, 0, 0, 0, 0);
    /// <summary>La base de la versión anterior (una sola, junto al proyecto), si todavía existe: no es de ningún perfil y no se borra desde aquí.</summary>
    public string? BaseAnterior { get; init; }
    public string? Mensaje { get; init; }
    public string? Error { get; init; }
    public bool PuedeBorrar => NombrePerfil is not null;
}
