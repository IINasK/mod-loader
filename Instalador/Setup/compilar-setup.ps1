param([string]$AppFile,[string]$OutputPath,[string]$Compiler,[string]$FixtureRoot,[string]$FixtureId)
$ErrorActionPreference='Stop'
$project=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$source=Join-Path $project 'Instalador\Online\Fonte'
$version=[regex]::Match((Get-Content -LiteralPath (Join-Path $source 'GeneralUpdates.cs') -Raw),'CurrentVersion="([0-9]+\.[0-9]+\.[0-9]+)"').Groups[1].Value
if(-not $version){throw 'Versao do aplicativo ausente'}
if(-not $AppFile){$AppFile=Join-Path $project ('Mod Loader '+$version+'.exe')}
if(-not $OutputPath){$OutputPath=Join-Path $project 'T\Setup'}
if(-not $Compiler){
    $Compiler=@((Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),(Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),(Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'))|Where-Object {Test-Path -LiteralPath $_ -PathType Leaf}|Select-Object -First 1
}
if(-not $Compiler -or -not (Test-Path -LiteralPath $Compiler -PathType Leaf)){throw 'Instale o compilador Inno Setup 6 ou informe -Compiler. Ele so e necessario para compilar, nunca para os amigos usarem o instalador.'}
$AppFile=[IO.Path]::GetFullPath($AppFile);$OutputPath=[IO.Path]::GetFullPath($OutputPath)
if(-not(Test-Path -LiteralPath $AppFile -PathType Leaf)){throw 'Compile e empacote o aplicativo antes do instalador'}
$assembly=[Reflection.AssemblyName]::GetAssemblyName($AppFile)
if($assembly.Version.ToString(3) -ne $version){throw 'A versao do EXE diverge das fontes'}
New-Item -ItemType Directory -Path $OutputPath -Force|Out-Null
$arguments=@('/Q',('/DAppVersion='+$version),('/DAppFile='+$AppFile),('/DIconFile='+(Join-Path $source 'Assets\ModLoader.ico')),('/DOutputPath='+$OutputPath))
if($FixtureRoot -or $FixtureId){
    if($FixtureId -notmatch '^[a-f0-9]{32}$'){throw 'Identificador de teste invalido'}
    $FixtureRoot=[IO.Path]::GetFullPath($FixtureRoot);$allowed=[IO.Path]::GetFullPath((Join-Path $project 'T')).TrimEnd('\')+'\'
    if(-not $FixtureRoot.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase)){throw 'Fixture deve ficar dentro de T no projeto'}
    $ancestor=New-Object IO.DirectoryInfo($FixtureRoot)
    while($ancestor){if($ancestor.Exists -and ($ancestor.Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Fixture nao pode atravessar link/junction'};$ancestor=$ancestor.Parent}
    $arguments+=('/DFixtureId='+$FixtureId);$arguments+=('/DFixtureRoot='+$FixtureRoot)
}
$arguments+=(Join-Path $PSScriptRoot 'mod-loader.iss')
& $Compiler @arguments
if($LASTEXITCODE -ne 0){throw 'Compilacao do instalador falhou'}
$setup=Join-Path $OutputPath ('Mod-Loader-'+$version+'.exe')
[ordered]@{Version=$version;Setup=$setup;Sha256=(Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash;Bytes=(Get-Item -LiteralPath $setup).Length;AppSha256=(Get-FileHash -LiteralPath $AppFile -Algorithm SHA256).Hash;CompilerSha256=(Get-FileHash -LiteralPath $Compiler -Algorithm SHA256).Hash;Fixture=[bool]$FixtureId}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $OutputPath 'setup-verificado.json') -Encoding UTF8
Get-Content -LiteralPath (Join-Path $OutputPath 'setup-verificado.json')
