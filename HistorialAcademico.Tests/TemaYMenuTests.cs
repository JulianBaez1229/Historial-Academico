using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Xunit;

namespace HistorialAcademico.Tests;

/// <summary>Localiza los archivos estáticos de la aplicación y lee los tokens de color de tema.css.</summary>
public static class Estaticos
{
    public static string Raiz
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && dir.GetFiles("*.sln").Length == 0) dir = dir.Parent;
            return Path.Combine(dir!.FullName, "HistorialAcademico.Web", "wwwroot");
        }
    }

    public static string Leer(string relativo) => File.ReadAllText(Path.Combine(Raiz, relativo));

    /// <summary>
    /// Todos los tokens de color: nombre (sin "--") → hex. Un token puede ser un hex de 6 dígitos o un alias
    /// <c>var(--otro)</c> a otro token, que se resuelve.
    /// </summary>
    public static Dictionary<string, string> Tokens(string tema = "claro")
    {
        var css = Leer("css/tema.css");
        var crudos = LeerBloque(css, ":root {");
        if (tema == "oscuro")
            foreach (var (nombre, valor) in LeerBloque(css, ":root[data-tema=\"oscuro\"] {")) crudos[nombre] = valor;   // el oscuro pisa al claro

        string Resolver(string valor, int profundidad = 0)
        {
            if (valor.StartsWith('#')) return valor.ToUpperInvariant();
            if (profundidad > 5) throw new InvalidOperationException("Alias circular en tema.css");
            var otro = valor[6..^1];   // var(--nombre) → nombre
            return Resolver(crudos[otro], profundidad + 1);
        }
        return crudos.ToDictionary(k => k.Key, k => Resolver(k.Value));
    }

    /// <summary>Los nombres de todos los tokens (de cualquier tipo) que redefine el tema oscuro.</summary>
    public static IEnumerable<string> NombresDelTemaOscuro()
    {
        var css = Leer("css/tema.css");
        var bloque = css[css.IndexOf(":root[data-tema=\"oscuro\"] {", StringComparison.Ordinal)..];
        bloque = bloque[..bloque.IndexOf("\n}", StringComparison.Ordinal)];
        return Regex.Matches(bloque, @"--([a-z0-9-]+):").Select(m => m.Groups[1].Value);
    }

    /// <summary>Tokens de color (hex o alias var) de un bloque CSS, desde su selector hasta la llave que lo cierra.</summary>
    private static Dictionary<string, string> LeerBloque(string css, string selector)
    {
        var desde = css.IndexOf(selector, StringComparison.Ordinal);
        var bloque = css[desde..];
        bloque = bloque[..bloque.IndexOf("\n}", StringComparison.Ordinal)];
        return Regex.Matches(bloque, @"--([a-z0-9-]+):\s*(#[0-9A-Fa-f]{6}|var\(--[a-z0-9-]+\))\s*;")
            .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);
    }

    /// <summary>Contraste WCAG 2.1 entre dos colores (1:1 a 21:1).</summary>
    public static double Contraste(string hexA, string hexB)
    {
        var a = Luminancia(hexA);
        var b = Luminancia(hexB);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    private static double Luminancia(string hex)
    {
        double Canal(int desde)
        {
            var c = int.Parse(hex.AsSpan(desde, 2), NumberStyles.HexNumber) / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Canal(1) + 0.7152 * Canal(3) + 0.0722 * Canal(5);
    }
}

/// <summary>Pruebas de los tokens de color. Cada una se ejecuta en el tema claro Y en el oscuro.</summary>
public class TemaContrasteTests
{
    private static Dictionary<string, string> T(string tema) => Estaticos.Tokens(tema);

    public static IEnumerable<object[]> Temas() => new[] { new object[] { "claro" }, new object[] { "oscuro" } };

    [Fact]
    public void LaPaletaBaseEsLaDocumentadaEnElTemaClaro()
    {
        var t = T("claro");
        Assert.Equal("#1B2A7E", t["color-primario"]);   // azul marino
        Assert.Equal("#C4A23A", t["color-acento"]);      // dorado
        Assert.Equal("#D0202E", t["color-peligro"]);     // rojo
        Assert.Equal("#3A6EA5", t["color-info"]);        // azul acero
        Assert.Equal("#FFFFFF", t["color-superficie"]);  // blanco
    }

    [Fact]
    public void ElDoradoDeLaMarcaSeConservaEnElTemaOscuro()
    {
        Assert.Equal("#C4A23A", T("oscuro")["color-acento"]);
        Assert.Equal("#C4A23A", T("oscuro")["color-menu-activo-fondo"]);
    }

    [Theory]
    [MemberData(nameof(Temas))]
    public void ElTemaDefineTodosLosTokensNecesarios(string tema)
    {
        var t = T(tema);
        foreach (var nombre in new[]
        {
            "color-primario", "color-primario-oscuro", "color-primario-suave", "color-acento", "color-acento-oscuro",
            "color-fondo", "color-superficie", "color-superficie-2", "color-texto", "color-texto-suave", "color-borde", "color-codigo",
            "color-exito", "color-alerta", "color-alerta-texto", "color-peligro", "color-info",
            "color-sobre-primario", "color-sobre-acento",
            "color-menu-fondo", "color-menu-texto", "color-menu-suave", "color-menu-activo-fondo", "color-menu-activo-texto",
            "estado-aprobada-fondo", "estado-exenta-fondo", "estado-encurso-fondo", "estado-disponible-fondo", "estado-bloqueada-fondo",
            "aviso-exito-fondo", "aviso-info-fondo", "aviso-peligro-fondo", "aviso-alerta-fondo", "aviso-neutro-fondo",
        })
            Assert.True(t.ContainsKey(nombre), $"Falta el token --{nombre} en el tema {tema}");
    }

    [Fact]
    public void ElTemaOscuroSoloRedefineTokensQueExistenEnElClaro()
    {
        var claro = T("claro");
        foreach (var nombre in Estaticos.NombresDelTemaOscuro())
            Assert.True(claro.ContainsKey(nombre) || nombre.StartsWith("bs-") || nombre == "flecha-select" || nombre == "filtro-cerrar" ||
                        nombre is "sombra-suave" or "sombra-elevada" or "color-velo" or "color-foco-sombra" or "color-fila-hover" or "color-menu-hover",
                $"--{nombre} está en el tema oscuro pero no en el claro (¿error de nombre?)");
    }

    [Fact]
    public void ElTemaOscuroRedefineLosTokensDeSuperficieTextoYAcento()
    {
        // Si un token de estos se olvidara, el tema oscuro heredaría un color claro y quedaría ilegible.
        var oscuro = Estaticos.NombresDelTemaOscuro().ToHashSet();
        foreach (var nombre in new[]
        {
            "color-primario", "color-fondo", "color-superficie", "color-superficie-2", "color-texto", "color-texto-suave", "color-borde",
            "color-sobre-primario", "color-sobre-acento", "color-peligro", "color-info", "color-exito", "color-alerta-texto",
            "color-menu-fondo", "color-menu-texto",
            "estado-aprobada-fondo", "estado-aprobada-texto", "estado-exenta-fondo", "estado-encurso-fondo", "estado-disponible-fondo", "estado-bloqueada-fondo",
            "aviso-exito-fondo", "aviso-info-fondo", "aviso-peligro-fondo", "aviso-alerta-fondo", "aviso-neutro-fondo",
            "bs-primary-rgb", "bs-success-rgb", "bs-danger-rgb", "bs-info-rgb", "bs-secondary-rgb",
        })
            Assert.Contains(nombre, oscuro);
    }

    public static IEnumerable<object[]> ParesTextoFondo()
    {
        var pares = new List<(string Texto, string Fondo)>
        {
            ("color-texto", "color-superficie"), ("color-texto", "color-fondo"), ("color-texto", "color-superficie-2"),
            ("color-texto-suave", "color-superficie"), ("color-texto-suave", "color-fondo"), ("color-texto-suave", "color-superficie-2"),
            ("color-primario", "color-superficie"), ("color-primario", "color-fondo"), ("color-primario", "color-primario-suave"),
            ("color-primario", "color-superficie-2"),
            ("color-sobre-primario", "color-primario"), ("color-sobre-primario", "color-primario-oscuro"),
            ("color-sobre-primario", "color-peligro"), ("color-sobre-primario", "color-info"), ("color-sobre-primario", "color-exito"),
            ("color-sobre-acento", "color-acento"),
            ("color-alerta-texto", "color-superficie"), ("color-alerta-texto", "color-fondo"),
            ("color-peligro", "color-superficie"), ("color-peligro", "color-fondo"),
            ("color-exito", "color-superficie"), ("color-exito", "color-fondo"),
            ("color-info", "color-superficie"),
            ("color-codigo", "color-superficie"), ("color-codigo", "color-fondo"),
            ("color-menu-texto", "color-menu-fondo"), ("color-menu-suave", "color-menu-fondo"),
            ("color-menu-activo-texto", "color-menu-activo-fondo"),
            ("mapa-requisito-texto", "mapa-requisito-fondo"), ("mapa-desbloquea-texto", "mapa-desbloquea-fondo"),
        };
        foreach (var e in new[] { "aprobada", "exenta", "encurso", "disponible", "bloqueada" })
            pares.Add(($"estado-{e}-texto", $"estado-{e}-fondo"));
        foreach (var a in new[] { "exito", "info", "peligro", "alerta", "neutro" })
            pares.Add(($"aviso-{a}-texto", $"aviso-{a}-fondo"));

        foreach (var tema in new[] { "claro", "oscuro" })
            foreach (var (texto, fondo) in pares)
                yield return new object[] { tema, texto, fondo };
    }

    [Theory]
    [MemberData(nameof(ParesTextoFondo))]
    public void CadaParTextoFondoCumpleWcagAA(string tema, string texto, string fondo)
    {
        var t = T(tema);
        var ratio = Estaticos.Contraste(t[texto], t[fondo]);
        Assert.True(ratio >= 4.5, $"[{tema}] --{texto} ({t[texto]}) sobre --{fondo} ({t[fondo]}): {ratio:0.00}:1, mínimo 4.5:1");
    }

    public static IEnumerable<object[]> ElementosGraficos() =>
        from tema in new[] { "claro", "oscuro" }
        from token in new[] { "grafico-aprobada", "grafico-encurso", "grafico-faltante", "grafico-periodo", "grafico-acumulado" }
        select new object[] { tema, token };

    [Theory]
    [MemberData(nameof(ElementosGraficos))]
    public void LosElementosDeLosGraficosSeDistinguenDelFondo(string tema, string token)
    {
        // WCAG 1.4.11: los componentes gráficos necesitan al menos 3:1 frente al fondo de la tarjeta.
        var t = T(tema);
        var ratio = Estaticos.Contraste(t[token], t["color-superficie"]);
        Assert.True(ratio >= 3.0, $"[{tema}] --{token} ({t[token]}) sobre la superficie: {ratio:0.00}:1, mínimo 3:1");
    }

    [Theory]
    [MemberData(nameof(Temas))]
    public void LosBordesDeLosEstadosSeDistinguenDelFondoDeLaPagina(string tema)
    {
        var t = T(tema);
        foreach (var e in new[] { "aprobada", "exenta", "encurso", "disponible", "bloqueada" })
            Assert.True(Estaticos.Contraste(t[$"estado-{e}-borde"], t["color-fondo"]) >= 3.0, $"[{tema}] borde de {e}");
    }

    [Fact]
    public void ElDoradoNoSeUsaComoTextoSobreFondoClaro()
    {
        // Documenta por qué existe --color-alerta-texto: el dorado de la marca no alcanza el contraste sobre blanco.
        var t = T("claro");
        Assert.True(Estaticos.Contraste(t["color-acento"], t["color-superficie"]) < 4.5);
        Assert.True(Estaticos.Contraste(t["color-alerta-texto"], t["color-superficie"]) >= 4.5);
    }

    [Fact]
    public void ElCalculoDeContrasteEsCorrecto()
    {
        Assert.Equal(21.0, Estaticos.Contraste("#000000", "#FFFFFF"), 1);
        Assert.Equal(1.0, Estaticos.Contraste("#777777", "#777777"), 2);
    }
}

public class TemaReglasTests
{
    [Fact]
    public void LosColoresVivenSoloEnTemaCss()
    {
        var colores = new Regex(@"#[0-9a-fA-F]{3,8}\b|\brgba?\(|\bhsla?\(");
        foreach (var archivo in new[] { "css/site.css", "js/site.js", "js/planificador.js" })
            Assert.False(colores.IsMatch(Estaticos.Leer(archivo)), $"{archivo} tiene un color fijo: muévelo a un token de tema.css");
    }

    [Fact]
    public void LasAnimacionesSeDesactivanConPrefersReducedMotion()
    {
        var css = Estaticos.Leer("css/tema.css");
        Assert.Contains("@media (prefers-reduced-motion: reduce)", css);
        var bloque = css[css.IndexOf("@media (prefers-reduced-motion: reduce)", StringComparison.Ordinal)..];
        Assert.Contains("animation: none", bloque);
        Assert.Contains("transition: none", bloque);
        Assert.Contains("transform: none", bloque);   // también las elevaciones y escalas de hover
    }

    [Fact]
    public void LasTransicionesDurantEntre200Y300Milisegundos()
    {
        var css = Estaticos.Leer("css/tema.css");
        foreach (var token in new[] { "transicion-rapida", "transicion-menu" })
        {
            var ms = int.Parse(Regex.Match(css, $@"--{token}:\s*(\d+)ms").Groups[1].Value);
            Assert.InRange(ms, 200, 300);
        }
    }

    [Fact]
    public void LosEstilosNoCarganNadaDeInternet()
    {
        // Un xmlns="http://www.w3.org/…" dentro de un SVG es solo un identificador, no una petición: se busca url() e @import externos.
        foreach (var archivo in new[] { "css/tema.css", "css/site.css" })
            Assert.DoesNotMatch(@"url\(\s*[""']?https?://|@import\s+[""']?(url\()?\s*[""']?https?://", Estaticos.Leer(archivo));
        foreach (var archivo in new[] { "js/site.js", "js/planificador.js" })
            Assert.DoesNotMatch(@"https?://", Estaticos.Leer(archivo));
    }

    [Fact]
    public void ElHoverAnimaTarjetasFilasYBotones()
    {
        var css = Estaticos.Leer("css/site.css");
        Assert.Contains("a.card:hover", css);
        Assert.Contains("box-shadow: var(--sombra-elevada)", css);
        Assert.Contains(".table-hover tbody tr:hover", css);
        Assert.Contains(".btn:hover:not(:disabled) { transform: scale(", css);
    }
}

/// <summary>Menú lateral: sección activa, accesibilidad e íconos, con la aplicación completa.</summary>
public class MenuLateralTests : IClassFixture<AppConDatosFactory>
{
    private readonly AppConDatosFactory _app;

    public MenuLateralTests(AppConDatosFactory app) => _app = app;

    [Theory]
    [InlineData("/", "Inicio")]
    [InlineData("/Estudiante/DatosPersonales", "Datos Personales")]
    [InlineData("/Estudiante/MateriasTomadas", "Materias Tomadas")]
    [InlineData("/Estudiante/DetalleMateriaTomada?codigo=ISO200", "Materias Tomadas")]      // el detalle pertenece a su sección
    [InlineData("/Estudiante/MateriasFaltantes", "Materias Faltantes")]
    [InlineData("/Estudiante/DetalleMateriaFaltante?codigo=ISO800", "Materias Faltantes")]
    [InlineData("/Estudiante/IndiceAcademico", "Índice Académico")]
    [InlineData("/Pensum/Mapa", "Mapa del pénsum")]
    [InlineData("/Pensum/QueInscribir", "Qué puedo inscribir")]
    [InlineData("/Planificador", "Planificador")]
    [InlineData("/Planificador/Prioridades", "Planificador")]
    [InlineData("/Planificador/Comparar", "Planificador")]
    [InlineData("/Horarios", "Horarios de Banner")]
    [InlineData("/Horarios/Tentativo", "Horarios de Banner")]
    [InlineData("/Horarios/NoDisponible", "Horarios de Banner")]
    [InlineData("/Horarios/Apertura", "Horarios de Banner")]
    [InlineData("/Carrera", "Carrera y pénsum")]
    [InlineData("/Carrera/Guia", "Carrera y pénsum")]
    [InlineData("/Pensum", "Pénsum (CSV)")]
    [InlineData("/Equivalencias", "Equivalencias")]
    [InlineData("/Sincronizaciones", "Sincronizaciones")]
    public async Task LaSeccionActualQuedaResaltadaYSoloUna(string url, string esperada)
    {
        var (estado, html) = await _app.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, estado);

        // Solo un elemento del menú activo (las páginas de detalle tienen además un aria-current en su miga de pan).
        Assert.Single(Regex.Matches(html, "class=\"menu-item activo\""));
        var activo = Regex.Match(html, "<a class=\"menu-item activo\"([^>]*)>.*?<span class=\"menu-texto\">([^<]+)</span>", RegexOptions.Singleline);
        Assert.True(activo.Success, "No hay ningún elemento del menú marcado como activo");
        Assert.Contains("aria-current=\"page\"", activo.Groups[1].Value);   // y lo anuncia a los lectores de pantalla
        Assert.Equal(esperada, activo.Groups[2].Value);
    }

    [Fact]
    public async Task ElMenuEsAccesibleYTieneUnIconoPorSeccion()
    {
        var (_, html) = await _app.GetAsync("/");

        Assert.Contains("id=\"menu-lateral\"", html);
        Assert.Contains("aria-label=\"Menú principal\"", html);
        Assert.Matches("<button id=\"btn-menu\"[^>]*aria-controls=\"menu-lateral\"", html);
        Assert.Contains("aria-label=\"Mostrar u ocultar el menú\"", html);
        Assert.Contains("class=\"salto-contenido\"", html);
        Assert.Contains("id=\"contenido\"", html);

        var menu = html[html.IndexOf("id=\"menu-lateral\"", StringComparison.Ordinal)..html.IndexOf("id=\"menu-fondo\"", StringComparison.Ordinal)];
        Assert.Equal(15, Regex.Matches(menu, "<a class=\"menu-item").Count);
        Assert.Equal(15, Regex.Matches(menu, "<svg class=\"icono\"").Count);       // un ícono por sección
        Assert.Equal(15, Regex.Matches(menu, "aria-hidden=\"true\" focusable=\"false\"").Count);   // decorativos, el texto los acompaña
        Assert.Contains("Horarios de Banner", menu);
        foreach (var grupo in new[] { "Mi carrera", "Planificación", "Datos" }) Assert.Contains(grupo, menu);
    }

    [Fact]
    public async Task ElTemaSeCargaAntesQueLosEstilosDeLaPaginaYNoHayRecursosExternos()
    {
        var (_, html) = await _app.GetAsync("/");

        Assert.True(html.IndexOf("tema.css", StringComparison.Ordinal) < html.IndexOf("site.css", StringComparison.Ordinal));
        Assert.DoesNotMatch("(src|href)=\"https?://", html);   // sin CDN: nada sale de este equipo
    }

    [Fact]
    public async Task LosArchivosDelTemaYDelMenuSeSirven()
    {
        using var cliente = _app.CreateClient();

        var tema = await cliente.GetStringAsync("/css/tema.css");
        Assert.Contains("--color-primario: #1B2A7E", tema);

        var js = await cliente.GetStringAsync("/js/site.js");
        Assert.Contains("menu-colapsado", js);
        Assert.Contains("menu-abierto", js);
        Assert.Contains("Escape", js);          // Esc cierra el menú en móvil
        Assert.Contains("localStorage", js);    // recuerda si lo colapsaste
    }
}

public class IconosTests
{
    [Fact]
    public void GeneraUnSvgDecorativoQueHeredaElColor()
    {
        var svg = WebUtility.HtmlDecode(HistorialAcademico.Web.Helpers.Iconos.Svg("mapa").ToString()!);
        Assert.StartsWith("<svg class=\"icono\"", svg);
        Assert.Contains("stroke=\"currentColor\"", svg);
        Assert.Contains("aria-hidden=\"true\"", svg);
        Assert.Contains("<path d=\"M9 4L3 6", svg);
    }

    [Fact]
    public void UnIconoQueNoExisteFallaConUnMensajeClaro() =>
        Assert.Contains("No existe el ícono", Assert.Throws<ArgumentException>(() => HistorialAcademico.Web.Helpers.Iconos.Svg("nada")).Message);
}
