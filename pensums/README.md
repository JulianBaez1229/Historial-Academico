# Pénsums

> ¿Quieres agregar tu carrera? Lee la guía [CONTRIBUTING-pensums.md](../CONTRIBUTING-pensums.md): formato, un ejemplo,
> cómo comprobar tu archivo y cómo abrir un Pull Request. Cada Pull Request se valida automáticamente con una GitHub Action.

Cada carrera es un archivo JSON con el plan de estudios, en `pensums/<universidad>/<carrera>-<versión>.json`.
Por ejemplo, `pensums/unapec/ingenieria-software-11.json`.

- El formato está descrito en [`schema.json`](schema.json) (JSON Schema 2020-12).
- La aplicación valida cada archivo al cargarlo, con las mismas reglas del esquema y algunas más que un esquema no puede
  expresar. Los mensajes van en español y dicen dónde está el problema (por ejemplo, `materias[3].creditos debe estar entre 0 y 20`).

## Reglas de la universidad

Cada universidad tiene un archivo `pensums/<universidad>/universidad.json` (esquema en
[`universidad.schema.json`](universidad.schema.json)) con lo que cambia de una universidad a otra. El índice, el estado de las
materias y el planificador leen estas reglas en lugar de tenerlas fijas en el código.

| Propiedad | Qué es |
| --- | --- |
| `nombre`, `urlBanner` | Nombre para mostrar y página de entrada de Banner (si la universidad lo usa; debe ser `https://`). |
| `escalaCalificaciones` | Las letras: `puntos`, si `aprueba` y si `cuentaParaIndice`. En UNAPEC la F no aprueba pero sí baja el índice, y la E (exenta) aprueba pero no entra en el índice. Una letra que cuenta para el índice necesita `puntos`; una que no cuenta no debe tenerlos. |
| `periodos` | Los períodos de un año en orden (`ENE-ABR`, `MAY-AGO`, `SEP-DIC`), con su `codigoBanner` si aplica (todos o ninguno). Pueden ser dos semestres, cuatro trimestres, etc. |
| `limitesCreditos` | `base` (créditos máximos por período), `alto` (si el índice supera `umbralIndice`) y el umbral, que no puede pasar del máximo de la escala. |

La aplicación usa la universidad indicada en `Pensums:Universidad` (por defecto `unapec`) y busca la carpeta `pensums/`
desde la aplicación hacia arriba (o la que indique `Pensums:Carpeta`). Si el archivo falta o tiene errores no se cae: usa las
reglas incorporadas de UNAPEC y deja el motivo en el registro. Una prueba vigila que el archivo de UNAPEC coincida con lo incorporado.

## Reglas de un pénsum

| Regla | Detalle |
| --- | --- |
| Identificadores | `universidad` y `carrera` en minúsculas, con números y guiones (`unapec`, `ingenieria-software`). La carpeta y el nombre del archivo deben coincidir con `universidad`, `carrera` y `version`. |
| Créditos | `totalCreditos` debe ser la suma de los créditos de todas las materias. |
| Materias | Código en mayúsculas y números (2 a 12). Sin códigos repetidos. `cuatrimestre` entre 1 y `cuatrimestres`. |
| Prerrequisitos | Lista de textos: un código de materia (`"ISO100"`) o una regla de porcentaje (`"67% créditos aprobados"`), uno por elemento. Deben existir, no pueden apuntar a la propia materia ni formar ciclos. |
| Electivas | Una materia con `"electiva": true` debe tener su bloque en `bloquesElectivas`, con las materias entre las que se elige. |
| Certificaciones | Grupos de materias que sustituyen a las electivas generales (`reemplazaElectivas`). |
| Requisitos de graduación | Texto libre para lo que se exige además del pénsum (deporte, inglés, pasantía…). |
| Equivalencias | Materias de un plan anterior (u otra carrera), con el código que aparece en el histórico de Banner (`origen`), que cubren materias de este pénsum (`destino`, que deben existir). `"destino": []` declara que se sabe que no tiene equivalente. Un `origen` no puede repetirse: si cubre varias materias, se juntan en una sola entrada (`"origen": "ING701", "destino": ["ING716", "ING717"]`). |

Las equivalencias del pénsum elegido se usan junto con las que cada persona agrega a mano en la aplicación (si una está en las dos,
cuenta una vez). Al elegir una carrera, la pantalla «Carrera y pénsum» muestra cuántos créditos y qué materias se convalidarían
con tu histórico antes de cambiar.
| Propiedades desconocidas | Se rechazan, para que un error de tipeo (`creditso`) no pase desapercibido. |

## Ejemplo mínimo

```json
{
  "formato": 1,
  "universidad": "uni-prueba",
  "carrera": "carrera-x",
  "nombreCarrera": "Carrera X",
  "version": "2019",
  "totalCreditos": 8,
  "cuatrimestres": 2,
  "materias": [
    { "codigo": "AAA100", "nombre": "Primera", "creditos": 3, "cuatrimestre": 1 },
    { "codigo": "BBB200", "nombre": "Segunda", "creditos": 5, "cuatrimestre": 2, "prerrequisitos": ["AAA100"] }
  ]
}
```

## Convertir desde el CSV

El CSV de siempre (`codigo,nombre,creditos,cuatrimestre,prerrequisitos,es_electiva`) se convierte con
`PensumConversor.DesdeCsv`, que devuelve el pénsum ya validado. El archivo de UNAPEC se regenera con:

```powershell
$env:GENERAR_PENSUMS = "1"; dotnet test --filter GenerarElArchivoDeEjemplo
```

Una prueba comprueba que `ingenieria-software-11.json` coincide con `docs/pensum_iso_unapec.csv`; si cambias el CSV,
regenera el archivo.
