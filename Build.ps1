$ErrorActionPreference='Stop'
Push-Location $PSScriptRoot
try {
 & (Join-Path $PSScriptRoot 'Instalador/Online/Fonte/compilar.ps1')
 if($LASTEXITCODE -ne 0){throw 'Compilacao falhou'}
 & (Join-Path $PSScriptRoot 'Instalador/Online/Fonte/empacotar.ps1')
} finally {Pop-Location}
