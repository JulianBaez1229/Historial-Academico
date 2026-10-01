# Cómo agregar tu carrera (un pénsum nuevo)

Cada carrera de la aplicación es un archivo JSON con su plan de estudios. Si tu carrera no está en la lista de
«Carrera y pénsum», puedes agregarla tú: no hace falta programar. Esta guía te lleva paso a paso, desde el archivo hasta el Pull Request.

## Antes de empezar

- Usa **el plan de estudios oficial** de tu carrera, el que publica tu universidad (no el de un compañero ni uno «de memoria»).
- Un pénsum es información pública del plan: materias, créditos, prerrequisitos. **No incluyas datos personales** (tus notas,
  tu matrícula, tu nombre, tus profesores). Los pénsums no llevan nada de eso.
- Cada archivo es **un plan de una carrera**. Si la universidad cambia el plan, se agrega un archivo nuevo con otra `version`;
  el anterior se conserva para quienes siguen en él.
- Necesitas Git y, para comprobar tu archivo en tu computador, el [SDK de .NET 8](https://dotnet.microsoft.com/download). Es opcional
  pero recomendable: es la misma comprobación que hace el Pull Request.

## Dónde va cada archivo

```text
pensums/
  schema.json                      esquema de los pénsums
  universidad.schema.json          esquema de las universidades
  unapec/
    universidad.json               reglas de la universidad (escala de notas, períodos, límites de créditos)
    ingenieria-software-11.json   un pénsum: <carrera>-<versión>.json
```

- La carpeta es el identificador de la universidad: solo minúsculas, números y guiones (`unapec`, `uni-de-ejemplo`).
- El archivo se llama `<carrera>-<versión>.json` (`ingenieria-software-11.json`) y **debe coincidir** con las propiedades
  `carrera` y `version` de su interior. La validación lo comprueba.

## Paso 1: la universidad (solo si es nueva)

Si tu universidad ya tiene carpeta, sáltate este paso. Si no, crea `pensums/<universidad>/universidad.json`:

```json
{
  "formato": 1,
  "id": "uni-de-ejemplo",
  "nombre": "Universidad de Ejemplo",
  "escalaCalificaciones": [
    { "letra": "A", "puntos": 4, "aprueba": true, "cuentaParaIndice": true },
    { "letra": "B", "puntos": 3, "aprueba": true, "cuentaParaIndice": true },
    { "letra": "C", "puntos": 2, "aprueba": true, "cuentaParaIndice": true },
    { "letra": "F", "puntos": 0, "aprueba": false, "cuentaParaIndice": true },
    { "letra": "P", "aprueba": true, "cuentaParaIndice": false, "nota": "Exenta: aprueba, pero no entra en el índice." }
  ],
  "periodos": [
    { "nombre": "SEM1" },
    { "nombre": "SEM2" }
  ],
  "limitesCreditos": { "base": 21, "alto": 24, "umbralIndice": 3.5 }
}
```

| Propiedad | Qué poner |
| --- | --- |
| `id` | El mismo nombre de la carpeta. |
| `nombre` | El nombre para mostrar. |
| `urlBanner` | Opcional. La página de entrada de Banner si tu universidad lo usa (debe empezar con `https://`). |
| `escalaCalificaciones` | Cada letra (`letra`) con sus `puntos`, si `aprueba` y si `cuentaParaIndice`, y una `nota` opcional. Una letra que cuenta para el índice necesita `puntos`; una que no cuenta (como una exenta) no debe tenerlos. |
| `periodos` | Los períodos de un año, en orden. Pueden ser dos semestres, cuatro trimestres… Si usa Banner, agrega `codigoBanner` (dos dígitos) a **todos**. |
| `limitesCreditos` | `base`: créditos máximos por período; `alto`: el máximo si el índice supera `umbralIndice`. |

## Paso 2: el pénsum

### Opción A: convertir desde un CSV (lo más cómodo)

Copia el plan a una hoja de cálculo con estas columnas y guárdala como CSV (UTF-8):

```text
codigo,nombre,creditos,cuatrimestre,prerrequisitos,es_electiva
ESP101,Análisis de Textos Discursivos,3,1,,false
ESP106,Redacción de Textos Discursivos I,3,2,ESP101,false
E077,Electiva I,3,7,59% créditos aprobados,true
```

Los prerrequisitos van separados por `;` y pueden ser un código (`ESP101`) o una regla de porcentaje (`67% créditos aprobados`).
Después convierte el CSV:

```text
dotnet run --project HistorialAcademico.Validador -- convertir mi-plan.csv --universidad uni-de-ejemplo --carrera derecho --nombre "Derecho" --version 2022
```

Crea `pensums/uni-de-ejemplo/derecho-2022.json` y te avisa si falta algo. El CSV no trae las electivas con sus opciones, las
certificaciones, los requisitos de graduación ni las equivalencias: agrégalos a mano en el JSON si tu plan los tiene.

### Opción B: escribirlo a mano

Un ejemplo mínimo, con dos materias:

```json
{
  "formato": 1,
  "universidad": "uni-de-ejemplo",
  "carrera": "derecho",
  "nombreCarrera": "Derecho",
  "version": "2022",
  "totalCreditos": 8,
  "cuatrimestres": 2,
  "materias": [
    { "codigo": "AAA100", "nombre": "Introducción al Derecho", "creditos": 3, "cuatrimestre": 1 },
    { "codigo": "BBB200", "nombre": "Derecho Civil", "creditos": 5, "cuatrimestre": 2, "prerrequisitos": ["AAA100"] }
  ]
}
```

### Qué significa cada propiedad

| Propiedad | Qué poner |
| --- | --- |
| `formato` | Siempre `1`. |
| `universidad` | El identificador de la carpeta (`uni-de-ejemplo`). |
| `carrera` | El identificador de la carrera: minúsculas, números y guiones. |
| `nombreCarrera` | El nombre para mostrar (`Derecho`). |
| `version` | La versión o el año del plan (`2022`, `11`, `2019-b`). |
| `totalCreditos` | La **suma** de los créditos de todas las materias. La validación lo comprueba. |
| `cuatrimestres` | Cuántos períodos tiene el plan (del 1 al 24). |
| `materias` | La lista de materias (abajo). |
| `bloquesElectivas` | Opcional. Las electivas y las materias entre las que se elige (abajo). |
| `certificaciones` | Opcional. Grupos de materias que sustituyen a las electivas generales. |
| `requisitosGraduacion` | Opcional. Textos con lo que se exige además del pénsum (deporte, inglés, pasantía…). |
| `equivalencias` | Opcional. Materias de un plan anterior que cubren materias de este pénsum (abajo). |

**Materias** (`materias`):

| Propiedad | Qué poner |
| --- | --- |
| `codigo` | En mayúsculas y números, de 2 a 12 caracteres (`ISO100`). Sin repetir. |
| `nombre` | El nombre oficial. |
| `creditos` | De 0 a 20. Las materias sin crédito (por ejemplo los niveles de inglés) llevan `0`. |
| `cuatrimestre` | El período del plan en que se ubica, de 1 hasta `cuatrimestres`. |
| `prerrequisitos` | Una lista: cada elemento es un código (`"ISO100"`) o una regla de porcentaje (`"67% créditos aprobados"`). Los códigos deben existir y no pueden formar un ciclo. |
| `electiva` | `true` si es un espacio de electiva. Debe tener su bloque en `bloquesElectivas`. |

**Electivas** (`bloquesElectivas`): por cada materia con `"electiva": true`, un bloque con su `codigo`, su `nombre` y las
`opciones` (cada una con `codigo` y `nombre`) entre las que se elige.

**Certificaciones** (`certificaciones`): cada una con `nombre`, sus `materias` (códigos), `reemplazaElectivas` (`true` si al elegirla no se
toman las electivas generales) y una `nota` opcional.

**Equivalencias** (`equivalencias`): cada una con `origen` (el código de la materia del plan anterior, **tal como aparece en el
histórico de Banner**), `destino` (la lista de materias de este pénsum que cubre; deben existir) y una `nota` opcional. Un `destino` vacío
(`[]`) declara que esa materia se sabe que no tiene equivalente. Un mismo `origen` no se repite: si cubre varias materias, se juntan en una entrada
(`"origen": "ING701", "destino": ["ING716", "ING717"]`).

Las propiedades que no están en esta guía se rechazan, para que un error de tipeo (`creditso`) no pase desapercibido.

## Paso 3: comprueba tu archivo

Desde la carpeta del proyecto:

```text
dotnet run --project HistorialAcademico.Validador -- validar
```

Si todo está bien verás algo así:

```text
OK      unapec/universidad.json  (UNAPEC – Universidad APEC)
OK      unapec/ingenieria-software-11.json  (Ingeniería de Software: 75 materias, 218 créditos, 12 cuatrimestres)
OK      uni-de-ejemplo/derecho-2022.json  (Derecho: 2 materias, 8 créditos, 2 cuatrimestres)

Todo en orden: 2 pénsums y 2 universidades válidos.
```

Si algo falla, el mensaje dice dónde está el problema, por ejemplo `materias[3].creditos debe estar entre 0 y 20 (es 30)`. Corrígelo y
vuelve a ejecutar. Los **avisos** no bloquean, pero conviene leerlos (por ejemplo, un prerrequisito que está en un cuatrimestre posterior).

También puedes verlo en la aplicación: arráncala, abre **Carrera y pénsum** y tu carrera aparece en la lista, con sus créditos y,
si ya sincronizaste tu histórico, las materias que se convalidarían.

## Paso 4: abre el Pull Request

1. Haz un **fork** del repositorio (botón *Fork* en GitHub) y clónalo:

   ```text
   git clone https://github.com/<tu-usuario>/<repositorio>.git
   cd <repositorio>
   ```

2. Crea una rama con un nombre claro:

   ```text
   git switch -c pensum-uni-de-ejemplo-derecho-2022
   ```

3. Agrega tus archivos (el `universidad.json` solo si la universidad es nueva), valida y haz el commit:

   ```text
   git add pensums/uni-de-ejemplo
   git commit -m "Agrega el pénsum de Derecho (plan 2022) de la Universidad de Ejemplo"
   git push -u origin pensum-uni-de-ejemplo-derecho-2022
   ```

4. En GitHub, abre el **Pull Request** hacia la rama principal. En la descripción escribe:
   - la universidad, la carrera y el plan (año o versión);
   - **de dónde sacaste el plan** (el enlace a la página oficial o el documento);
   - si es la primera carrera de esa universidad, de dónde salen la escala de calificaciones y el límite de créditos.

5. Una **GitHub Action** valida tus archivos automáticamente: comprueba el JSON contra el esquema y ejecuta la misma validación del
   paso 3. Si falla, abre el detalle de la ejecución (*Details*), lee el mensaje, corrige el archivo y vuelve a empujar: el Pull Request se
   actualiza solo.

## Preguntas frecuentes

**¿Cómo pongo las electivas?** Cada espacio de electiva es una materia con `"electiva": true` y un bloque en `bloquesElectivas` con las opciones.
Si el plan solo dice «electiva» sin opciones concretas, pon una opción genérica (`"codigo": "ELE100", "nombre": "Electiva a elegir"`).

**Solo quiero usarlo yo, sin compartirlo.** En la aplicación abre **Carrera y pénsum → Importar mi pénsum pegando el texto**: pegas la tabla del plan
que copiaste de la página de tu universidad (código, asignatura, créditos y prerrequisitos), revisas y corriges la vista previa y lo guardas como
**pénsum personal**. Queda en un archivo `personal-<carrera>-<versión>.json` dentro de `pensums/`, que Git ignora, y aparece en la lista como cualquier otro.
Si después quieres compartirlo, renombra el archivo a `<carrera>-<versión>.json` (sin el `personal-`), cambia `carrera` dentro para que coincida y sigue esta guía desde el paso 3.

**Mi plan usa horas, no créditos.** Usa el valor que la universidad llama crédito. Si no hay créditos, no se puede calcular el avance: pregunta en la escuela cuántos créditos vale cada materia.

**Una materia se puede tomar en cualquier momento (como la pasantía).** Ponla en el cuatrimestre donde el plan la ubica y sin prerrequisitos.

**El plan cambió y mis materias tienen otros códigos.** Crea el pénsum nuevo con otra `version` y declara en `equivalencias` qué materia del plan anterior
cubre a cuál del nuevo. Al elegirlo, la aplicación muestra cuántos créditos y qué materias se convalidan con tu histórico.

**¿Puedo corregir un pénsum que ya existe?** Sí: abre un Pull Request que lo modifique y explica en la descripción qué cambió y por qué
(con el enlace al plan oficial). Si el cambio es que la universidad publicó un plan nuevo, mejor agrega un archivo nuevo.

**No sé cuál es mi código de Banner.** Aparece en tu histórico académico, en la columna de la materia (por ejemplo `ING701`).
