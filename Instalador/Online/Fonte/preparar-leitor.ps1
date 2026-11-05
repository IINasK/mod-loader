$ErrorActionPreference='Stop'
$version='26.05'
$online=Split-Path -Parent $PSScriptRoot
$dependency=Join-Path $online ('Leitor\7zip\'+$version)
$specs=@(
    [pscustomobject]@{Name='7z.exe';Size=577536;Sha256='94205cfc054dd3bbeb978905bc619a65b3f04e9e73bb2717841110b6f46d962e'},
    [pscustomobject]@{Name='7z.dll';Size=1908736;Sha256='78bf6b668d75801e3aa180b4ccb16e6d8395fba80dec8a065055bb6f8dfabb82'},
    [pscustomobject]@{Name='License.txt';Size=6031;Sha256='519ac0a4bded9c18ea02e0afb71f663d8c47373bd9facd3ac96a79f51d77765d'},
    [pscustomobject]@{Name='copying.txt';Size=27032;Sha256='1e7e6bae5a5bde32f1ae5a7c37a082d1ab03cf89354f7f936ac40be9e39a6531'},
    [pscustomobject]@{Name='7z2605-src.tar.xz';Size=1565688;Sha256='0b6080b8c3e7a579161d2d495224c0e51c467576cb4c7048b0ab485ef44cc22e'}
)
function Verify-PinnedFile($path,$size,$hash){if(-not(Test-Path -LiteralPath $path) -or (Get-Item -LiteralPath $path).Length -ne $size -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $hash){throw ('Componente do leitor diferente do oficial: '+$path)}}
New-Item -ItemType Directory -Path $dependency -Force|Out-Null
if(@($specs|Where-Object {-not(Test-Path -LiteralPath (Join-Path $dependency $_.Name))}).Count -gt 0 -or -not(Test-Path -LiteralPath (Join-Path $dependency 'copying.txt'))){
    $work=Join-Path $dependency ('obtencao-'+[Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $work|Out-Null
    $downloads=@(
        @{Name='7z2605-x64.exe';Size=1663506;Sha256='fd99e86f57d8f429ef5ed045bf98315af962e2309b0f1ba87a147a44c71647d0'},
        @{Name='7zr.exe';Size=602624;Sha256='9bf582b894dfa665f852d7e9112b02290a1c303c2d95a21fcdc41ffcb03143b7'},
        @{Name='7z2605-src.tar.xz';Size=1565688;Sha256='0b6080b8c3e7a579161d2d495224c0e51c467576cb4c7048b0ab485ef44cc22e'}
    )
    foreach($download in $downloads){$file=Join-Path $work $download.Name;Invoke-WebRequest -Uri ('https://github.com/ip7z/7zip/releases/download/'+$version+'/'+$download.Name) -OutFile $file;Verify-PinnedFile $file $download.Size $download.Sha256}
    $bootstrap=Join-Path $work '7zr.exe';$runtime=Join-Path $work 'runtime'
    & $bootstrap x -y -bd ('-o'+$runtime) (Join-Path $work '7z2605-x64.exe') '7z.exe' '7z.dll' 'License.txt'|Out-Null
    if($LASTEXITCODE -ne 0){throw 'Falha ao preparar leitor oficial'}
    foreach($spec in $specs|Where-Object {$_.Name -in @('7z.exe','7z.dll','License.txt')}){Verify-PinnedFile (Join-Path $runtime $spec.Name) $spec.Size $spec.Sha256;Copy-Item -LiteralPath (Join-Path $runtime $spec.Name) -Destination (Join-Path $dependency $spec.Name) -Force}
    Copy-Item -LiteralPath (Join-Path $work '7z2605-src.tar.xz') -Destination (Join-Path $dependency '7z2605-src.tar.xz') -Force
    $reader=Join-Path $dependency '7z.exe';$sourceWork=Join-Path $work 'source'
    & $reader x -y -bd ('-o'+$sourceWork) (Join-Path $work '7z2605-src.tar.xz')|Out-Null
    if($LASTEXITCODE -ne 0){throw 'Falha ao ler fonte oficial'}
    $tar=Get-ChildItem -LiteralPath $sourceWork -File|Where-Object Extension -eq '.tar'|Select-Object -First 1
    if(-not $tar){throw 'Pacote-fonte oficial inesperado'}
    & $reader e -y -bd ('-o'+$sourceWork) $tar.FullName 'DOC/copying.txt'|Out-Null
    if($LASTEXITCODE -ne 0){throw 'Licenca LGPL ausente no pacote-fonte'}
    Copy-Item -LiteralPath (Join-Path $sourceWork 'copying.txt') -Destination (Join-Path $dependency 'copying.txt') -Force
}
foreach($spec in $specs){Verify-PinnedFile (Join-Path $dependency $spec.Name) $spec.Size $spec.Sha256}
$notice=@'
Cyberpunk Coop Mod Loader - leitor de arquivos

Inclui 7-Zip 26.05 de Igor Pavlov, sem modificacoes, executado como um programa separado.
O leitor e distribuido sob GNU LGPL 2.1 ou posterior, com partes BSD e a restricao unRAR descritas abaixo.
As licencas completas acompanham este executavel. O codigo-fonte correspondente original tambem esta embutido e pode ser salvo na janela Sobre e licencas.
Projeto oficial: https://www.7-zip.org/
Pacote oficial: https://github.com/ip7z/7zip/releases/download/26.05/7z2605-x64.exe
Codigo-fonte correspondente: https://github.com/ip7z/7zip/releases/download/26.05/7z2605-src.tar.xz
Os componentes do leitor nao sao mods e nao sao instalados na pasta do jogo.

'@
$notice+=[IO.File]::ReadAllText((Join-Path $dependency 'License.txt'))
$notice+="`r`n`r`n--- GNU LESSER GENERAL PUBLIC LICENSE ---`r`n`r`n"+[IO.File]::ReadAllText((Join-Path $dependency 'copying.txt'))
[IO.File]::WriteAllText((Join-Path $dependency 'Licenses.txt'),$notice,(New-Object Text.UTF8Encoding($false)))
$licenses=Get-Item -LiteralPath (Join-Path $dependency 'Licenses.txt')
$identity=@"
internal static class ArchiveReaderIdentity {
    internal const string Version="$version";
    internal const string ExeHash="94205cfc054dd3bbeb978905bc619a65b3f04e9e73bb2717841110b6f46d962e";
    internal const long ExeSize=577536;
    internal const string DllHash="78bf6b668d75801e3aa180b4ccb16e6d8395fba80dec8a065055bb6f8dfabb82";
    internal const long DllSize=1908736;
    internal const string SourceHash="0b6080b8c3e7a579161d2d495224c0e51c467576cb4c7048b0ab485ef44cc22e";
    internal const long SourceSize=1565688;
    internal const string LicensesHash="$((Get-FileHash -LiteralPath $licenses.FullName).Hash.ToLowerInvariant())";
    internal const long LicensesSize=$($licenses.Length);
}
"@
[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'ArchiveReaderIdentity.cs'),$identity,(New-Object Text.UTF8Encoding($false)))
