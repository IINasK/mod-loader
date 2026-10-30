param([Parameter(Mandatory=$true)][string]$Project,[Parameter(Mandatory=$true)][object[]]$BasePackages)
$ErrorActionPreference='Stop'
$dataFile=Join-Path $Project 'Auditoria/Densidade-20261029/perfis-auditados.json'
$approvedHash='41fd60ccb3d7a8ba4fd02274436cc3ac8326e39755cc65a5fb28c2dfea9e33b4'
if((Get-FileHash -LiteralPath $dataFile -Algorithm SHA256).Hash.ToLowerInvariant() -ne $approvedHash){throw 'Dados de densidade divergentes da auditoria aprovada'}
$data=Get-Content -LiteralPath $dataFile -Raw|ConvertFrom-Json
$nca=@($BasePackages|Where-Object {$_.Id -eq '22-NightCityAlive-NoREDMOD'})
if($nca.Count -ne 1 -or $data.Profiles.Count -ne 3){throw 'Night City Alive ou perfis ausentes'}
$expected=@{low=164034;medium=164033;high=164031}
foreach($p in $data.Profiles){
 if(-not $expected.ContainsKey($p.Id) -or $p.Assets.Count -ne 1){throw 'Perfil de densidade desconhecido'}
 $a=$p.Assets[0];$seed=@($data.Seeds|Where-Object {$_.Sha256 -eq $a.Sha256})
 if($a.FileId -ne $expected[$p.Id] -or $a.ModId -ne 10395 -or $seed.Count -ne 1){throw 'Fonte fixa inconsistente'}
 if(-not (Test-Path -LiteralPath $seed[0].Path -PathType Leaf) -or (Get-Item -LiteralPath $seed[0].Path).Length -ne $a.Size -or (Get-FileHash -LiteralPath $seed[0].Path).Hash.ToLowerInvariant() -ne $a.Sha256){throw 'Pacote original ausente ou divergente'}
}
$medium=$data.Profiles|Where-Object Id -eq 'medium'
if($nca[0].Files.Count -ne $medium.Files.Count -or $nca[0].Assets[0].Sha256 -ne $medium.Assets[0].Sha256){throw 'A variante Media precisa preservar exatamente a receita anterior'}
foreach($f in $nca[0].Files){
 $same=@($medium.Files|Where-Object {$_.Path -ceq $f.Path -and $_.Entry -ceq $f.Entry -and $_.Asset -ceq $f.Asset -and $_.Sha256 -ceq $f.Sha256 -and [long]$_.Size -eq [long]$f.Size})
 if($same.Count -ne 1){throw ('Receita Media alterada: '+$f.Path)}
}
[pscustomobject]@{Profiles=$data.Profiles;Seeds=$data.Seeds}
