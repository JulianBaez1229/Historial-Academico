## Qué cambia y por qué

<!-- Una o dos frases. Enlaza el issue si lo hay (Cierra #123). -->

## Cómo lo probé

<!-- Pruebas nuevas, comandos que corriste, pantallas que revisaste. -->

## Lista de comprobación

- [ ] `dotnet build` sale sin advertencias y `dotnet test` pasa.
- [ ] Hay pruebas para la lógica nueva o corregida.
- [ ] Todo lo que ve el usuario está en español.
- [ ] No incluí datos personales (notas, matrícula, nombres de estudiantes o de profesores), credenciales, sesiones ni muestras sin anonimizar.
- [ ] Si cambia la arquitectura, actualicé `CLAUDE.md`.

Guía: [CONTRIBUTING.md](../CONTRIBUTING.md)

---

### Solo si agregas o cambias un pénsum

## De dónde sale el plan

<!-- Enlace a la página oficial o al documento del plan de estudios. Si es la primera carrera de esa universidad,
     de dónde salen la escala de calificaciones, los períodos y el límite de créditos. -->

- [ ] El plan es el oficial de la universidad (no de memoria ni de un compañero).
- [ ] No incluí datos personales (notas, matrícula, nombres de estudiantes o de profesores).
- [ ] El archivo está en `pensums/<universidad>/<carrera>-<versión>.json` y coincide con sus propiedades `carrera` y `version`.
- [ ] Ejecuté `dotnet run --project HistorialAcademico.Validador -- validar` y sale sin errores.
- [ ] Si la universidad es nueva, agregué su `universidad.json`.

Guía completa: [CONTRIBUTING-pensums.md](../CONTRIBUTING-pensums.md)
