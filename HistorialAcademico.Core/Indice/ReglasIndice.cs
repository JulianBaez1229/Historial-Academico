using HistorialAcademico.Core.Universidad;

namespace HistorialAcademico.Core.Indice;

/// <summary>
/// Reglas del índice de UNAPEC (A=4, B=3, C=2, D=1, F=0, sin + ni −; la E exenta cuenta como aprobada pero no entra en el índice).
/// Es un atajo a <see cref="EscalaCalificaciones.Unapec"/>: el cálculo real recibe la escala de la universidad activa
/// (pensums/&lt;universidad&gt;/universidad.json) y solo usa esto cuando no se le indica otra.
/// </summary>
public static class ReglasIndice
{
    private static EscalaCalificaciones Escala => EscalaCalificaciones.Unapec;

    public static int? PuntosPorLetra(string? letra) => Escala.PuntosPorLetra(letra);

    /// <summary>Entra en el índice: toda letra con puntos (A-F). La E y cualquier otra no.</summary>
    public static bool CuentaParaIndice(string? letra) => Escala.CuentaParaIndice(letra);

    public static bool EsExenta(string? letra) => Escala.EsExenta(letra);

    /// <summary>Aprobada de forma directa: A, B, C o D.</summary>
    public static bool EsAprobada(string? letra) => Escala.EsAprobada(letra);

    /// <summary>Cuenta como créditos aprobados: A-D o E.</summary>
    public static bool CuentaComoAprobada(string? letra) => Escala.CuentaComoAprobada(letra);

    public static decimal Redondear(decimal indice) => Escala.Redondear(indice);
}
