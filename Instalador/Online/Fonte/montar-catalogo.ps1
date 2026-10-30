$ErrorActionPreference='Stop'
$project=Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))
$out=Join-Path (Split-Path -Parent $PSScriptRoot) 'Programa'
[void](New-Item -ItemType Directory -Path $out -Force)
Add-Type -AssemblyName System.IO.Compression.FileSystem
$transaction=(Get-Content -LiteralPath (Join-Path $project 'Auditoria/instalacao-local-atual.txt')).Trim()
$packages=[Collections.Generic.List[object]]::new()
$seeds=[Collections.Generic.List[object]]::new()
$deps=Get-Content -LiteralPath (Join-Path $transaction 'dependencias-downloads.json') -Raw|ConvertFrom-Json
function Asset($file,$url,$mod=0,$fid=0) {
 $hash=(Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
 $ext=[IO.Path]::GetExtension($file).ToLowerInvariant()
 $a=[ordered]@{Id='a0';Name=[IO.Path]::GetFileName($file);Sha256=$hash;Size=(Get-Item -LiteralPath $file).Length;Format=if($ext -eq '.zip'){'zip'}elseif($ext -in @('.7z','.rar')){'tar'}else{'raw'};Url=$url;ModId=$mod;FileId=$fid}
 $seeds.Add([ordered]@{Sha256=$hash;Path=$file})
 return $a
}
function Recipes($archive,$rows) {
 $zip=$null
 if([IO.Path]::GetExtension($archive) -eq '.zip'){$zip=[IO.Compression.ZipFile]::OpenRead($archive);$entries=@($zip.Entries|Where-Object Name|ForEach-Object FullName)}
 else{$entries=@(& tar -tf $archive);if($LASTEXITCODE -ne 0){throw 'Arquivo ilegivel'}}
 try{foreach($r in $rows){
  $dest=$r.path.Replace('\','/');$matches=@($entries|Where-Object {$_ -eq $dest -or $_.EndsWith('/'+$dest,[StringComparison]::OrdinalIgnoreCase)})
  if($matches.Count -ne 1){throw "Receita ambigua: $archive -> $dest"}
  [ordered]@{Asset='a0';Entry=$matches[0];Path=$dest;Sha256=$r.installed_sha256.ToLowerInvariant();Size=(Get-Item -LiteralPath $r.source).Length}
 }}finally{if($zip){$zip.Dispose()}}
}
function AddPackage($id,$name,$version,$profile,$assets,$files,$requires,$warning='',$immutable=$false) {
 $packages.Add([ordered]@{Id=$id;Name=$name;Version=$version;Profile=$profile;GameVersion='2.5';RequiresPL=$true;RequiresRedmod=$false;Immutable=$immutable;Dependencies=@($requires);Warning=$warning;Assets=@($assets);Files=@($files)})
}
$sourceDir=Join-Path $project 'Mods'
$fixedNexusIds=@{24377=122680;28640=141441;27625=168442;8557=165425;20489=127699;16300=168398;31319=151587;32203=168720;16952=164435;10395=164033}
$mapping=@{
 '12-Dialogue'=@('Immersive V Dialogue Expanded',24377,'1.1.0','Immersive V Dialogue*',@('CET','Audioware','Codeware','NativeSettings','redscript','TweakXL'))
 '13-StreetVendors'=@('Vanilla Style Street Vendors',28640,'1.0.1','VanillaStyleStreetVendors*',@('ArchiveXL','TweakXL','Codeware','redscript','Audioware','CET','NativeSettings'))
 '14-NightCityAllies'=@('Night City Allies',27625,'1.6.11','Night City Allies*',@('CET','redscript','ArchiveXL','TweakXL','Codeware','InputLoader','ModSettings','Audioware','Utilities2077','NativeSettings'))
 '15-NCPDHotline'=@('Immersive NCPD Hotline',8557,'3.1.11','Immersive NCPD Hotline*',@('CET','redscript'))
 '16-Cyberpsycho'=@('Cyberpsycho Encounters',20489,'0.8.0','CyberpsychoEncounters*',@('CET','redscript','ModSettings','TweakXL','Utilities2077','Codeware'))
 '17-DarkFuture'=@('Dark Future',16300,'3.0.0-for-2.5','Dark Future*',@('ArchiveXL','TweakXL','Codeware','redscript','ModSettings','InputLoader','Audioware','NativeSettings','CET'))
 '18-SliceOfLife'=@('Slice of Life',31319,'2','SliceOfLife*',@('CET','redscript','NativeSettings'))
 '19-ThirdPerson'=@('Immersive Third Person',32203,'2.0.1','Immersive Third Person*',@('CET','NativeSettings','ArchiveXL','TweakXL','Codeware','redscript'))
 '21-EconomyPunk'=@('EconomyPunk Standard',16952,'4.7.2','EconomyPunk*',@('ArchiveXL','TweakXL','redscript'))
 '22-NightCityAlive-NoREDMOD'=@('Night City Alive sem REDmod',10395,'2.3','NCA Standard Density*',@('TweakXL','redscript','CET','Codeware'))
}
$baseNames=@('CET','redscript','ArchiveXL','TweakXL','Codeware','InputLoader','ModSettings','Audioware','Utilities2077')
$baseDeps=@(@('NightCoop'),@('NightCoop'),@('NightCoop'),@('NightCoop'),@('NightCoop'),@('NightCoop'),@('ArchiveXL','redscript'),@('NightCoop'),@('redscript','Codeware'))
$baseArchive=Join-Path $project 'Auditoria/Downloads-verificados/NightCoop-0.1.2-beta-2.5.zip'
$rows=Get-Content -LiteralPath (Join-Path $transaction 'nightcoop-arquivos.json') -Raw|ConvertFrom-Json
foreach($r in $rows){if(-not $r.source){$r|Add-Member -NotePropertyName source -NotePropertyValue (Join-Path (Join-Path $project 'Coop') $r.path) -Force}}
AddPackage 'NightCoop' 'NightCoop + RED4ext 1.31.0' '0.1.2-beta' 'Kit inicial' (Asset $baseArchive 'https://nightcoop.net/dl/0.1.2-beta/NightCoop-0.1.2-beta-2.5.zip') (Recipes $baseArchive $rows) @() 'Base fixa; sincronizacao do conjunto ainda experimental.' $true
for($i=0;$i -lt $deps.Count;$i++){
 $d=$deps[$i];$id=$baseNames[$i];$journal=Get-ChildItem -LiteralPath $transaction -Filter '*-arquivos.json'|Where-Object {$_.Name -match ('^'+('{0:d2}' -f ($i+2))+'-')}
 $rows=Get-Content -LiteralPath $journal.FullName -Raw|ConvertFrom-Json
 # Input Loader regenerates these outputs at startup; preserve existing cache and let the plugin create it.
 if($id -eq 'InputLoader'){$rows=@($rows|Where-Object {$_.path.Replace('\','/') -notin @('r6/cache/inputContexts.xml','r6/cache/inputUserMappings.xml')})}
 $warning=if($id -eq 'InputLoader'){'Cache XML gerado pelo loader é preservado e não usado para identificar versão; os cinco arquivos do componente são conferidos.'}else{''}
 AddPackage $id $id $d.version 'Kit inicial' (Asset $d.archive $d.url) (Recipes $d.archive $rows) $baseDeps[$i] $warning
}
$native=Get-Content -LiteralPath (Join-Path $transaction 'nativeSettings-fonte.json') -Raw|ConvertFrom-Json
$rows=Get-Content -LiteralPath (Join-Path $transaction '11-NativeSettings-arquivos.json') -Raw|ConvertFrom-Json
$assets=@();$recipes=@();$n=0
foreach($r in $rows){$a=Asset $r.source ('https://raw.githubusercontent.com/'+$native.repo+'/'+$native.commit+'/'+$r.path.Substring('bin/x64/plugins/cyber_engine_tweaks/mods/'.Length));$a.Id='a'+$n++;$assets+=$a;$recipes+=[ordered]@{Asset=$a.Id;Entry='';Path=$r.path;Sha256=$r.installed_sha256.ToLowerInvariant();Size=$a.Size}}
AddPackage 'NativeSettings' 'Native Settings UI' ('commit '+$native.commit.Substring(0,7)) 'Kit inicial' $assets $recipes @('CET')
foreach($key in ($mapping.Keys|Sort-Object)){
 $m=$mapping[$key];$archive=@(Get-ChildItem -LiteralPath $sourceDir -File|Where-Object Name -like $m[3]);if($archive.Count -ne 1){throw 'Fonte ambigua '+$key}
 $rows=Get-Content -LiteralPath (Join-Path $transaction ($key+'-arquivos.json')) -Raw|ConvertFrom-Json
 $warn='Abertura local observada; sincronizacao do conjunto nao aprovada.'
 if($key -eq '14-NightCityAllies'){$warn='Avisos de hooks de combate/votacao no NightCoop; possivel interferencia. Conserva frameworks do teste local; lista completa do autor ainda pendente.'}
 $fid=$fixedNexusIds[[int]$m[1]]
 AddPackage $key $m[0] $m[2] 'Kit inicial' (Asset $archive[0].FullName ('https://www.nexusmods.com/cyberpunk2077/mods/'+$m[1]) $m[1] $fid) (Recipes $archive[0].FullName $rows) $m[4] $warn
}
$extra=Get-Content -LiteralPath (Join-Path $project 'Auditoria/catalogo-adicionais-20261028.json') -Raw|ConvertFrom-Json
$dependencyNames=@{ArchiveXL='ArchiveXL';Codeware='Codeware';TweakXL='TweakXL';CET='CET';RED4ext='NightCoop';redscript='redscript';'redscript 0.5.31+'='redscript';'Vehicle Manufacturer Fix'='mod-28744';'Virtual Car Dealer'='mod-4454';'Material and Texture Override Legacy'='mod-5266'}
foreach($x in $extra.packages){
 $id='mod-'+$x.mod_id;if($x.mod_id -eq 16697){$id+='-'+$x.file_id}
 $requires=@($x.dependencies|Where-Object {$_}|ForEach-Object {if(-not $dependencyNames.ContainsKey($_)){throw ('Dependencia sem mapa: '+$_)};$dependencyNames[$_]})
 if($x.mod_id -eq 24076){$requires+=@('CET')}
 $files=@($x.recipe_draft|ForEach-Object {[ordered]@{Asset='a0';Entry=$_.source_path;Path=$_.destination;Sha256=$_.sha256.ToLowerInvariant();Size=$_.bytes}})
 # RAR/7z entry sizes are not in the early ledger; count decoded bytes without extracting to disk.
 foreach($f in $files|Where-Object {$null -eq $_.Size}){
  $psi=[Diagnostics.ProcessStartInfo]::new();$psi.FileName='tar';$psi.UseShellExecute=$false;$psi.RedirectStandardOutput=$true;$psi.RedirectStandardError=$true;$psi.CreateNoWindow=$true
  foreach($arg in @('-xOf',$x.local_path,$f.Entry)){[void]$psi.ArgumentList.Add($arg)}
  $p=[Diagnostics.Process]::Start($psi);$count=0L;$buf=New-Object byte[] 65536
  while(($read=$p.StandardOutput.BaseStream.Read($buf,0,$buf.Length)) -gt 0){$count+=$read};$err=$p.StandardError.ReadToEnd();$p.WaitForExit();if($p.ExitCode -ne 0){throw $err};$p.Dispose();$f.Size=$count
 }
 $name=if($x.mod_id -eq 16697){($x.archive -split '-16697')[0].Trim()}else{$x.name}
 AddPackage $id $name $x.version 'Complementos' (Asset $x.local_path $x.source $x.mod_id $x.file_id) $files $requires 'Experimental: sem teste local de gameplay ou sincronizacao.'
}
$hair=Get-Content -LiteralPath (Join-Path $project 'Auditoria/eclectic-instalacao-atual.txt');$h=Get-Content -LiteralPath (Join-Path $hair 'complemento.json') -Raw|ConvertFrom-Json;$r=Get-Content -LiteralPath (Join-Path $hair 'arquivos.json') -Raw|ConvertFrom-Json
AddPackage 'mod-28570' 'Eclectic Hairstyles' '1.3' 'Complementos' (Asset $h.archive 'https://www.nexusmods.com/cyberpunk2077/mods/28570?tab=files&file_id=149682' 28570 149682) (Recipes $h.archive $r) @('ArchiveXL','TweakXL') 'Abertura local confirmada; sincronizacao ainda pendente.'
$driving=Get-Content -LiteralPath (Join-Path $project 'Auditoria/downloads-pilotagem-20261028.json') -Raw|ConvertFrom-Json
foreach($x in $driving.packages){$id=if($x.name -eq 'Shift'){22340}else{33954};$fid=if($id -eq 22340){168897}else{169724};$requires=if($id -eq 22340){@('CET','Codeware')}else{@('NightCoop','redscript','InputLoader','ModSettings')};$r=@($x.entries|ForEach-Object {[ordered]@{Asset='a0';Entry=$_.path;Path=$_.path;Size=$_.bytes;Sha256=$_.sha256.ToLowerInvariant()}});AddPackage ('mod-'+$id) $x.name $x.version 'Complementos' (Asset $x.file $x.source $id $fid) $r $requires 'Experimental; conferir sobreposicao com as cameras do pacote.'}
$appearance=Get-Content -LiteralPath (Join-Path $project 'Auditoria/downloads-aparencia-20261028.json') -Raw|ConvertFrom-Json
$appearanceNames=@{10989='Netrunner Suit Pt2 - vanilla (FemV)';19187='Phoenicia Hair CCXL (FemV)';20763='Wolf Mullet Hair CCXL (FemV)';24547='Hackclaw Hair CCXL (FemV)';24626='InkCloud Hair CCXL (FemV)';25348='Polaris Hair CCXL (FemV)';25807='Alicia Hair CCXL (FemV)'}
foreach($a in $appearance.packages|Where-Object {$appearanceNames.ContainsKey([int]$_.mod_id)}){
 if(-not $a.sha256 -or -not $a.entries){throw ('Download de aparência não concluído: '+$a.mod_id)}
 $version=if($a.mod_id -eq 10989){'1.2'}elseif($a.mod_id -eq 24547){'1.1'}else{'1.0'}
 $requires=if($a.mod_id -eq 10989){@('ArchiveXL','TweakXL','redscript','CET','mod-5266')}else{@('ArchiveXL')}
 $recipe=@($a.entries|ForEach-Object {[ordered]@{Asset='a0';Entry=$_.path;Path=$_.path;Size=$_.bytes;Sha256=$_.sha256}})
 AddPackage ('mod-'+$a.mod_id) $appearanceNames[[int]$a.mod_id] $version 'Complementos' (Asset $a.file $a.source $a.mod_id $a.file_id) $recipe $requires 'Somente FemV. Candidato experimental; sem validação com 32203/NightCoop. Corpos futuros exigem nova revisão.'
}
foreach($p in $packages){
 $p.BlockedReason=''
 if($p.Id -eq 'mod-10702'){$p.BlockedReason='Falha reproduzida em 2.5: TweakXL rejeita vehDriveModelData clonado de Vehicle.v_sport2_porsche_911turbo_inline0 por tipo incompatível. Nenhuma correção autoral verificada.'}
 if($p.Id -eq 'mod-12669'){$p.BlockedReason='Falha reproduzida em 2.5: bases arch_nemesis/inline ausentes e referências de marchas divergentes no YAML original. Dez erros TweakXL; nenhuma correção autoral verificada.'}
 if($p.Id -eq 'mod-10989'){$p.Warning+=' Aviso reproduzido: o slot OutfitSlots.HandLeft das luvas não foi encontrado. Luvas e gameplay do traje ainda não validados.'}
 $p.ThirdPerson='Não testado'
 $p.ThirdPersonNotes='Não há validação desta combinação com Immersive Third Person 2.0.1 e NightCoop. Hashes e versão não comprovam câmera, animações ou sincronização.'
 $p.ThirdPersonSource=$p.Assets[0].Url
 if($p.Id -eq '19-ThirdPerson'){$p.ThirdPerson='Referência 2.0.1';$p.ThirdPersonNotes='Immersive Third Person 2.0.1 é a câmera de referência. Combinação final e sincronização continuam pendentes.'}
 elseif($p.Id -eq 'mod-28570'){$p.ThirdPerson='TPP declarado';$p.ThirdPersonNotes='Autor declara gameplay FPP/TPP. Não certifica especificamente Immersive Third Person 2.0.1 ou NightCoop; verificar cabeça, acessórios e sombras.'}
 elseif($p.Id -eq 'mod-22340'){$p.ThirdPerson='Risco: câmera';$p.ThirdPersonNotes='Shift altera câmera/FOV. Autor cita conflitos com outras câmeras de primeira pessoa; não foi encontrada confirmação específica para 32203. Testar inicialmente só FOV/tremor veicular, sem sobrepor ajustes de câmera.'}
 elseif($p.Id -like 'mod-16697-*'){$p.ThirdPerson='Teste visual pendente';$p.ThirdPersonNotes='CCXL adiciona opções; autor documenta diferenças de sombras e clipping em poses. Verificar cabeça/cabelo ao girar a câmera, correr e equipar acessórios com 32203. Não confundir com os arquivos REPLACER antigos.'}
 elseif($p.Id -match '^mod-(19187|20763|24547|24626|25348|25807)$'){$p.ThirdPerson='FemV: teste pendente';$p.ThirdPersonNotes='Variante CCXL, não substituidor de penteado. Somente FemV. Testar cabeça, sombras, acessórios e cabelo durante animações 32203; cor CCXL pode afetar Wolf Mullet. Sem validação de visualização remota no coop.'}
 elseif($p.Id -eq 'mod-10989'){$p.ThirdPerson='FemV: teste pendente';$p.ThirdPersonNotes='Variante de corpo padrão, não refit para corpos futuros. Sem suporte específico 32203 confirmado; testar clipping, corrida, assentos e transição FPP/TPP. Para MascV, não selecionar este traje.'}
 elseif($p.Id -match '^mod-(11659|10702|11331|12556|8827|13569|8795|8912|12652|8730|11180|24076|12669|33954)$'){$p.ThirdPerson='Teste ao dirigir';$p.ThirdPersonNotes='Sem certificação específica de Immersive Third Person encontrada. Testar entrada/saída, assento, mãos, câmera e troca FPP/TPP em carros e motos; depois testar anfitrião/convidado.'}
}
$experimentalFile=Join-Path $project 'Auditoria/Zeno-dependencias-20261028/catalogo-experimental.json'
if(Test-Path -LiteralPath $experimentalFile){
 $extension=& (Join-Path $project 'Auditoria/Zeno-dependencias-20261028/montar-extras.ps1') -Project $project -BasePackages $packages.ToArray() -ExtraFile $experimentalFile
 foreach($p in $extension.Packages){$packages.Add($p)}
 foreach($seed in $extension.Seeds){$seeds.Add($seed)}
}
$density=& (Join-Path $PSScriptRoot 'montar-densidades.ps1') -Project $project -BasePackages $packages.ToArray()
($packages|Where-Object {$_.Id -eq '22-NightCityAlive-NoREDMOD'})['DensityProfiles']=@($density.Profiles)
foreach($seed in $density.Seeds){if(-not @($seeds|Where-Object {$_.Sha256 -eq $seed.Sha256}).Count){$seeds.Add($seed)}}
$cpm=& (Join-Path $project 'Auditoria/CPM-20261029/montar-cpm.ps1') -Project $project -BasePackages $packages.ToArray() -PackageFile (Join-Path $project 'Auditoria/CPM-20261029/cpm-pacote.json') -ApprovedDataSha256 '7ed8ea92b07b704b7cbdc52f56f6a4fd05b7864e28a25f65ac8d8da77b013f1d'
foreach($p in $cpm.Packages){$packages.Add($p)}
foreach($seed in $cpm.Seeds){if(-not @($seeds|Where-Object {$_.Sha256 -eq $seed.Sha256}).Count){$seeds.Add($seed)}}
$catalog=[ordered]@{Schema=1;Id='cp25-fixed-20261028';GameVersion='2.5';GameFileVersion='3.0.5813403';Packages=@($packages.ToArray());Excluded=@('7606: DEPRECATED','10520: explosao documentada na primeira chamada')}
& (Join-Path $project 'Auditoria/Validacao-coop-20261029/aplicar-metadados.ps1') -Packages $catalog.Packages
$catalog|ConvertTo-Json -Depth 12|Set-Content -LiteralPath (Join-Path $out 'catalogo.json') -Encoding UTF8
$seeds.ToArray()|ConvertTo-Json -Depth 4|Set-Content -LiteralPath (Join-Path (Split-Path -Parent $PSScriptRoot) 'sementes-locais.json') -Encoding UTF8
Write-Output ('Catalogo: '+$packages.Count+' componentes; nenhum mod empacotado.')
