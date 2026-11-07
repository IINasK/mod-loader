param(
    [Parameter(Mandatory=$true)][string]$NuGetPackages,
    [string]$Vpk='vpk',
    [string]$OutputPath
)
$ErrorActionPreference='Stop'
$project=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if(!$OutputPath){$OutputPath=Join-Path $project 'T\Velopack'}
$OutputPath=[IO.Path]::GetFullPath($OutputPath)
$source=Join-Path $project 'Instalador\Online\Fonte'
$version=[regex]::Match((Get-Content -LiteralPath (Join-Path $source 'GeneralUpdates.cs') -Raw),'CurrentVersion="([0-9]+\.[0-9]+\.[0-9]+)"').Groups[1].Value
if(!$version){throw 'Versao do aplicativo ausente'}
$app=Join-Path $OutputPath 'App'
$release=Join-Path $OutputPath 'Release'
& (Join-Path $source 'compilar.ps1') -VelopackBuildDirectory $app -NuGetPackages $NuGetPackages
& $Vpk pack --packId IINasK.ModLoader --packVersion $version --packDir $app --mainExe ModLoader.exe --packTitle 'Mod Loader' --packAuthors IINasK --shortcuts StartMenu --runtime win-x64 --icon (Join-Path $source 'Assets\ModLoader.ico') --outputDir $release
if($LASTEXITCODE){throw 'Empacotamento Velopack falhou'}
Copy-Item -LiteralPath (Join-Path $release 'IINasK.ModLoader-win-Setup.exe') -Destination (Join-Path $release 'Mod-Loader-Setup.exe') -Force
Get-FileHash -LiteralPath (Join-Path $release 'Mod-Loader-Setup.exe')
