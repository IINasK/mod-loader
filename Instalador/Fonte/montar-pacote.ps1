$ErrorActionPreference='Stop'
$root=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$transaction=(Get-Content -Raw -LiteralPath (Join-Path $root 'Auditoria\instalacao-local-atual.txt')).Trim()
$out=Join-Path (Split-Path -Parent $PSScriptRoot) 'Pacote-para-amigo'
New-Item -ItemType Directory -Path (Join-Path $out 'Dados') -Force|Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
$manifest=[ordered]@{Schema=1;Profile='nightcoop-012-cp25-no-redmod-20261028';GameVersion='2.5';RequirePhantomLiberty=$true;RequireRedmod=$false;Files=@()}
$sources=@{}
foreach($journal in Get-ChildItem -LiteralPath $transaction -File -Filter '*-arquivos.json'|Where-Object Name -ne '20-NightCityAlive-arquivos.json'|Sort-Object Name){
  foreach($row in @(Get-Content -Raw -LiteralPath $journal.FullName|ConvertFrom-Json)){
    if($row.state -ne 'instalado'){throw 'Etapa incompleta'}
    $source=if($row.source){$row.source}else{Join-Path (Join-Path $root 'Coop') $row.path}
    if((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne $row.installed_sha256){throw ('Fonte mudou: '+$row.path)}
    if($sources.ContainsKey($row.path)){throw 'Destino duplicado no pacote'}
    $sources[$row.path]=$source
    $manifest.Files+=[ordered]@{Path=$row.path;Sha256=$row.installed_sha256.ToLowerInvariant();Size=(Get-Item -LiteralPath $source).Length;Component=$journal.BaseName.Replace('-arquivos','')}
  }
}
$payload=Join-Path $out 'Dados\conteudo.zip'
if(Test-Path -LiteralPath $payload){throw 'Payload ja existe; conferir antes de remontar'}
$zip=[IO.Compression.ZipFile]::Open($payload,[IO.Compression.ZipArchiveMode]::Create)
try{foreach($entry in $manifest.Files){$null=[IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,$sources[$entry.Path],$entry.Path,[IO.Compression.CompressionLevel]::Optimal)}}finally{$zip.Dispose()}
$manifest|ConvertTo-Json -Depth 7|Set-Content -LiteralPath (Join-Path $out 'Dados\manifesto.json') -Encoding utf8
$payloadHash=(Get-FileHash -LiteralPath $payload).Hash.ToLowerInvariant()
$manifestHash=(Get-FileHash -LiteralPath (Join-Path $out 'Dados\manifesto.json')).Hash.ToLowerInvariant()
$identity='internal static class PackageIdentity { internal const string ManifestHash="'+$manifestHash+'"; internal const string PayloadHash="'+$payloadHash+'"; }'
Set-Content -LiteralPath (Join-Path $PSScriptRoot 'PackageIdentity.cs') -Value $identity -Encoding utf8
$licenses=Join-Path $out 'Licencas-e-fontes'
New-Item -ItemType Directory -Path $licenses -Force|Out-Null
$originals=@(Get-ChildItem -LiteralPath (Join-Path $root 'Mods') -File -Filter '*.zip'|Where-Object Name -notlike '(REDMOD)*')+@(Get-ChildItem -LiteralPath (Join-Path $root 'Auditoria\Downloads-verificados') -File -Filter '*.zip')
$index=@()
foreach($archive in $originals){
 $index+=[ordered]@{Arquivo=$archive.Name;SHA256=(Get-FileHash -LiteralPath $archive.FullName).Hash}
 $z=[IO.Compression.ZipFile]::OpenRead($archive.FullName)
 try{$n=0;foreach($entry in $z.Entries|Where-Object{$_.Name -match '(?i)license|licence|copying|third.party' -and $_.Length -lt 1MB}){$n++;$reader=[IO.StreamReader]::new($entry.Open());try{$text=$reader.ReadToEnd()}finally{$reader.Dispose()};$safe=[regex]::Replace($archive.BaseName,'[^A-Za-z0-9-]','_');Set-Content -LiteralPath (Join-Path $licenses ($safe+'-'+$n+'.txt')) -Value $text -Encoding utf8}}finally{$z.Dispose()}
}
$index|ConvertTo-Json -Depth 4|Set-Content -LiteralPath (Join-Path $licenses 'arquivos-de-origem.json') -Encoding utf8
$readme=@'
COMO INSTALAR
1. Clique com o botão direito no ZIP recebido e escolha Extrair tudo.
2. Abra a pasta extraída e dê dois cliques em Instalar mods.exe.
3. Confira a pasta do jogo e clique em Instalar mods.

O instalador procura o jogo na Steam, GOG, Epic e pastas comuns. Se não encontrar, clique em Escolher pasta e selecione a pasta Cyberpunk 2077, que contém a pasta bin.
O Windows pode pedir permissão para gravar na pasta do jogo. O programa só instala depois do seu clique.
Precisa de Windows 10/11 de 64 bits, Cyberpunk 2077 2.5 e Phantom Liberty. Este perfil NÃO precisa do REDmod.
Não precisa baixar mods, instalar Python, usar terminal ou informar senha dentro do instalador.
Feche o jogo antes. A primeira abertura pode demorar enquanto os scripts compilam.
Para desfazer, abra o mesmo EXE e clique em Desfazer instalação. Arquivos que você tenha alterado depois serão preservados. Backups ficam em .CoopMods dentro da pasta do jogo. Não apague essa pasta se quiser desfazer.

SOBRE ESTE PACOTE
NightCoop 0.1.2-beta + dez mods e suas dependências, nas versões da coleta de 28/10/2026. Night City Alive Standard Density 2.3 sem REDmod.
Os dois jogadores devem usar este mesmo pacote e jogo compatível. Abertura local foi conferida por processo e logs; gameplay completo e sincronização não foram validados.
Há avisos de possível interferência de Night City Allies nas funções de combate/votação do NightCoop. Podem aparecer problemas durante a sessão.
Este arquivo não contém o jogo, DLC, saves, logs pessoais, senhas ou sessão do autor do pacote.
'@
Set-Content -LiteralPath (Join-Path $out 'COMECE AQUI.txt') -Value $readme -Encoding utf8
$permissions=@'
FONTES E PERMISSÕES
Os mods pertencem aos respectivos autores. Este pacote foi montado localmente a pedido do usuário a partir de seus downloads, sem transmitir arquivos a terceiros. Montar o ZIP não concede permissão de redistribuição.
Algumas páginas dos autores restringem redistribuir o mod completo ou enviá-lo a outros sites; Night City Allies exige autorização escrita para redistribuir o conjunto completo. Obtenha as autorizações aplicáveis antes de compartilhar ou publicar o pacote. As licenças encontradas nos arquivos foram preservadas nesta pasta; ausência de licença não significa domínio público.
Alternativa quando não houver autorização: distribuir somente o instalador e obter os arquivos diretamente nas fontes dos autores. Esta edição completa é offline e não implementa login/download do Nexus.

NightCoop: https://nightcoop.net/download.html (0.1.2-beta, jogo 2.5, protocolo 143)
Night City Alive: https://www.nexusmods.com/cyberpunk2077/mods/10395?tab=files&file_id=164033
Cyberpsycho Encounters: https://www.nexusmods.com/cyberpunk2077/mods/20489
Night City Allies: https://www.nexusmods.com/cyberpunk2077/mods/27625
Immersive NCPD Hotline: https://www.nexusmods.com/cyberpunk2077/mods/8557
EconomyPunk: https://www.nexusmods.com/cyberpunk2077/mods/16952
Immersive Third Person: https://www.nexusmods.com/cyberpunk2077/mods/32203
Dark Future: https://www.nexusmods.com/cyberpunk2077/mods/16300
Slice of Life: https://www.nexusmods.com/cyberpunk2077/mods/31319
Immersive V Dialogue Expanded: https://www.nexusmods.com/cyberpunk2077/mods/24377
Vanilla Style Street Vendors: https://www.nexusmods.com/cyberpunk2077/mods/28640

CET 1.38.1: https://github.com/maximegmd/CyberEngineTweaks
redscript 0.5.32: https://github.com/jac3km4/redscript
ArchiveXL 1.28.2: https://github.com/psiberx/cp2077-archive-xl
TweakXL 1.12.0: https://github.com/psiberx/cp2077-tweak-xl
Codeware 1.21.1: https://github.com/psiberx/cp2077-codeware
Input Loader 0.2.6: https://github.com/jackhumbert/cyberpunk2077-input-loader
Mod Settings 0.2.27: https://github.com/jackhumbert/mod_settings
Audioware 1.10.1: https://github.com/cyb3rpsych0s1s/audioware
Utilities 2077 0.7.0: https://github.com/U0047/Utlities2077
Native Settings UI 1.97: https://github.com/justarandomguyintheinternet/CP77_nativeSettings (commit 4065b51456f5e3df8f29e1dc3c07edcc294e68e9)
'@
Set-Content -LiteralPath (Join-Path $licenses 'FONTES-E-PERMISSOES.txt') -Value $permissions -Encoding utf8
[pscustomobject]@{output=$out;files=$manifest.Files.Count;uncompressed_bytes=($manifest.Files|Measure-Object Size -Sum).Sum;zip_bytes=(Get-Item -LiteralPath $payload).Length;manifest_sha256=$manifestHash;payload_sha256=$payloadHash}|ConvertTo-Json
