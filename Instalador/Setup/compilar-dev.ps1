$ErrorActionPreference='Stop'
$project=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$output=Join-Path $project 'T/Dev'
& (Join-Path $project 'Instalador/Online/Fonte/compilar.ps1') -DevelopmentBuildDirectory $output
