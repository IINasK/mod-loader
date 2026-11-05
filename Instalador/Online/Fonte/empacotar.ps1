$ErrorActionPreference='Stop'
$online=Split-Path -Parent $PSScriptRoot
$project=Split-Path -Parent (Split-Path -Parent $online)
$program=Join-Path $online 'Programa'
$version='1.1.5'
$source=Join-Path $program 'Cyberpunk Coop.exe'
$destination=Join-Path $project ('Mod Loader '+$version+'.exe')
$assembly=[Reflection.Assembly]::Load([IO.File]::ReadAllBytes($source))
$resources=@($assembly.GetManifestResourceNames())
$reader=Join-Path $online 'Leitor\7zip\26.05'
$expected=@{
 'CyberpunkCoop.catalogo.json'=(Join-Path $program 'catalogo.json')
 'CyberpunkCoop.Reader.7z.exe'=(Join-Path $reader '7z.exe')
 'CyberpunkCoop.Reader.7z.dll'=(Join-Path $reader '7z.dll')
 'CyberpunkCoop.Reader.Licenses.txt'=(Join-Path $reader 'Licenses.txt')
 'CyberpunkCoop.Reader.Source.tar.xz'=(Join-Path $reader '7z2605-src.tar.xz')
 'ModLoader.Jost.ttf'=(Join-Path $PSScriptRoot 'Assets\Jost.ttf')
 'ModLoader.Jost-OFL.txt'=(Join-Path $PSScriptRoot 'Assets\Jost-OFL.txt')
 'ModLoader.Lucide.ttf'=(Join-Path $PSScriptRoot 'Assets\Lucide.ttf')
 'ModLoader.Lucide-LICENSE.txt'=(Join-Path $PSScriptRoot 'Assets\Lucide-LICENSE.txt')
}
if($resources.Count -ne $expected.Count -or @($resources|Where-Object {-not $expected.ContainsKey($_)}).Count -ne 0){throw 'Recursos inesperados no executável'}
foreach($resource in $resources){
    $stream=$assembly.GetManifestResourceStream($resource);$digest=[Security.Cryptography.SHA256]::Create()
    try{$hash=[BitConverter]::ToString($digest.ComputeHash($stream)).Replace('-','').ToLowerInvariant()}finally{$stream.Dispose();$digest.Dispose()}
    if($hash -ne (Get-FileHash -LiteralPath $expected[$resource]).Hash.ToLowerInvariant()){throw ('Recurso embutido divergente: '+$resource)}
}
$stream=$assembly.GetManifestResourceStream('CyberpunkCoop.catalogo.json')
$sha=[Security.Cryptography.SHA256]::Create()
try{$embeddedHash=[BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','').ToLowerInvariant()}finally{$stream.Dispose();$sha.Dispose()}
$catalogHash=(Get-FileHash -LiteralPath (Join-Path $program 'catalogo.json') -Algorithm SHA256).Hash.ToLowerInvariant()
if($embeddedHash -ne $catalogHash){throw 'Catálogo embutido diferente do catálogo aprovado'}
$sourceHash=(Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
Copy-Item -LiteralPath $source -Destination $destination -Force
if((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -ne $sourceHash){throw 'Cópia do executável divergente'}
[ordered]@{Version=$version;Executable=$destination;Size=(Get-Item -LiteralPath $destination).Length;Sha256=$sourceHash;EmbeddedCatalogSha256=$embeddedHash;ExternalFilesRequired=@();ModsIncluded=$false;CredentialsIncluded=$false;ArchiveReader='7-Zip 26.05 x64';ArchiveReaderSourceIncluded=$true;ArchiveReaderLicensesIncluded=$true}|ConvertTo-Json -Depth 4|Set-Content -LiteralPath (Join-Path $online 'pacote-verificado.json') -Encoding UTF8
Get-Content -LiteralPath (Join-Path $online 'pacote-verificado.json')
