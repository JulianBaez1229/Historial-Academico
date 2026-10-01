using HistorialAcademico.Core.Entities;

namespace HistorialAcademico.Core.Pensum;

/// <summary>Una materia del pénsum en el formato estándar. Los prerrequisitos son códigos o reglas de porcentaje («67% créditos aprobados»).</summary>
public record MateriaDef(string Codigo, string Nombre, int Creditos, int Cuatrimestre, List<string> Prerrequisitos, bool Electiva);

public record OpcionElectivaDef(string Codigo, string Nombre);

/// <summary>Una electiva del pénsum (E077) y las materias entre las que se elige.</summary>
public record BloqueElectivasDef(string Codigo, string Nombre, List<OpcionElectivaDef> Opciones);

/// <summary>Una alternativa de certificación: un grupo de materias que se puede tomar en lugar de las electivas generales.</summary>
public record CertificacionDef(string Nombre, List<string> Materias, bool ReemplazaElectivas, string? Nota);

/// <summary>
/// Una equivalencia que declara el pénsum: una materia de un plan anterior (u otra carrera), tal como aparece en el histórico
/// de Banner, que cubre una o varias materias de este pénsum. Sin destinos significa «se sabe que no tiene equivalente».
/// </summary>
public record EquivalenciaDef(string Origen, List<string> Destino, string? Nota);

/// <summary>
/// Un pénsum en el formato estándar (pensums/&lt;universidad&gt;/&lt;carrera&gt;-&lt;versión&gt;.json). Ver pensums/schema.json y
/// CONTRIBUTING-pensums.md. <see cref="Universidad"/> y <see cref="Carrera"/> son identificadores (minúsculas, sin espacios).
/// </summary>
public record PensumDefinicion(
    int Formato,
    string Universidad,
    string Carrera,
    string NombreCarrera,
    string Version,
    int TotalCreditos,
    int Cuatrimestres,
    List<MateriaDef> Materias,
    List<BloqueElectivasDef> BloquesElectivas,
    List<CertificacionDef> Certificaciones,
    List<string> RequisitosGraduacion,
    List<EquivalenciaDef> Equivalencias)
{
    public const int FormatoActual = 1;

    /// <summary>Prefijo de la carrera de los pénsums que cada persona crea pegando texto (archivos que no se comparten: van fuera de Git).</summary>
    public const string PrefijoPersonal = "personal-";

    /// <summary>Un pénsum creado por la persona (pegando texto), no uno del catálogo compartido.</summary>
    public bool EsPersonal => Carrera.StartsWith(PrefijoPersonal, StringComparison.Ordinal);

    /// <summary>Identificador único: unapec/ingenieria-software-11.</summary>
    public string Clave => $"{Universidad}/{NombreArchivo}";

    /// <summary>ingenieria-software-11.json: así se debe llamar el archivo dentro de la carpeta de la universidad.</summary>
    public string NombreArchivo => $"{Carrera}-{Version}.json";

    /// <summary>
    /// Las equivalencias declaradas como filas de equivalencia (una por destino; una sola con destino nulo si «no tiene equivalente»),
    /// el mismo formato que las personales que se guardan en la base.
    /// </summary>
    public List<Equivalencia> AEquivalencias() => Equivalencias.SelectMany(e => e.Destino.Count == 0
        ? new[] { new Equivalencia { CodigoBanner = e.Origen, CodigoPensum = null, Nota = e.Nota } }
        : e.Destino.Select(d => new Equivalencia { CodigoBanner = e.Origen, CodigoPensum = d, Nota = e.Nota }).ToArray()).ToList();

    /// <summary>Las materias como las guarda la base de datos (prerrequisitos en el texto de siempre: «ESP101; 67% créditos aprobados»).</summary>
    public List<MateriaPensum> AMateriasPensum() => Materias.Select(m => new MateriaPensum
    {
        Codigo = m.Codigo, Nombre = m.Nombre, Creditos = m.Creditos, Cuatrimestre = m.Cuatrimestre,
        Prerrequisitos = m.Prerrequisitos.Count == 0 ? null : string.Join("; ", m.Prerrequisitos), EsElectiva = m.Electiva,
    }).ToList();
}

/// <summary>El resultado de leer un pénsum: la definición (solo si no hay errores), los errores que impiden usarlo y las advertencias.</summary>
public record ResultadoPensumJson(PensumDefinicion? Definicion, List<string> Errores, List<string> Advertencias)
{
    public bool EsValido => Definicion is not null && Errores.Count == 0;
}
