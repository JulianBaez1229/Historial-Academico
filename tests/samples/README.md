# Muestras anonimizadas

Aquí viven los archivos de ejemplo que usan las pruebas de los lectores (parsers) de Banner. **Nunca** se sube un archivo real: solo muestras
con identidad y calificaciones ficticias.

| Archivo | Qué es |
|---|---|
| `historico-anonimizado.html` | Un Histórico Académico con la forma de uno real (siete períodos, materias exentas, códigos del plan anterior y un período en progreso). Las pruebas `HistoricoParserRealTests`, `SincronizacionTests`, `EstadoConDatosRealesTests` y `PlanificadorServiceTests` verifican contra él. |

Además, en `HistorialAcademico.Tests/Fixtures/` hay muestras hechas a mano: `historico_sintetico.html`, `secciones-anonimizadas.json` y `laboratorio-sintetico.md`.

## Cómo agregar una muestra nueva (por ejemplo, de otra universidad o de un formato de Banner distinto)

1. Guarda el HTML real **fuera del repositorio** (la carpeta `samples/` de tu perfil ya está ignorada por Git).
2. Anonimízalo con el validador:

   ```bash
   dotnet run --project HistorialAcademico.Validador -- anonimizar ruta/al/historico.html --anio-inicial 2024
   ```

   Crea `tests/samples/<nombre>-anonimizado.html`. El comando:
   - reemplaza el nombre, la matrícula, la fecha de nacimiento, el programa, la escuela y el campus por datos ficticios;
   - sortea de nuevo las calificaciones (lo aprobado sigue aprobado y lo reprobado, reprobado) con una semilla fija: el mismo archivo da siempre el mismo resultado;
   - recalcula los puntos de calidad y todos los totales, y comprueba que la muestra se lee y cuadra;
   - nunca toca el archivo original ni pisa una muestra existente (usa `--forzar`).
3. **Ábrelo y revísalo a mano** antes de hacer commit: el comando garantiza que no quedan el nombre, el programa ni el campus originales, pero solo una persona puede confirmar que no queda nada más que identifique a alguien.
4. Agrega la prueba que la use y haz commit. El archivo original no se sube nunca.

Opciones: `--salida <archivo>`, `--anio-inicial <año>` (el primer período de la muestra pasa a ese año), `--semilla <número>`, `--forzar`.
