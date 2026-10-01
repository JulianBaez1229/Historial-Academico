<#
.SYNOPSIS
  Exporta el codigo a una carpeta NUEVA con un solo commit inicial, sin la historia del repositorio de trabajo.

.DESCRIPTION
  Sirve para publicar el proyecto en GitHub sin arrastrar commits antiguos. Hace esto y nada mas:
    1. Toma los archivos que Git sigue (y los nuevos que no estan ignorados) del repositorio actual.
    2. Revisa esos archivos: si encuentra correos, rutas de usuario de Windows, o cualquiera de los -Terminos que le pases
       (tu nombre, tu matricula...), NO crea nada y te dice donde.
    3. Los copia a la carpeta de destino, que tiene que no existir o estar vacia.
    4. Crea un repositorio Git nuevo en el destino con un unico commit "Version inicial".

  NO sube nada: no agrega ningun remoto ni ejecuta git push. El repositorio original y su historia no se tocan.
  Los -Terminos no se guardan en ningun lado. Pasa tu nombre real, tu matricula y cualquier dato que no deba salir.

.PARAMETER Destino
  Carpeta nueva donde queda el repositorio limpio.

.PARAMETER Terminos
  Palabras o frases que no pueden aparecer en ningun archivo exportado (sin distinguir mayusculas).

.PARAMETER Mensaje
  Mensaje del commit inicial.

.PARAMETER Autor
  Nombre que queda en el commit inicial. Por omisión, el user.name de Git. El commit es publico: el nombre y el correo se ven en GitHub.

.PARAMETER Correo
  Correo que queda en el commit inicial. Por omisión, el user.email de Git. Para no publicar el tuyo, usa el que GitHub te da
  (Settings > Emails > "Keep my email addresses private": 12345+usuario@users.noreply.github.com).

.EXAMPLE
  pwsh scripts/exportar-repositorio-limpio.ps1 -Destino ..\HistorialAcademico-publico -Terminos "Mi Nombre","A00123456"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Destino,
    [string[]]$Terminos = @(),
    [string]$Mensaje = "Version inicial",
    [string]$Autor,
    [string]$Correo
)

$ErrorActionPreference = 'Stop'

# Nunca se exporta esto, aunque Git lo siguiera por error.
$prohibidos = @('^samples/', '^HistorialAcademico\.[^/]+/samples/','(^|/)\.auth/', '\.db($|-)', '(^|/)appsettings\.Development\.json$', '^pensums/[^/]+/personal-.*\.json$', '(^|/)(bin|obj)/', '(^|/)perfiles\.json$')
# Estos archivos no son de texto: no se revisan con las busquedas de texto.
$binarios = '\.(png|jpe?g|gif|ico|woff2?|ttf|eot|otf|pdf|zip|dll|exe|pdb|db|map)$'
# Bibliotecas de terceros: traen correos de sus autores; no son datos de nadie de este proyecto.
$terceros = '^HistorialAcademico\.Web/wwwroot/lib/'

$raiz = (git rev-parse --show-toplevel).Trim()
if (-not $raiz) { throw 'Ejecuta este script dentro del repositorio.' }
Set-Location $raiz

$separador = [System.IO.Path]::DirectorySeparatorChar
$raizNormal = [System.IO.Path]::GetFullPath($raiz).TrimEnd('\', '/')
$destinoCompleto = [System.IO.Path]::GetFullPath($Destino, (Get-Location).Path).TrimEnd('\', '/')
if ($destinoCompleto -eq $raizNormal -or $destinoCompleto.StartsWith($raizNormal + $separador)) {
    throw 'El destino no puede estar dentro del repositorio actual.'
}
if ((Test-Path $destinoCompleto) -and (Get-ChildItem -Force $destinoCompleto | Select-Object -First 1)) {
    throw "El destino ya existe y no esta vacio: $destinoCompleto"
}

# Archivos seguidos por Git + nuevos no ignorados, sin los que se borraron del disco.
$archivos = git ls-files --cached --others --exclude-standard | Sort-Object -Unique | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf }
$archivos = @($archivos | Where-Object { $rel = $_.Replace('\', '/'); -not ($prohibidos | Where-Object { $rel -match $_ }) })
if ($archivos.Count -eq 0) { throw 'No hay archivos para exportar.' }

# --- Revision antes de copiar ---
$hallazgos = New-Object System.Collections.Generic.List[string]
foreach ($rel in $archivos) {
    $ruta = $rel.Replace('\', '/')
    if ($ruta -match $binarios) { continue }
    $contenido = Get-Content -LiteralPath $rel -Raw -Encoding UTF8 -ErrorAction SilentlyContinue
    if ([string]::IsNullOrEmpty($contenido)) { continue }

    foreach ($t in $Terminos) {
        if ($t -and $contenido.IndexOf($t, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) { $hallazgos.Add("${ruta}: contiene '$t'") }
    }
    if ($ruta -match $terceros) { continue }
    # Un correo: parte local que termina en letra o numero, dominio y extension de letras (no versiones como ajv-cli@5.0.0 ni Razor como pin-@p.Id).
    foreach ($m in [regex]::Matches($contenido, '[\w.+-]*[A-Za-z0-9]@[A-Za-z0-9-]+(\.[A-Za-z0-9-]+)*\.[A-Za-z]{2,}\b')) {
        if ($m.Value -notmatch '^(noreply|no-reply)@' -and $m.Value -notmatch '@(example|ejemplo)\.' -and $m.Value -notmatch '\.invalid$' -and $m.Value -notmatch '@users\.noreply\.github\.com$') { $hallazgos.Add("${ruta}: correo '$($m.Value)'") }
    }
    foreach ($m in [regex]::Matches($contenido, '(?i)[A-Z]:\\Users\\[^\\"''\s<>|]+')) {
        if ($m.Value -notmatch '(?i)\\Users\\(Public|Default|<|\{|%|\$|Usuario|User$)') { $hallazgos.Add("${ruta}: ruta de usuario '$($m.Value)'") }
    }
}
if ($hallazgos.Count -gt 0) {
    Write-Host 'Encontre datos que no deberian publicarse. No se creo nada:' -ForegroundColor Red
    $hallazgos | Select-Object -First 50 | ForEach-Object { Write-Host "  - $_" }
    if ($hallazgos.Count -gt 50) { Write-Host "  ... y $($hallazgos.Count - 50) mas" }
    Write-Host 'Corrigelos en el repositorio de trabajo y vuelve a ejecutar el script.'
    exit 1
}

$autor = if ($Autor) { $Autor } else { git config user.name }
$correo = if ($Correo) { $Correo } else { git config user.email }
if (-not $autor -or -not $correo) { throw 'Configura user.name y user.email de Git (o pasa -Autor y -Correo) antes de exportar.' }
Write-Host "El commit inicial saldra a nombre de: $autor <$correo> (sera publico)." -ForegroundColor Yellow

# --- Copia y commit inicial ---
New-Item -ItemType Directory -Force -Path $destinoCompleto | Out-Null
foreach ($rel in $archivos) {
    $destinoArchivo = Join-Path $destinoCompleto $rel
    New-Item -ItemType Directory -Force -Path (Split-Path $destinoArchivo -Parent) | Out-Null
    Copy-Item -LiteralPath $rel -Destination $destinoArchivo
}

Push-Location $destinoCompleto
try {
    git init -q -b main
    git -c core.safecrlf=false add -A 2>$null
    git -c "user.name=$autor" -c "user.email=$correo" commit -q -m $Mensaje
    $total = (git ls-files | Measure-Object).Count
}
finally { Pop-Location }

Write-Host "Listo: $total archivos en $destinoCompleto (un solo commit, sin historia ni remoto)." -ForegroundColor Green
Write-Host 'Siguientes pasos, a mano:'
Write-Host '  1. Revisa la carpeta (git log, git status, git ls-files).'
Write-Host '  2. Crea el repositorio vacio en GitHub.'
Write-Host '  3. git remote add origin <url> ; git push -u origin main'
