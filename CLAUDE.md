# CLAUDE.md — reglas para Claude Code en este repositorio

Historial Académico es una aplicación ASP.NET Core MVC (.NET 8, EF Core + SQLite, Playwright) que lee el histórico de Banner de un
estudiante, calcula su avance y planifica cuatrimestres. **Todo corre en local**. La interfaz y los mensajes van en **español**.

## Reglas que no se negocian (privacidad y seguridad)

1. **Nunca** pongas usuario, contraseña, tokens ni cookies en el código, en `appsettings*.json`, en pruebas ni en el repositorio.
   La persona inicia sesión sola en un Chromium visible; el código nunca ve sus credenciales.
2. **Nada personal en el repositorio:** ni nombres, matrículas, notas reales, fechas de nacimiento, correos, archivos de sesión (`.auth/`),
   bases de datos (`*.db`), capturas de Banner (`samples/`) ni rutas de usuario. Lo ignora `.gitignore`; no lo fuerces con `git add -f`.
3. **Las muestras se anonimizan:** un HTML/JSON real de Banner solo entra al repo después de
   `dotnet run --project HistorialAcademico.Validador -- anonimizar <archivo>` y de revisarlo a mano. Van en `tests/samples/` o
   `HistorialAcademico.Tests/Fixtures/`. Las pruebas no leen datos reales de la máquina de nadie salvo las opcionales `*RealFact`, que se omiten si faltan.
4. **Banner solo a petición de la persona:** nada de consultas en segundo plano ni reintentos en bucle. Pausa entre peticiones
   (`BannerOptions.PausaEntreConsultasMs`) y nunca a ráfagas.
5. **De otras personas se guarda lo mínimo:** solo el nombre de los profesores que Banner publica en un horario.
6. **Nada sale del equipo sin permiso:** no agregues telemetría, servicios externos ni descargas. Las únicas conexiones son Banner
   (a petición) y, si la persona no lo desactivó, la consulta de nuevas versiones del proyecto.
7. **No publiques por tu cuenta:** no hagas `git push`, no abras Pull Requests ni releases, no reescribas la historia sin que te lo pidan.
   Para publicar el código con historia limpia existe `scripts/exportar-repositorio-limpio.ps1`.

## Cómo se trabaja

- **Por fases:** al terminar una fase corre `dotnet build` (0 advertencias), **todas** las pruebas, haz commit, reinicia la app y resume en español;
  espera confirmación antes de la siguiente.
- **Definición de terminado:** compila sin advertencias · pruebas para la lógica nueva · interfaz en español · ningún dato personal, credencial ni sesión
  en el repo · actualizar este archivo si cambia la arquitectura.
- **Commits:** mensaje en español, primera línea corta con el porqué, cuerpo con lo que cambia y cómo se probó. Los hechos con ayuda de
  Claude terminan con `Co-Authored-By: Claude <noreply@anthropic.com>` (el nombre exacto del modelo que se use). Las descripciones de PR terminan con
  `🤖 Generated with [Claude Code](https://claude.com/claude-code)`.
- **Antes de reiniciar la app o compilar**, detén la que esté corriendo (bloquea los `.dll`). Se inicia con
  `dotnet run --project HistorialAcademico.Web --no-build` (http://localhost:5296).

## Comandos

```bash
dotnet build                                   # debe salir sin advertencias
dotnet test                                    # toda la suite (~4 min; incluye pruebas con Chromium)
dotnet test --filter "FullyQualifiedName~Planificador"
dotnet run --project HistorialAcademico.Validador -- validar pensums
dotnet run --project HistorialAcademico.Validador -- anonimizar ruta/al/historico.html
HA_CAPTURAS=1 dotnet test --filter CapturasReadme   # regenera docs/capturas/*.png (Windows: $env:HA_CAPTURAS='1')
```

Migraciones de EF (la herramienta es local, `.config/dotnet-tools.json`):

```bash
dotnet tool run dotnet-ef migrations add NombreCambio --project HistorialAcademico.Web --startup-project HistorialAcademico.Web --output-dir Migrations --no-build
```

## Arquitectura

| Proyecto | Qué contiene |
| --- | --- |
| `HistorialAcademico.Core` | Lógica pura, sin web ni base de datos: `Indice` (cálculo del índice), `Pensum` (formato JSON, motor de estado, conversor CSV, prerrequisitos), `Universidad` (escala de notas, períodos, límites), `Planificacion` (generador, validador, prioridades, simulador de índice, reconciliador), `Horarios` (secciones, choques, horario tentativo), `Perfiles` (almacén, PIN), `Manual`, `Models` y `Entities`. |
| `HistorialAcademico.Banner` | Todo lo que toca Banner: `BannerClient` (Playwright, sesión, consultas), `HistoricoParser` y `SeccionesParser` (HTML/JSON → modelos), `AnonimizadorHistorico`. |
| `HistorialAcademico.Web` | MVC: `Controllers`, `Views` (Razor, español), `Services` (casos de uso), `Perfiles` (`GestorPerfiles`, `PerfilMiddleware`, `PerfilActual`), `Data` (EF Core), `Migrations`, `wwwroot`. |
| `HistorialAcademico.Validador` | Consola `validador-pensums`: `validar`, `convertir`, `anonimizar`. Códigos de salida 0/1/2. |
| `HistorialAcademico.Tests` | xUnit, `WebApplicationFactory`, Playwright, AngleSharp. `Fixtures/` (datos sintéticos) y `tests/samples/` (muestras anonimizadas). |
| `pensums/` | Un JSON por carrera (`<universidad>/<carrera>-<versión>.json`) y `universidad.json` por universidad, con sus esquemas. |

Puntos clave:

- **Perfiles:** cada persona tiene `%LOCALAPPDATA%\HistorialAcademico\perfiles\<id>\{historial.db, .auth\banner.json, samples\}` y `perfiles.json`.
  La cookie `ha_perfil` (Data Protection) elige el perfil; el PIN se guarda con PBKDF2. Con `Perfiles:BaseFija` (pruebas) hay una base única sin perfiles.
- **Las reglas de la universidad no están en el código:** el índice, el estado de las materias y el planificador leen `universidad.json`
  (`ReglasUniversidad`). UNAPEC: A=4, B=3, C=2, D=1, F=0 (la F no aprueba pero sí baja el índice); E = exenta (aprueba, no entra al índice).
- **Sincronización:** `SincronizacionService` valida el histórico (`ValidadorHistorico`: las materias suman los totales, los acumulados encadenan)
  y solo entonces toca la base. Si algo no cuadra, no guarda nada.
- **Modo sin Banner:** `MateriaManual` + `HistoricoManual.Fusionar` (Banner manda en los períodos que trae).
- **Planificador:** un escenario por plan (`PlanEstudio`, uno `Activo`), `ValidadorColocacion`, `AlertasPlan`, `SimuladorIndice`
  (`simulador.js` repite el texto de C#: si cambias uno, cambia el otro), `ReconciliadorPlan` (ajusta el plan tras sincronizar) y exportación PDF/PNG con Playwright.
- **Programa descargable:** `dotnet publish HistorialAcademico.Web -r <rid> --self-contained` (lo arma `.github/workflows/release.yml` al subir una etiqueta `v*`;
  la versión sale de la etiqueta con `-p:Version=`). `appsettings.Production.json` (solo viaja en el programa publicado) enciende `Inicio:PuertoLibre`,
  `Inicio:InstalarNavegador` y `Inicio:AbrirNavegador`: `Arranque` (Web/Helpers) elige el puerto, abre el navegador y vuelve a la carpeta del programa;
  `InstaladorNavegador` (Banner) instala Chromium la primera vez. En desarrollo y pruebas todo eso está apagado. El ejecutable debe llamarse `HistorialAcademico.Web`
  (el ajuste de carpeta, `LEEME.txt` y el README dependen del nombre). El publicado lleva `pensums/`, `LICENSE` y `LEEME.txt`.
- **Aviso de versiones nuevas:** `ActualizacionesService` (Web/Services) hace UNA consulta GET a `api.github.com/repos/<usuario>/<repo>/releases/latest` al arrancar
  (y otra si la persona pulsa «Buscar ahora»), solo si `Actualizaciones:Activas` (solo en `appsettings.Production.json`), el programa sabe su repositorio
  (`-p:RepositorioGitHub=` del flujo de Release, o `Actualizaciones:Repositorio`) y la preferencia del equipo (`preferencias.json`, `AlmacenPreferencias`) no está apagada.
  Compara con `VersionApp` (Core). Nunca en bucle, con tope de 5 s y 256 KB; la respuesta se trata como dato (el enlace debe ser de los Releases de ese repositorio).
  Si cambias qué se envía, actualiza «Mis datos», el README y `LEEME.txt`.
- **Integración continua:** `.github/workflows/ci.yml` (PR a `main`: compila con `-warnaserror`, pruebas, validar pénsums) y `validar-pensums.yml` (esquemas JSON).
  Solo se usan acciones de `actions/*` y nunca `pull_request_target` ni secretos.
- **Estilos:** `wwwroot/css/site.css` no lleva colores hexadecimales ni `rgb()`: solo variables de `tema.css` (claro/oscuro). Una prueba lo exige y otra audita el contraste.
- **Textos de usuario** en español dominicano (`Ui.Cultura`); fechas y decimales iguales en cualquier equipo.

## Estilo de código

- Sigue el estilo de lo que ya existe (nombres en español, comentarios que explican el porqué, `<summary>` en español en lo público).
- Una prueba por comportamiento, con nombre que dice lo que pasa. Para totales esperados, calcula a mano o con otra herramienta: no copies lo que devuelve el código.
- Comprueba que tus ediciones quedaron (una que falla en silencio se detecta con la prueba, no con la vista).
- No agregues dependencias sin una razón clara.
