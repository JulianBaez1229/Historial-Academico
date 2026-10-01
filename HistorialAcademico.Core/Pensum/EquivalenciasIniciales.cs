using HistorialAcademico.Core.Entities;

namespace HistorialAcademico.Core.Pensum;

/// <summary>
/// Equivalencias propuestas para empezar. NO se guardan solas: el usuario las confirma y luego las carga
/// con el botón "Cargar valores iniciales" de la página de equivalencias.
/// </summary>
public static class EquivalenciasIniciales
{
    public static IReadOnlyList<Equivalencia> Valores => new[]
    {
        new Equivalencia { CodigoBanner = "ING701", CodigoPensum = "ING716", Nota = "Física I y Laboratorio (plan anterior) → Física I" },
        new Equivalencia { CodigoBanner = "ING701", CodigoPensum = "ING717", Nota = "Física I y Laboratorio (plan anterior) → Laboratorio de Física I" },
        new Equivalencia { CodigoBanner = "ESP102", CodigoPensum = null, Nota = "Redacción de Textos Discursivos I (plan anterior): sin equivalente" },
        new Equivalencia { CodigoBanner = "MAT126", CodigoPensum = null, Nota = "Matemática Básica para Ingenieros (plan anterior): sin equivalente" },
    };
}
