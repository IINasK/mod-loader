param(
    [Parameter(Mandatory=$true)][string]$NuGetPackages,
    [string]$Vpk='vpk',
    [string]$OutputPath,
    [string]$DevVersion
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
$packId='IINasK.ModLoader'
$exe='ModLoader.exe'
$title='Mod Loader'
$channel='win'
$setup='Mod-Loader-Setup.exe'
$dev=$DevVersion -ne ''
if($dev){
    if($DevVersion -notmatch '^\d+\.\d+\.\d+-dev\.\d+(\.\d+)?$'){throw 'Versão Dev inválida.'}
    $version=$DevVersion
    $packId+='.Dev'
    $exe='LoaderDev.exe'
    $title+=' Dev'
    $channel='dev'
    $setup='Mod-Loader-Dev-Setup.exe'
}
& (Join-Path $source 'compilar.ps1') -VelopackBuildDirectory $app -NuGetPackages $NuGetPackages -Dev:$dev
& $Vpk pack --packId $packId --packVersion $version --packDir $app --mainExe $exe --packTitle $title --packAuthors IINasK --shortcuts StartMenu --runtime win-x64 --channel $channel --icon (Join-Path $source 'Assets\ModLoader.ico') --outputDir $release
if($LASTEXITCODE){throw 'Empacotamento Velopack falhou'}
Copy-Item -LiteralPath (Join-Path $release "$packId-$channel-Setup.exe") -Destination (Join-Path $release $setup) -Force
Get-FileHash -LiteralPath (Join-Path $release $setup)
