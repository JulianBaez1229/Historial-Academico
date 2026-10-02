# Cómo contribuir

¡Gracias por querer ayudar! Este proyecto lo usan estudiantes: la privacidad y la claridad importan más que la prisa.

Hay tres formas de ayudar, de la más fácil a la más técnica:

1. **Agregar tu carrera** (un archivo JSON, sin programar): [CONTRIBUTING-pensums.md](CONTRIBUTING-pensums.md).
2. **Reportar un problema o proponer una mejora:** abre un _issue_ con la plantilla que corresponda (error, pénsum nuevo o mejora).
3. **Escribir código:** esta guía.

## Antes de nada: privacidad

**Nunca** subas (en un _issue_, un Pull Request, un comentario o una captura) tu nombre, tu matrícula, tus notas, tu usuario o contraseña,
archivos de sesión (`.auth/`), bases de datos (`*.db`) ni capturas reales de Banner. Si necesitas compartir un HTML o JSON de Banner
para explicar un problema, [anonimízalo primero](tests/samples/README.md). Los Pull Requests con datos personales se cierran.

## Preparar el entorno

Necesitas el [SDK de .NET 8](https://dotnet.microsoft.com/download), Git y [PowerShell 7](https://learn.microsoft.com/powershell/scripting/install/installing-powershell) (para instalar el navegador de Playwright).

```bash
git clone <dirección de tu fork (botón verde «Code»)>
cd HistorialAcademico
dotnet build
pwsh HistorialAcademico.Banner/bin/Debug/net8.0/playwright.ps1 install chromium
dotnet test
dotnet run --project HistorialAcademico.Web
```

La aplicación queda en `http://localhost:5296`. Tus datos de prueba viven en `%LOCALAPPDATA%\HistorialAcademico`, fuera del repositorio.
La suite completa tarda unos 4 minutos porque incluye pruebas con un navegador de verdad.

## Ramas

- `main` siempre compila y pasa las pruebas; no se trabaja directamente sobre ella.
- Trabaja en una rama por cambio, con un nombre corto que diga qué es:
  `pensum/uni-derecho-2022`, `fix/choque-de-horarios`, `feat/exportar-horario`, `docs/faq-banner`.

```bash
git switch -c fix/choque-de-horarios
```

## Commits

- Escribe en **español**. Primera línea corta (unos 70 caracteres) que diga el _porqué_; una línea en blanco; y el detalle de qué cambió y cómo lo probaste.
- Un commit = una idea. Si tu cambio arregla un error y además reordena código, sepáralo.
- Si te ayudó una herramienta de IA (como Claude Code), agrégalo al final: `Co-Authored-By: Nombre del modelo <noreply@anthropic.com>`.

## Pull Requests

1. Asegúrate de que `dotnet build` sale **sin advertencias** y de que `dotnet test` pasa.
2. Abre el Pull Request contra `main` y llena la plantilla: qué cambia, por qué y cómo lo probaste.
3. La GitHub Action compila, corre las pruebas y valida los pénsums. Debe salir en verde.
4. Alguien lo revisa; puede pedirte cambios. Responde en el mismo Pull Request.

### Lista de comprobación

- [ ] Compila sin advertencias.
- [ ] Hay pruebas para la lógica nueva o corregida (y fallaban antes del cambio, si era un error).
- [ ] Todo lo que ve el usuario está en **español**.
- [ ] No hay datos personales, credenciales, sesiones ni archivos de muestra sin anonimizar.
- [ ] Si cambia la arquitectura, actualicé [CLAUDE.md](CLAUDE.md).
- [ ] Si agrega una migración de EF, la generé con la herramienta local (ver CLAUDE.md).

## Cómo está organizado el código

Resumen (el detalle está en [CLAUDE.md](CLAUDE.md)):

- `HistorialAcademico.Core`: la lógica (índice, pénsum, planificador, horarios), sin web ni base de datos.
- `HistorialAcademico.Banner`: lo que habla con Banner y lee su HTML/JSON.
- `HistorialAcademico.Web`: la aplicación MVC.
- `HistorialAcademico.Validador`: la herramienta de consola `validador-pensums`.
- `HistorialAcademico.Tests`: las pruebas. Los datos de ejemplo son ficticios o anonimizados.

Reglas que conviene recordar:

- **Las reglas de cada universidad** (escala de notas, períodos, límites de créditos) están en `pensums/<universidad>/universidad.json`, no en el código.
- **Banner solo se consulta cuando la persona lo pide.** No agregues consultas en segundo plano ni reintentos en bucle.
- **Los colores** van en variables de `tema.css`; `site.css` no lleva colores fijos.

## Agregar un lector para otro formato de Banner

1. Guarda la página o respuesta real **fuera** del repositorio.
2. Anonimízala: `dotnet run --project HistorialAcademico.Validador -- anonimizar ruta/al/archivo.html`.
3. Revisa el resultado a mano y guárdalo en `tests/samples/`.
4. Escribe el lector en `HistorialAcademico.Banner` y pruebas contra la muestra.

## Regenerar las capturas del README

```bash
HA_CAPTURAS=1 dotnet test --filter CapturasReadme
```

(En PowerShell: `$env:HA_CAPTURAS='1'; dotnet test --filter CapturasReadme`.) Usa datos ficticios y deja los PNG en `docs/capturas/`.

## Publicar una versión (para quien mantiene el proyecto)

1. Con `main` en verde, crea y sube una etiqueta con el número de versión: `git tag v1.0.0` y `git push origin v1.0.0`.
   (`v1.0.0-beta.1` se publica como versión de prueba, no como la estable.)
2. La Action «Publicar versión» prueba el código, arma el programa para Windows, Linux y macOS, y crea el Release con las sumas de comprobación.
3. Revisa el Release en GitHub: que estén el instalador `HistorialAcademico-Instalador-vX.Y.Z.exe`, los cuatro `.zip`/`.tar.gz` y `SHA256SUMS.txt`, y
   **instala el de Windows en un equipo limpio** (o una máquina virtual) para comprobar que se instala, abre el navegador y descarga Chromium.
4. **No crees el Release a mano en la web de GitHub:** sube solo la etiqueta (o usa Actions → Publicar versión → Run workflow, escribiendo la etiqueta) y la Action lo crea con
   todos los archivos. Si ya lo creaste a mano, la Action le agrega los archivos al mismo Release.
5. **La primera vez que salga una versión estable** (no `-beta`), actualiza el README: borra el bloque «Estado actual» (entre los comentarios `ESTADO-RELEASE`).
   Hasta entonces el README dice, con razón, que todavía no hay una versión estable.

## Conducta

Sé amable y paciente: quien te lee puede ser un estudiante que está aprendiendo. Las críticas van al código, no a las personas.

## Licencia

Al contribuir aceptas que tu aporte se publique bajo la licencia [MIT](LICENSE) del proyecto.
