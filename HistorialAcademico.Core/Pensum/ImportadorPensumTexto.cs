using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace HistorialAcademico.Core.Pensum;

/// <summary>
/// Una fila del pénsum tal como se ve y se edita en la vista previa. Todo son textos (y no números) para poder mostrar lo que la persona
/// escribió aunque no sea válido, y explicarle qué corregir.
/// </summary>
public class FilaEditable
{
    // El navegador envía un campo vacío y MVC lo entrega como null: se guarda como texto vacío para que nada tenga que revisar nulos.
    private string _codigo = "", _nombre = "", _creditos = "", _cuatrimestre = "", _prerrequisitos = "";

    public string Codigo { get => _codigo; set => _codigo = value ?? ""; }
    public string Nombre { get => _nombre; set => _nombre = value ?? ""; }
    public string Creditos { get => _creditos; set => _creditos = value ?? ""; }
    public string Cuatrimestre { get => _cuatrimestre; set => _cuatrimestre = value ?? ""; }
    /// <summary>Códigos o reglas de porcentaje separados por «;» (ej. «ISO515; 67% créditos aprobados»).</summary>
    public string Prerrequisitos { get => _prerrequisitos; set => _prerrequisitos = value ?? ""; }
    public bool Electiva { get; set; }
    /// <summary>Marcada para no incluirla en el pénsum.</summary>
    public bool Quitar { get; set; }
    /// <summary>Línea del texto pegado de la que salió (0 = agregada a mano).</summary>
    public int Linea { get; set; }
}

public record ResultadoAnalisis(List<FilaEditable> Filas, List<string> Avisos)
{
    public bool CuatrimestresEstimados { get; init; }
}

/// <summary>Lo que salió de armar el pénsum con las filas de la vista previa.</summary>
public record ResultadoConstruccion(
    ResultadoPensumJson Resultado, Dictionary<FilaEditable, List<string>> ErroresPorFila, List<string> ErroresGenerales, int TotalCreditos, int Cuatrimestres)
{
    public bool EsValido => Resultado.EsValido && ErroresPorFila.Count == 0 && ErroresGenerales.Count == 0;
}

/// <summary>
/// Convierte el texto de un plan de estudios copiado de la página de una universidad (una tabla con código, asignatura, créditos y
/// prerrequisitos) en filas editables, y arma con ellas un pénsum válido. Entiende columnas separadas por tabulador, «|», «;» o dos o
/// más espacios; un encabezado (que fija el orden de las columnas); títulos de cuatrimestre («Cuatrimestre 3», «Tercer semestre», «IV Ciclo»);
/// y líneas de un solo espacio entre palabras. Lo que no entiende no lo inventa: lo deja como aviso.
/// </summary>
public static class ImportadorPensumTexto
{
    public const int MaxCaracteres = 200_000;
    public const int MaxFilas = 300;

    private static readonly Regex CodigoRx = new(@"^[A-Za-z]{1,6}[- ]?\d{1,4}[A-Za-z]?$", RegexOptions.Compiled);
    private static readonly Regex CodigoLetrasRx = new(@"^[A-Z]{2,6}$", RegexOptions.Compiled);
    private static readonly Regex PorcentajeRx = new(@"(\d{1,3})\s*%", RegexOptions.Compiled);
    private static readonly Regex LineaSinDelimitador = new(@"^(?<cod>[A-Za-z]{1,6}[- ]?\d{1,4}[A-Za-z]?)\s+(?<resto>\S.*)$", RegexOptions.Compiled);
    private static readonly Regex CodigoValido = new("^[A-Z0-9]{2,12}$", RegexOptions.Compiled);

    private const string Periodos = "cuatrimestre|cuatri|semestre|trimestre|per[ií]odo|ciclo|nivel|bimestre";
    private static readonly Regex TituloNumero = new($@"^\s*(?:{Periodos})\s*[:\-–]?\s*(?<n>\d{{1,2}}|[IVXivx]{{1,5}})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex TituloOrdinalNumerico = new($@"^\s*(?<n>\d{{1,2}})\s*(?:er|ro|do|to|vo|mo|º|°|ª|\.)?\s*(?:{Periodos})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex TituloPalabra = new($@"^\s*(?<w>primer[o]?|segundo|tercer[o]?|cuarto|quinto|sexto|s[eé]ptimo|octavo|noveno|d[eé]cimo|und[eé]cimo|duod[eé]cimo)\s+(?:{Periodos})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex TituloRomanoPrimero = new($@"^\s*(?<n>[IVX]{{1,5}})\s+(?:{Periodos})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Relleno = new(@"\b(?:de|del|los|las|el|la|y|créditos|creditos|aprobados|aprobado|haber|total|porcentaje|mínimo|minimo)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Dictionary<string, int> Ordinales = new(StringComparer.OrdinalIgnoreCase)
    {
        ["primer"] = 1, ["primero"] = 1, ["segundo"] = 2, ["tercer"] = 3, ["tercero"] = 3, ["cuarto"] = 4, ["quinto"] = 5, ["sexto"] = 6,
        ["septimo"] = 7, ["séptimo"] = 7, ["octavo"] = 8, ["noveno"] = 9, ["decimo"] = 10, ["décimo"] = 10, ["undecimo"] = 11, ["undécimo"] = 11,
        ["duodecimo"] = 12, ["duodécimo"] = 12,
    };

    private enum Columna { Codigo, Nombre, Creditos, Prerrequisitos, Cuatrimestre, Electiva, Ignorar }

    // ── Analizar el texto ─────────────────────────────────────────────────────────────────

    public static ResultadoAnalisis Analizar(string? texto)
    {
        var filas = new List<FilaEditable>();
        var avisos = new List<string>();
        if (string.IsNullOrWhiteSpace(texto)) return new(filas, new() { "No pegaste ningún texto." });
        if (texto.Length > MaxCaracteres) return new(filas, new() { $"El texto es demasiado largo (máximo {MaxCaracteres:N0} caracteres)." });

        var lineas = texto.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        Columna[]? mapa = null;
        var cuatrimestre = 0;
        var sinEntender = 0;

        for (var i = 0; i < lineas.Length; i++)
        {
            var n = i + 1;
            var linea = lineas[i].Trim().TrimStart('﻿');
            if (linea.Length == 0) continue;

            if (EsEncabezado(linea, out var columnas)) { mapa = columnas.Length == 0 ? null : columnas; continue; }
            if (TituloDePeriodo(linea) is { } periodo) { cuatrimestre = periodo; continue; }

            var fila = LeerFila(linea, mapa);
            if (fila is null)
            {
                // Una línea de un solo texto entre filas suele ser un título («Plan 2019», «Total de créditos»): se avisa, sin alarmar.
                if (++sinEntender <= 15) avisos.Add($"Línea {n}: no la entendí como una materia, la salté: «{Recortar(linea)}».");
                continue;
            }

            fila.Linea = n;
            if (fila.Cuatrimestre.Length == 0 && cuatrimestre > 0) fila.Cuatrimestre = cuatrimestre.ToString(CultureInfo.InvariantCulture);
            filas.Add(fila);
            if (filas.Count >= MaxFilas) { avisos.Add($"Solo se leyeron las primeras {MaxFilas} materias."); break; }
        }
        if (sinEntender > 15) avisos.Add($"Y {sinEntender - 15} líneas más que tampoco se entendieron como materias.");
        if (filas.Count == 0) avisos.Insert(0, "No encontré ninguna materia. Pega la tabla del plan de estudios con al menos el código, la asignatura y los créditos de cada una.");

        var estimados = false;
        if (filas.Count > 0 && filas.All(f => f.Cuatrimestre.Length == 0))
        {
            EstimarCuatrimestres(filas);
            estimados = true;
            avisos.Add("El texto no indicaba el cuatrimestre de cada materia: lo estimé según los prerrequisitos. Revisa la columna «Cuatrimestre».");
        }
        return new(filas, avisos) { CuatrimestresEstimados = estimados };
    }

    private static string Recortar(string s) => s.Length <= 70 ? s : s[..67] + "…";

    private static string Sin(string s)
    {
        var sb = new StringBuilder();
        foreach (var c in s.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(char.ToLowerInvariant(c));
        return sb.ToString().Trim();
    }

    // ── Encabezado y columnas ─────────────────────────────────────────────────────────────

    private static bool EsEncabezado(string linea, out Columna[] columnas)
    {
        columnas = Array.Empty<Columna>();
        var celdas = Dividir(linea) ?? linea.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();   // sin delimitador claro: palabra por palabra
        if (celdas.Count < 2) return false;
        var mapa = celdas.Select(c => Sin(c) switch
        {
            "codigo" or "clave" or "cod" or "cod." or "code" or "sigla" => Columna.Codigo,
            "asignatura" or "asignaturas" or "materia" or "materias" or "nombre" or "curso" => Columna.Nombre,
            "creditos" or "credito" or "cr" or "cred" or "cred." or "uc" or "creditos academicos" => Columna.Creditos,
            "prerrequisito" or "prerrequisitos" or "pre-requisito" or "pre-requisitos" or "requisito" or "requisitos" or "pre requisito" or "pre requisitos" or "prelacion" or "prelaciones" => Columna.Prerrequisitos,
            "cuatrimestre" or "semestre" or "periodo" or "trimestre" or "nivel" or "ciclo" => Columna.Cuatrimestre,
            "electiva" => Columna.Electiva,
            _ => Columna.Ignorar,
        }).ToArray();
        // Es un encabezado si reconoce al menos el nombre y otra columna útil (el código, los créditos o los prerrequisitos).
        var reconocidas = mapa.Where(c => c != Columna.Ignorar).Distinct().ToList();
        if (!reconocidas.Contains(Columna.Nombre) || reconocidas.Count < 2) return false;
        // Sin delimitador claro no se sabe qué palabra es qué columna: las filas se leerán con el orden habitual.
        columnas = Dividir(linea) is null ? Array.Empty<Columna>() : mapa;
        return true;
    }

    /// <summary>Separa una línea en celdas si trae un delimitador claro (tabulador, «|», «;» o dos o más espacios); null si no.</summary>
    private static List<string>? Dividir(string linea)
    {
        string[] partes;
        if (linea.Contains('\t')) partes = linea.Split('\t');
        else if (linea.Contains('|')) partes = linea.Trim('|').Split('|');
        else if (linea.Count(c => c == ';') >= 2) partes = linea.Split(';');
        else if (Regex.IsMatch(linea, @"\s{2,}")) partes = Regex.Split(linea, @"\s{2,}");
        else return null;
        var celdas = partes.Select(p => p.Trim()).ToList();
        while (celdas.Count > 0 && celdas[^1].Length == 0) celdas.RemoveAt(celdas.Count - 1);
        return celdas;
    }

    // ── Una fila ──────────────────────────────────────────────────────────────────────────

    private static FilaEditable? LeerFila(string linea, Columna[]? mapa)
    {
        var celdas = Dividir(linea);
        return celdas is not null && celdas.Count >= 2 ? FilaDeCeldas(celdas, mapa) ?? FilaSinDelimitador(linea) : FilaSinDelimitador(linea);
    }

    private static FilaEditable? FilaDeCeldas(List<string> celdas, Columna[]? mapa)
    {
        var fila = new FilaEditable();

        if (mapa is not null && celdas.Count >= 2)
        {
            for (var i = 0; i < celdas.Count && i < mapa.Length; i++)
                switch (mapa[i])
                {
                    case Columna.Codigo: fila.Codigo = NormalizarCodigo(celdas[i]); break;
                    case Columna.Nombre: fila.Nombre = celdas[i]; break;
                    case Columna.Creditos: fila.Creditos = celdas[i].Trim(); break;
                    case Columna.Prerrequisitos: fila.Prerrequisitos = NormalizarPrerrequisitos(celdas[i]); break;
                    case Columna.Cuatrimestre: fila.Cuatrimestre = NumeroDePeriodo(celdas[i]) is { } p ? p.ToString(CultureInfo.InvariantCulture) : celdas[i].Trim(); break;
                    case Columna.Electiva: fila.Electiva = EsSi(celdas[i]); break;
                }
            // Con «;» como separador, una lista de prerrequisitos («E077; 67%») se parte en varias celdas: las que sobran del final son de los prerrequisitos.
            if (celdas.Count > mapa.Length && mapa.Length > 0 && mapa[^1] == Columna.Prerrequisitos)
                fila.Prerrequisitos = NormalizarPrerrequisitos(string.Join("; ", celdas.Skip(mapa.Length - 1)));
            if (fila.Codigo.Length == 0 && fila.Nombre.Length == 0) return null;
            // Una fila del encabezado repetido o sin código ni créditos (un subtítulo dentro de la tabla) no es una materia.
            if (fila.Codigo.Length == 0 && fila.Creditos.Length == 0) return null;
        }
        else
        {
            // Sin encabezado: código, asignatura, créditos, prerrequisitos (y, si hay, cuatrimestre), en ese orden.
            var iCodigo = celdas.FindIndex(c => EsCodigo(c));
            if (iCodigo < 0 || iCodigo + 1 >= celdas.Count) return null;
            fila.Codigo = NormalizarCodigo(celdas[iCodigo]);
            fila.Nombre = iCodigo + 1 < celdas.Count ? celdas[iCodigo + 1] : "";
            var resto = celdas.Skip(iCodigo + 2).ToList();
            var iCred = resto.FindIndex(c => int.TryParse(c, NumberStyles.None, CultureInfo.InvariantCulture, out var v) && v <= 20);
            if (iCred < 0) return null;
            fila.Creditos = resto[iCred];
            var despues = resto.Skip(iCred + 1).ToList();
            if (despues.Count > 0) fila.Prerrequisitos = NormalizarPrerrequisitos(despues[0]);
            if (despues.Count > 1 && int.TryParse(despues[1], NumberStyles.None, CultureInfo.InvariantCulture, out var cuat) && cuat is >= 1 and <= 24) fila.Cuatrimestre = cuat.ToString(CultureInfo.InvariantCulture);
        }

        if (fila.Codigo.Length == 0 && fila.Nombre.Length == 0) return null;
        if (fila.Nombre.Contains("electiva", StringComparison.OrdinalIgnoreCase)) fila.Electiva = true;
        return fila;
    }

    /// <summary>«ISO625 Programación Móvil 4 ISO515»: el código, luego el nombre hasta el primer número de créditos, luego los prerrequisitos.</summary>
    private static FilaEditable? FilaSinDelimitador(string linea)
    {
        var m = LineaSinDelimitador.Match(linea);
        if (!m.Success) return null;
        var tokens = m.Groups["resto"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 1; i < tokens.Length; i++)
        {
            if (!int.TryParse(tokens[i], NumberStyles.None, CultureInfo.InvariantCulture, out var creditos) || creditos > 20) continue;
            // El número de créditos es el que va al final o el que va seguido de prerrequisitos (un código, un porcentaje o un guion).
            var siguiente = i + 1 < tokens.Length ? tokens[i + 1].TrimEnd(';', ',', '.') : null;
            if (siguiente is not null && !EsCodigo(siguiente) && !siguiente.Contains('%') && siguiente is not ("-" or "–" or "—") && !EsNinguno(siguiente)) continue;
            return new FilaEditable
            {
                Codigo = NormalizarCodigo(m.Groups["cod"].Value),
                Nombre = string.Join(' ', tokens.Take(i)),
                Creditos = creditos.ToString(CultureInfo.InvariantCulture),
                Prerrequisitos = NormalizarPrerrequisitos(string.Join(' ', tokens.Skip(i + 1))),
                Electiva = string.Join(' ', tokens.Take(i)).Contains("electiva", StringComparison.OrdinalIgnoreCase),
            };
        }
        return null;
    }

    private static bool EsCodigo(string celda) => CodigoRx.IsMatch(celda.Trim()) || CodigoLetrasRx.IsMatch(celda.Trim());
    private static bool EsSi(string celda) => Sin(celda) is "si" or "sí" or "s" or "x" or "true" or "1" or "yes";
    private static bool EsNinguno(string celda) => Sin(celda) is "" or "-" or "–" or "—" or "ninguno" or "ninguna" or "n/a" or "na" or "no tiene" or "no aplica" or "ninguno.";

    public static string NormalizarCodigo(string codigo) => Regex.Replace(codigo.Trim(), @"[\s\-]", "").ToUpperInvariant();

    /// <summary>
    /// «ISO 515, ISO-520 y 67% de créditos» → «ISO515; ISO520; 67% créditos aprobados». Lo que no se reconoce se conserva tal cual
    /// para que la validación lo señale y la persona lo corrija.
    /// </summary>
    public static string NormalizarPrerrequisitos(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto) || EsNinguno(texto)) return "";
        var partes = new List<string>();
        foreach (var trozo in Regex.Split(texto, @"\s*(?:;|,|/|\by\b|\be\b|\+|&)\s*", RegexOptions.IgnoreCase))
        {
            var t = trozo.Trim().TrimEnd('.');
            if (t.Length == 0 || EsNinguno(t)) continue;
            if (PorcentajeRx.Match(t) is { Success: true } p)
            {
                // «ISO100 67%»: además del porcentaje puede venir un código; lo que sobra tras quitar las palabras de relleno («de los créditos»).
                var resto = Relleno.Replace(PorcentajeRx.Replace(t, " "), " ").Trim();
                foreach (var pieza in resto.Split(' ', StringSplitOptions.RemoveEmptyEntries)) partes.Add(NormalizarCodigo(pieza));
                partes.Add($"{p.Groups[1].Value}% créditos aprobados");
                continue;
            }
            var codigo = NormalizarCodigo(t);
            var pareceCodigo = CodigoValido.IsMatch(codigo) && (CodigoRx.IsMatch(t) || CodigoLetrasRx.IsMatch(codigo));
            partes.Add(pareceCodigo ? codigo : t);
        }
        return string.Join("; ", partes);
    }

    // ── Cuatrimestres ─────────────────────────────────────────────────────────────────────

    private static int? TituloDePeriodo(string linea)
    {
        if (linea.Length > 60 || linea.Contains('\t') || linea.Contains('|')) return null;
        if (TituloNumero.Match(linea) is { Success: true } a) return NumeroDePeriodo(a.Groups["n"].Value);
        if (TituloOrdinalNumerico.Match(linea) is { Success: true } b) return NumeroDePeriodo(b.Groups["n"].Value);
        if (TituloPalabra.Match(linea) is { Success: true } c) return Ordinales[c.Groups["w"].Value];
        if (TituloRomanoPrimero.Match(linea) is { Success: true } d) return NumeroDePeriodo(d.Groups["n"].Value);
        return null;
    }

    /// <summary>«3», «III» o «tercero» → 3 (entre 1 y 24); null si no es un número de período.</summary>
    public static int? NumeroDePeriodo(string texto)
    {
        var t = texto.Trim();
        if (int.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out var n)) return n is >= 1 and <= 24 ? n : null;
        if (Ordinales.TryGetValue(t, out var o)) return o;
        var romano = Romano(t.ToUpperInvariant());
        return romano is >= 1 and <= 24 ? romano : null;
    }

    private static int? Romano(string s)
    {
        if (s.Length == 0 || s.Any(c => "IVXL".IndexOf(c) < 0)) return null;
        var valores = new Dictionary<char, int> { ['I'] = 1, ['V'] = 5, ['X'] = 10, ['L'] = 50 };
        var total = 0;
        for (var i = 0; i < s.Length; i++)
        {
            var v = valores[s[i]];
            total += i + 1 < s.Length && valores[s[i + 1]] > v ? -v : v;
        }
        return total;
    }

    /// <summary>
    /// Sin cuatrimestres en el texto: una materia sin prerrequisitos va en el 1 y una con prerrequisitos, un cuatrimestre después de
    /// su prerrequisito más avanzado. Es solo una aproximación (por eso se avisa): la persona la corrige en la vista previa.
    /// </summary>
    private static void EstimarCuatrimestres(List<FilaEditable> filas)
    {
        var por = filas.Where(f => f.Codigo.Length > 0).GroupBy(f => f.Codigo).ToDictionary(g => g.Key, g => g.First());
        var nivel = new Dictionary<string, int>();

        int Nivel(string codigo, HashSet<string> visitando)
        {
            if (nivel.TryGetValue(codigo, out var ya)) return ya;
            if (!por.TryGetValue(codigo, out var fila) || !visitando.Add(codigo)) return 0;
            var previos = fila.Prerrequisitos.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(p => !p.Contains('%') && por.ContainsKey(p)).Select(p => Nivel(p, visitando)).ToList();
            visitando.Remove(codigo);
            return nivel[codigo] = 1 + (previos.Count == 0 ? 0 : previos.Max());
        }

        foreach (var f in filas)
            f.Cuatrimestre = Math.Min(24, Math.Max(1, f.Codigo.Length > 0 ? Nivel(f.Codigo, new HashSet<string>()) : 1)).ToString(CultureInfo.InvariantCulture);
    }

    // ── Construir el pénsum con las filas ─────────────────────────────────────────────────

    /// <summary>
    /// Arma el pénsum con las filas (sin las marcadas para quitar) y lo valida con las mismas reglas que un pénsum del catálogo.
    /// Los problemas de una fila (créditos que no son un número…) se devuelven con esa fila; los del conjunto (códigos repetidos,
    /// prerrequisitos que no existen…) se relacionan con su fila cuando se puede.
    /// </summary>
    public static ResultadoConstruccion Construir(IReadOnlyList<FilaEditable> filas, MetadatosPensum meta)
    {
        var errores = new Dictionary<FilaEditable, List<string>>();
        var generales = new List<string>();
        void Fila(FilaEditable f, string e) { if (!errores.TryGetValue(f, out var l)) errores[f] = l = new(); l.Add(e); }

        var activas = filas.Where(f => !f.Quitar && !VaciaPorCompleto(f)).ToList();
        if (activas.Count == 0) return new(new ResultadoPensumJson(null, new() { "El pénsum no tiene ninguna materia." }, new()), errores, new() { "El pénsum no tiene ninguna materia." }, 0, 0);
        if (activas.Count > MaxFilas) generales.Add($"Son demasiadas materias (máximo {MaxFilas}).");

        var materias = new List<MateriaDef>();
        var origen = new List<FilaEditable>();   // la fila de cada materia, para relacionar los errores de la validación
        foreach (var f in activas)
        {
            var ok = true;
            var codigo = NormalizarCodigo(f.Codigo);
            if (codigo.Length == 0) { Fila(f, "Falta el código."); ok = false; }
            if (string.IsNullOrWhiteSpace(f.Nombre)) { Fila(f, "Falta el nombre de la asignatura."); ok = false; }
            if (!int.TryParse(f.Creditos?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var creditos)) { Fila(f, $"Los créditos «{f.Creditos}» no son un número entero."); ok = false; }
            if (!int.TryParse(f.Cuatrimestre?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var cuat)) { Fila(f, f.Cuatrimestre?.Trim().Length > 0 ? $"El cuatrimestre «{f.Cuatrimestre}» no es un número." : "Falta el cuatrimestre."); ok = false; }
            if (!ok) continue;

            var prer = (f.Prerrequisitos ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            materias.Add(new MateriaDef(codigo, f.Nombre.Trim(), creditos, cuat, prer, f.Electiva));
            origen.Add(f);
        }
        if (errores.Count > 0 || materias.Count == 0)
            return new(new ResultadoPensumJson(null, new() { "Hay filas con problemas." }, new()), errores, generales, 0, 0);

        var total = materias.Sum(m => m.Creditos);
        var cuatrimestres = materias.Max(m => m.Cuatrimestre);
        var definicion = new PensumDefinicion(PensumDefinicion.FormatoActual, meta.Universidad, meta.Carrera, meta.NombreCarrera, meta.Version,
            total, cuatrimestres, materias, new(), new(), new(), new());
        var resultado = PensumJson.Leer(PensumJson.Escribir(definicion));

        foreach (var e in resultado.Errores)
        {
            var fila = RelacionarConFila(e, materias, origen);
            if (fila is not null) Fila(fila, LimpiarMensaje(e));
            else generales.Add(e);
        }
        return new(resultado, errores, generales, total, cuatrimestres);
    }

    private static bool VaciaPorCompleto(FilaEditable f) =>
        string.IsNullOrWhiteSpace(f.Codigo) && string.IsNullOrWhiteSpace(f.Nombre) && string.IsNullOrWhiteSpace(f.Creditos) &&
        string.IsNullOrWhiteSpace(f.Cuatrimestre) && string.IsNullOrWhiteSpace(f.Prerrequisitos);

    /// <summary>Los errores de la validación empiezan con «materias[3]…» o «ISO625: …»: se buscan en la fila correspondiente.</summary>
    private static FilaEditable? RelacionarConFila(string error, List<MateriaDef> materias, List<FilaEditable> origen)
    {
        var porIndice = Regex.Match(error, @"^materias\[(\d+)\]");
        if (porIndice.Success && int.Parse(porIndice.Groups[1].Value, CultureInfo.InvariantCulture) is var i && i < origen.Count) return origen[i];

        var porCodigo = Regex.Match(error, @"^(?:materias: el código )?([A-Z0-9]{2,12})\b");
        if (porCodigo.Success)
        {
            var indice = materias.FindIndex(m => m.Codigo == porCodigo.Groups[1].Value);
            if (indice >= 0) return origen[indice];
        }
        return null;
    }

    private static string LimpiarMensaje(string error) => Regex.Replace(error, @"^materias\[\d+\]( \([A-Z0-9]+\))?[.:]\s*", "").Trim();

    // ── Identificadores ───────────────────────────────────────────────────────────────────

    /// <summary>«Ingeniería de Software» → «ingenieria-de-software» (minúsculas, sin acentos, con guiones). Vacío si no queda nada.</summary>
    public static string Slug(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return "";
        var sb = new StringBuilder();
        foreach (var c in texto.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            var l = char.ToLowerInvariant(c);
            sb.Append(l is >= 'a' and <= 'z' or >= '0' and <= '9' ? l : '-');
        }
        return Regex.Replace(sb.ToString(), "-{2,}", "-").Trim('-');
    }
}
