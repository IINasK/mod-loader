param([string]$Compiler)
$ErrorActionPreference='Stop'
Push-Location $PSScriptRoot
try {
 & (Join-Path $PSScriptRoot 'Instalador/Online/Fonte/compilar.ps1')
 if($LASTEXITCODE -ne 0){throw 'Compilacao falhou'}
 & (Join-Path $PSScriptRoot 'Instalador/Online/Fonte/empacotar.ps1')
 & (Join-Path $PSScriptRoot 'Instalador/Setup/compilar-setup.ps1') -Compiler $Compiler
 $setup=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'T/Setup/setup-verificado.json') -Raw|ConvertFrom-Json
 Copy-Item -LiteralPath $setup.Setup -Destination (Join-Path $PSScriptRoot ('Mod Loader Setup '+$setup.Version+'.exe')) -Force
} finally {Pop-Location}
