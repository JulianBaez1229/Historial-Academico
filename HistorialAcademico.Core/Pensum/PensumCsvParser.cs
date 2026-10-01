using System.Globalization;
using System.Text;
using HistorialAcademico.Core.Entities;

namespace HistorialAcademico.Core.Pensum;

public record ResultadoCsvPensum(List<MateriaPensum> Materias, List<string> Errores, List<string> Advertencias)
{
    public bool EsValido => Errores.Count == 0 && Materias.Count > 0;
}

/// <summary>
/// Lee pensum_iso_unapec.csv. Columnas: codigo, nombre, creditos, cuatrimestre, prerrequisitos, es_electiva.
/// Si hay errores no se debe guardar nada; las advertencias no bloquean.
/// </summary>
public static class PensumCsvParser
{
    private static readonly string[] Columnas = { "codigo", "nombre", "creditos", "cuatrimestre", "prerrequisitos", "es_electiva" };

    public static ResultadoCsvPensum Parse(string csv)
    {
        var materias = new List<MateriaPensum>();
        var errores = new List<string>();
        var advertencias = new List<string>();

        var lineas = csv.TrimStart('﻿').Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        var primera = lineas.FindIndex(l => l.Trim().Length > 0);
        if (primera < 0) return new(materias, new() { "El archivo está vacío." }, advertencias);

        var encabezado = DividirLinea(lineas[primera]).Select(c => c.Trim().ToLowerInvariant()).ToArray();
        if (!encabezado.SequenceEqual(Columnas))
            return new(materias, new() { $"Encabezado inesperado. Se esperaba: {string.Join(", ", Columnas)}." }, advertencias);

        var vistos = new HashSet<string>();
        for (var i = primera + 1; i < lineas.Count; i++)
        {
            if (lineas[i].Trim().Length == 0) continue;
            var n = i + 1;
            var c = DividirLinea(lineas[i]);
            if (c.Count != Columnas.Length) { errores.Add($"Línea {n}: se esperaban {Columnas.Length} columnas y hay {c.Count}."); continue; }

            var codigo = c[0].Trim().ToUpperInvariant();
            var nombre = c[1].Trim();
            if (codigo.Length == 0) { errores.Add($"Línea {n}: falta el código."); continue; }
            if (nombre.Length == 0) { errores.Add($"Línea {n} ({codigo}): falta el nombre."); continue; }
            if (!vistos.Add(codigo)) { errores.Add($"Línea {n}: el código {codigo} está repetido."); continue; }
            if (!int.TryParse(c[2].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var creditos))
            { errores.Add($"Línea {n} ({codigo}): créditos inválidos \"{c[2]}\"."); continue; }
            if (!int.TryParse(c[3].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var cuat) || cuat is < 1 or > 12)
            { errores.Add($"Línea {n} ({codigo}): el cuatrimestre debe estar entre 1 y 12 (\"{c[3]}\")."); continue; }
            if (!TryBool(c[5], out var electiva))
            { errores.Add($"Línea {n} ({codigo}): es_electiva debe ser true o false (\"{c[5]}\")."); continue; }
            if (!PrerrequisitoParser.TryParse(c[4], out _, out var errPrer))
            { errores.Add($"Línea {n} ({codigo}): {errPrer}"); continue; }

            materias.Add(new MateriaPensum
            {
                Codigo = codigo, Nombre = nombre, Creditos = creditos, Cuatrimestre = cuat,
                Prerrequisitos = string.IsNullOrWhiteSpace(c[4]) ? null : c[4].Trim(), EsElectiva = electiva,
            });
        }

        // Un prerrequisito que apunta a una materia que no existe dejaría la materia bloqueada para siempre.
        var codigos = materias.Select(m => m.Codigo).ToHashSet();
        foreach (var m in materias)
            foreach (var r in PrerrequisitoParser.Parse(m.Prerrequisitos).Where(r => r.Materia is not null && !codigos.Contains(r.Materia!)))
                advertencias.Add($"{m.Codigo}: su prerrequisito {r.Materia} no existe en el pénsum.");

        return new(materias, errores, advertencias);
    }

    private static bool TryBool(string s, out bool valor)
    {
        switch (s.Trim().ToLowerInvariant())
        {
            case "true": case "1": case "si": case "sí": valor = true; return true;
            case "false": case "0": case "no": case "": valor = false; return true;
            default: valor = false; return false;
        }
    }

    /// <summary>Divide una línea CSV respetando comillas dobles ("a,b" y "" como comilla literal).</summary>
    internal static List<string> DividirLinea(string linea)
    {
        var campos = new List<string>();
        var actual = new StringBuilder();
        var entreComillas = false;
        for (var i = 0; i < linea.Length; i++)
        {
            var ch = linea[i];
            if (entreComillas)
            {
                if (ch == '"' && i + 1 < linea.Length && linea[i + 1] == '"') { actual.Append('"'); i++; }
                else if (ch == '"') entreComillas = false;
                else actual.Append(ch);
            }
            else if (ch == '"') entreComillas = true;
            else if (ch == ',') { campos.Add(actual.ToString()); actual.Clear(); }
            else actual.Append(ch);
        }
        campos.Add(actual.ToString());
        return campos;
    }
}
