$ErrorActionPreference='Stop'
$online=Split-Path -Parent $PSScriptRoot
$project=Split-Path -Parent (Split-Path -Parent $online)
$program=Join-Path $online 'Programa'
$version='1.0.20'
$source=Join-Path $program 'Cyberpunk Coop.exe'
$destination=Join-Path $project ('Cyberpunk Coop '+$version+'.exe')
$assembly=[Reflection.Assembly]::Load([IO.File]::ReadAllBytes($source))
$resources=@($assembly.GetManifestResourceNames())
if($resources.Count -ne 1 -or $resources[0] -ne 'CyberpunkCoop.catalogo.json'){throw 'Recursos inesperados no executável'}
$stream=$assembly.GetManifestResourceStream('CyberpunkCoop.catalogo.json')
$sha=[Security.Cryptography.SHA256]::Create()
try{$embeddedHash=[BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','').ToLowerInvariant()}finally{$stream.Dispose();$sha.Dispose()}
$catalogHash=(Get-FileHash -LiteralPath (Join-Path $program 'catalogo.json') -Algorithm SHA256).Hash.ToLowerInvariant()
if($embeddedHash -ne $catalogHash){throw 'Catálogo embutido diferente do catálogo aprovado'}
$sourceHash=(Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
Copy-Item -LiteralPath $source -Destination $destination -Force
if((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -ne $sourceHash){throw 'Cópia do executável divergente'}
[ordered]@{Version=$version;Executable=$destination;Size=(Get-Item -LiteralPath $destination).Length;Sha256=$sourceHash;EmbeddedCatalogSha256=$embeddedHash;ExternalFilesRequired=@();ModsIncluded=$false;CredentialsIncluded=$false}|ConvertTo-Json -Depth 4|Set-Content -LiteralPath (Join-Path $online 'pacote-verificado.json') -Encoding UTF8
Get-Content -LiteralPath (Join-Path $online 'pacote-verificado.json')
