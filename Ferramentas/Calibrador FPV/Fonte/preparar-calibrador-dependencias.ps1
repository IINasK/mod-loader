param([string]$Destination=(Join-Path (Split-Path -Parent $PSScriptRoot) 'Dependencias'),[string]$Cyberpunk='C:\Program Files (x86)\Steam\steamapps\common\Cyberpunk 2077')
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Path $Destination -Force|Out-Null
function DownloadPinned($url,$path,$hash){if(!(Test-Path -LiteralPath $path)){Invoke-WebRequest -Uri $url -OutFile $path};if((Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant()-ne $hash){throw ('Hash divergente: '+$path)}}
$package=Join-Path $Destination 'vigem.nupkg'
DownloadPinned 'https://api.nuget.org/v3-flatcontainer/nefarius.vigem.client/1.21.256/nefarius.vigem.client.1.21.256.nupkg' $package 'ff460031b33e0882cd1166fe2d072d0fe97578480c61ed85dccfa6c930fdb55e'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive=[IO.Compression.ZipFile]::OpenRead($package)
try{$entry=$archive.GetEntry('lib/netstandard2.0/Nefarius.ViGEm.Client.dll');$memory=[IO.MemoryStream]::new();$stream=$entry.Open();try{$stream.CopyTo($memory)}finally{$stream.Dispose()};$assembly=[Reflection.Assembly]::Load($memory.ToArray());$resource=$assembly.GetManifestResourceStream('costura64.vigemclient.dll');try{$memory=[IO.MemoryStream]::new();$resource.CopyTo($memory);[IO.File]::WriteAllBytes((Join-Path $Destination 'ViGEmClient.dll'),$memory.ToArray())}finally{$resource.Dispose()}}finally{$archive.Dispose()}
if((Get-FileHash -LiteralPath (Join-Path $Destination 'ViGEmClient.dll')).Hash.ToLowerInvariant()-ne '00303f99e1968d523fbd08a7c691d60d145b7335059ec593947841d124c10875'){throw 'ViGEm nativo divergente'}
$sdl=Join-Path $Cyberpunk 'red4ext/plugins/FPVDroneInput/SDL3.dll'
if((Get-FileHash -LiteralPath $sdl).Hash.ToLowerInvariant()-ne 'eb4c055ba3a974ee81414e8b357afa2de35a00336d29b2bd944a460e6dfd00c8'){throw 'SDL3 instalado difere da versão auditada; não atualizar silenciosamente'}
Copy-Item -LiteralPath $sdl -Destination (Join-Path $Destination 'SDL3.dll') -Force
DownloadPinned 'https://raw.githubusercontent.com/nefarius/ViGEmClient/master/LICENSE' (Join-Path $Destination 'ViGEm-LICENSE.txt') '445b3bccd103d39cab7e9b276dd966d60269cf5c44b4b8a118e48bc4c7c4f0db'
DownloadPinned 'https://raw.githubusercontent.com/libsdl-org/SDL/main/LICENSE.txt' (Join-Path $Destination 'SDL-LICENSE.txt') '1c040b8271b37e5076359f8fd54240e371114112924d2df81ef87c7d6a1dfdfd'
Write-Output 'Dependências locais preparadas. Nenhum driver ou mod instalado/atualizado.'
