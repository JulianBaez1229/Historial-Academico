using System.Text.RegularExpressions;
using HistorialAcademico.Core.Entities;
using HistorialAcademico.Core.Indice;
using HistorialAcademico.Core.Planificacion;
using HistorialAcademico.Core.Universidad;

namespace HistorialAcademico.Core.Manual;

/// <summary>Qué pasó con una materia registrada a mano al mezclarla con el histórico.</summary>
public enum EstadoManual
{
    /// <summary>Cuenta en tus materias tomadas (o en curso), en el índice y en el avance del pénsum.</summary>
    Aplicada,
    /// <summary>Banner ya trae ese período: se usa lo de Banner y esta queda guardada sin contar.</summary>
    IgnoradaPorBanner,
    /// <summary>El código no está en el pénsum actual (cambiaste de carrera): queda guardada sin contar.</summary>
    SinPensum,
}

public record ResultadoFusion(List<Periodo> Periodos, List<CursoEnProgreso> Cursos, IReadOnlyDictionary<int, EstadoManual> Estados);

/// <summary>
/// Mezcla el histórico de Banner con las materias registradas a mano. La regla es simple: <b>Banner manda en los períodos que trae</b>;
/// lo escrito a mano solo cubre los períodos que Banner no tiene. Así conectar Banner después nunca duplica una materia.
/// Una materia con calificación forma parte de un período cursado; sin calificación es un curso en progreso.
/// </summary>
public static class HistoricoManual
{
    /// <summary>
    /// Devuelve los períodos y cursos en progreso ya mezclados, en orden cronológico. Sin materias manuales devuelve lo mismo que recibió.
    /// Cuando hay períodos manuales, <b>cambia el <see cref="Periodo.Orden"/></b> de los períodos recibidos para dejarlos en orden cronológico.
    /// </summary>
    public static ResultadoFusion Fusionar(
        IReadOnlyList<Periodo> banner, IReadOnlyList<CursoEnProgreso> cursosBanner, IReadOnlyList<MateriaManual> manuales,
        IReadOnlyList<MateriaPensum> pensum, ReglasUniversidad reglas)
    {
        var estados = new Dictionary<int, EstadoManual>();
        var cursos = cursosBanner.ToList();
        if (manuales.Count == 0) return new ResultadoFusion(banner.ToList(), cursos, estados);

        var cubiertos = new HashSet<string>(banner.Select(p => p.Nombre).Concat(cursosBanner.Select(c => c.Periodo)), StringComparer.OrdinalIgnoreCase);
        var delPensum = new Dictionary<string, MateriaPensum>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in pensum) delPensum.TryAdd(m.Codigo, m);

        var nuevos = new Dictionary<string, Periodo>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in manuales.OrderBy(x => x.Id))
        {
            if (cubiertos.Contains(m.Periodo)) { estados[m.Id] = EstadoManual.IgnoradaPorBanner; continue; }
            if (!delPensum.TryGetValue(m.Codigo, out var materia)) { estados[m.Id] = EstadoManual.SinPensum; continue; }
            estados[m.Id] = EstadoManual.Aplicada;

            var (parteMateria, parteCurso) = SepararCodigo(materia.Codigo);
            if (string.IsNullOrWhiteSpace(m.Calificacion))
            {
                cursos.Add(new CursoEnProgreso
                {
                    Id = -m.Id, Periodo = m.Periodo, Codigo = materia.Codigo, Materia = parteMateria, Curso = parteCurso,
                    Titulo = materia.Nombre, HorasCredito = materia.Creditos, Nivel = "Grado",
                });
                continue;
            }

            if (!nuevos.TryGetValue(m.Periodo, out var periodo))
                nuevos[m.Periodo] = periodo = new Periodo { Id = -(nuevos.Count + 1), Nombre = m.Periodo, Nivel = "Grado" };
            var puntos = reglas.Escala.PuntosPorLetra(m.Calificacion);
            periodo.Materias.Add(new MateriaCursada
            {
                Id = -m.Id, PeriodoId = periodo.Id, Periodo = periodo, Codigo = materia.Codigo, Materia = parteMateria, Curso = parteCurso,
                Titulo = materia.Nombre, Calificacion = m.Calificacion, HorasCredito = materia.Creditos,
                PuntosCalidad = (puntos ?? 0) * materia.Creditos, Nivel = "Grado",
            });
        }

        var todos = banner.Concat(nuevos.Values).ToList();
        if (nuevos.Count == 0) return new ResultadoFusion(banner.ToList(), cursos, estados);

        Ordenar(todos, banner, reglas);
        Acumular(todos, nuevos.Values.ToHashSet(), reglas);
        return new ResultadoFusion(todos, cursos, estados);
    }

    /// <summary>Cronológico según los períodos de la universidad; si algún nombre no se entiende, Banner queda como venía y lo manual va después.</summary>
    private static void Ordenar(List<Periodo> todos, IReadOnlyList<Periodo> banner, ReglasUniversidad reglas)
    {
        PeriodoAcademico? Leer(Periodo p) => PeriodoAcademico.TryParse(p.Nombre, reglas.Periodos, out var pa) ? pa : null;

        var bannerLegible = banner.All(p => Leer(p) is not null);
        List<Periodo> orden;
        if (bannerLegible)
            orden = todos.OrderBy(p => Leer(p)!.Value).ThenBy(p => p.Id < 0 ? 1 : 0).ToList();
        else
            orden = banner.OrderBy(p => p.Orden).Concat(todos.Except(banner).OrderBy(p => Leer(p) ?? default)).ToList();

        todos.Clear();
        todos.AddRange(orden);
        for (var i = 0; i < todos.Count; i++) todos[i].Orden = i + 1;
    }

    /// <summary>Los totales de un período manual salen de sus materias; el acumulado sigue al del período anterior (el de Banner, tal como lo publica).</summary>
    private static void Acumular(List<Periodo> ordenados, HashSet<Periodo> manuales, ReglasUniversidad reglas)
    {
        decimal intentadas = 0, aprobadas = 0, pga = 0, puntos = 0;
        foreach (var p in ordenados)
        {
            if (!manuales.Contains(p))
            {
                intentadas = p.AcumHorasIntentadas; aprobadas = p.AcumHorasAprobadas; pga = p.AcumHorasPga; puntos = p.AcumPuntosCalidad;
                continue;
            }
            var t = CalculadoraIndice.Totales(p.Materias, reglas.Escala);
            p.HorasIntentadas = t.HorasIntentadas; p.HorasAprobadas = t.HorasAprobadas; p.HorasGanadas = t.HorasAprobadas;
            p.HorasPga = t.HorasPga; p.PuntosCalidad = t.PuntosCalidad; p.Pga = t.Indice;

            intentadas += t.HorasIntentadas; aprobadas += t.HorasAprobadas; pga += t.HorasPga; puntos += t.PuntosCalidad;
            p.AcumHorasIntentadas = intentadas; p.AcumHorasAprobadas = aprobadas; p.AcumHorasGanadas = aprobadas; p.AcumHorasPga = pga;
            p.AcumPuntosCalidad = puntos;
            p.AcumPga = pga == 0 ? 0 : reglas.Escala.Redondear(puntos / pga);
        }
    }

    /// <summary>«ISO700» → («ISO», «700»); un código sin dígitos queda entero como materia.</summary>
    public static (string Materia, string Curso) SepararCodigo(string codigo)
    {
        var m = Regex.Match(codigo, @"^([A-Za-z]+)(\d.*)$");
        return m.Success ? (m.Groups[1].Value, m.Groups[2].Value) : (codigo, "");
    }
}

/// <summary>Una materia registrada a mano, ya revisada y con el formato canónico (código como en el pénsum, período y letra de la escala).</summary>
public record FilaManual(string Codigo, string Periodo, string Calificacion);

/// <summary>Revisa lo que la persona escribe al registrar una materia a mano.</summary>
public static class ValidadorManual
{
    public const int AnioMinimo = 1950, AnioMaximo = 2100;

    public static (FilaManual? Fila, string? Error) Normalizar(
        string? codigo, string? periodo, string? calificacion, IReadOnlyList<MateriaPensum> pensum, ReglasUniversidad reglas)
    {
        var c = codigo?.Trim() ?? "";
        if (c.Length == 0) return (null, "Escribe el código de la materia.");
        if (pensum.Count == 0) return (null, "Primero elige tu carrera: todavía no hay un pénsum con el que comparar el código.");
        var materia = pensum.FirstOrDefault(m => string.Equals(m.Codigo, c, StringComparison.OrdinalIgnoreCase));
        if (materia is null) return (null, $"«{c}» no está en tu pénsum. Revisa el código (por ejemplo, {pensum[0].Codigo}).");

        var texto = Regex.Replace(periodo?.Trim() ?? "", @"\s+", " ");
        var ejemplo = $"{reglas.Periodos.Nombre(0)} {DateTime.Today.Year}";
        if (texto.Length == 0) return (null, $"Escribe el período, por ejemplo «{ejemplo}».");
        if (!PeriodoAcademico.TryParse(texto, reglas.Periodos, out var p) || p.Anio < AnioMinimo || p.Anio > AnioMaximo)
            return (null, $"El período «{texto}» no se entiende. Escríbelo así: «{ejemplo}» (los períodos de {reglas.Nombre} son {string.Join(", ", reglas.Periodos.Periodos.Select(x => x.Nombre))}).");

        var nota = calificacion?.Trim() ?? "";
        var letra = "";
        if (nota.Length > 0)
        {
            var encontrada = reglas.Escala.Buscar(nota);
            if (encontrada is null)
                return (null, $"La calificación «{nota}» no existe en la escala de {reglas.Nombre} ({string.Join(", ", reglas.Escala.Letras.Select(l => l.Letra))}). Déjala vacía si todavía cursas la materia.");
            letra = encontrada.Letra;
        }
        return (new FilaManual(materia.Codigo, p.Nombre, letra), null);
    }
}

public record FilaCsv(int Linea, string Codigo, string Periodo, string Calificacion);
public record ResultadoCsv(List<FilaCsv> Filas, List<string> Errores);

/// <summary>Lee el CSV con las materias tomadas (código, período, calificación) y arma la plantilla descargable.</summary>
public static class CsvManual
{
    public const int MaxFilas = 2000;

    /// <summary>
    /// Acepta coma, punto y coma o tabulador (Excel en español guarda con punto y coma), comillas, encabezado opcional (en cualquier orden
    /// de columnas), líneas vacías y comentarios que empiezan con #.
    /// </summary>
    public static ResultadoCsv Leer(string? texto)
    {
        var filas = new List<FilaCsv>();
        var errores = new List<string>();
        var lineas = (texto ?? "").TrimStart('﻿').Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        char? separador = null;
        int colCodigo = 0, colPeriodo = 1, colNota = 2;
        var primera = true;
        for (var i = 0; i < lineas.Length; i++)
        {
            var linea = lineas[i];
            if (string.IsNullOrWhiteSpace(linea) || linea.TrimStart().StartsWith('#')) continue;
            separador ??= Detectar(linea);
            var campos = Partir(linea, separador.Value);

            if (primera)
            {
                primera = false;
                var nombres = campos.Select(Clave).ToList();
                if (nombres.Any(n => n is "codigo" or "materia" or "asignatura" or "periodo" or "calificacion" or "nota"))
                {
                    colCodigo = nombres.FindIndex(n => n is "codigo" or "materia" or "asignatura");
                    colPeriodo = nombres.FindIndex(n => n is "periodo");
                    colNota = nombres.FindIndex(n => n is "calificacion" or "nota");
                    if (colCodigo < 0 || colPeriodo < 0)
                    {
                        errores.Add("Línea " + (i + 1) + ": el encabezado debe tener las columnas «codigo» y «periodo» (y «calificacion»).");
                        return new ResultadoCsv(filas, errores);
                    }
                    continue;
                }
            }

            if (filas.Count >= MaxFilas) { errores.Add($"El archivo tiene más de {MaxFilas} filas; divídelo en partes."); break; }
            if (campos.Count <= Math.Max(colCodigo, colPeriodo)) { errores.Add($"Línea {i + 1}: faltan columnas (se necesita al menos el código y el período)."); continue; }
            filas.Add(new FilaCsv(i + 1, campos[colCodigo].Trim(), campos[colPeriodo].Trim(), colNota >= 0 && colNota < campos.Count ? campos[colNota].Trim() : ""));
        }

        if (filas.Count == 0 && errores.Count == 0) errores.Add("El archivo no tiene ninguna materia.");
        return new ResultadoCsv(filas, errores);
    }

    private static string Clave(string s) =>
        s.Trim().ToLowerInvariant().Replace("á", "a").Replace("é", "e").Replace("í", "i").Replace("ó", "o").Replace("ú", "u");

    private static char Detectar(string linea)
    {
        var candidatos = new[] { ',', ';', '\t' };
        return candidatos.OrderByDescending(c => linea.Count(x => x == c)).First();
    }

    /// <summary>Parte una línea respetando comillas dobles («a,b» y "" como comilla).</summary>
    private static List<string> Partir(string linea, char separador)
    {
        var campos = new List<string>();
        var actual = new System.Text.StringBuilder();
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
            else if (ch == separador) { campos.Add(actual.ToString()); actual.Clear(); }
            else actual.Append(ch);
        }
        campos.Add(actual.ToString());
        return campos;
    }

    /// <summary>La plantilla: el encabezado y unas filas de ejemplo con materias del pénsum de la persona.</summary>
    public static string Plantilla(IEnumerable<string> codigosDeEjemplo, ReglasUniversidad reglas, int anio)
    {
        var codigos = codigosDeEjemplo.Take(3).ToList();
        if (codigos.Count == 0) codigos = new() { "ABC101", "ABC102", "ABC201" };
        var letras = reglas.Escala.Letras.Where(l => l.Aprueba && l.CuentaParaIndice).Select(l => l.Letra).DefaultIfEmpty("A").ToList();
        var p0 = new PeriodoAcademico(anio - 1, 0, reglas.Periodos);
        var periodos = new[] { p0.Nombre, p0.Nombre, p0.Siguiente().Nombre };

        var sb = new System.Text.StringBuilder();
        sb.Append("codigo,periodo,calificacion\r\n");
        for (var i = 0; i < codigos.Count; i++)
            sb.Append($"{codigos[i]},{periodos[i]},{letras[i % letras.Count]}\r\n");
        sb.Append($"# Una materia por línea. El código es el de tu pénsum y el período se escribe como «{p0.Nombre}».\r\n");
        sb.Append($"# La calificación es una de: {string.Join(", ", reglas.Escala.Letras.Select(l => l.Letra))}. Déjala vacía si todavía cursas la materia.\r\n");
        sb.Append("# Borra las filas de ejemplo antes de importar.\r\n");
        return sb.ToString();
    }
}
