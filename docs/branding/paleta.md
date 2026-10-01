# Paleta de colores

La aplicación usa una paleta propia de azul marino y dorado. Todos los colores viven como variables CSS en
`HistorialAcademico.Web/wwwroot/css/tema.css` (tema claro y tema oscuro con los mismos nombres de token): cambiar de paleta es
cambiar solo ese archivo. `site.css` no lleva colores fijos.

| Color | Uso | Hex (tema claro) | Token |
|---|---|---|---|
| Azul marino | Color principal: menú lateral, botones y enlaces | `#1B2A7E` | `--color-primario` |
| Dorado | Acento: elemento activo del menú, bordes e indicadores | `#C4A23A` | `--color-acento` |
| Rojo | Peligro y errores | `#D0202E` | `--color-peligro` |
| Azul acero | Información | `#3A6EA5` | `--color-info` |
| Blanco | Superficies (tarjetas, tablas) | `#FFFFFF` | `--color-superficie` |

Además hay tokens para los estados de las materias (aprobada, exenta, en curso, disponible, bloqueada), los avisos, los gráficos y el mapa
del pénsum. Ver `tema.css`.

## Uso y contraste (WCAG AA, mínimo 4.5:1 para texto)

- El **dorado sobre blanco no cumple** (≈2.4:1): no se usa como color de texto. Se usa como acento (bordes, indicadores)
  y como fondo con texto azul marino encima (≈5.1:1).
- Texto blanco sobre azul marino (≈12.5:1), sobre rojo (≈5.3:1) y sobre azul acero (≈5.3:1) sí cumple.
- Las pruebas `TemaContrasteTests` comprueban todos los pares texto/fondo definidos en `tema.css`, en los dos temas.

## Marca y afiliación

Este proyecto es independiente: **no está afiliado, patrocinado ni avalado por ninguna universidad**. Los colores son solo una elección de
diseño; no reproducen ni reclaman ningún escudo, logotipo ni identidad visual oficial, y el repositorio no incluye escudos ni logotipos de
universidades. Los nombres de universidades y de Banner aparecen únicamente para decir con qué sistemas funciona la aplicación
(por ejemplo, el nombre de la universidad de un pénsum).

Si haces un _fork_ para una universidad concreta y quieres usar sus colores o su escudo, es cosa tuya: cambia los tokens de `tema.css`
y asegúrate de tener permiso para usar esa marca. Las imágenes que se guarden en `docs/branding/` están ignoradas por Git a propósito.
