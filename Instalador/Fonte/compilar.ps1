$ErrorActionPreference='Stop'
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$framework=Split-Path -Parent $compiler
$output=Join-Path (Split-Path -Parent $PSScriptRoot) 'Pacote-para-amigo\Instalar mods.exe'
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 ('/win32manifest:'+(Join-Path $PSScriptRoot 'app.manifest')) ('/out:'+$output) /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Core.dll ('/reference:'+(Join-Path $framework 'System.Web.Extensions.dll')) ('/reference:'+(Join-Path $framework 'System.IO.Compression.dll')) ('/reference:'+(Join-Path $framework 'System.IO.Compression.FileSystem.dll')) (Join-Path $PSScriptRoot 'Installer.cs') (Join-Path $PSScriptRoot 'SelfTests.cs') (Join-Path $PSScriptRoot 'PackageIdentity.cs')
if($LASTEXITCODE -ne 0){throw 'Compilacao falhou'}
Write-Output $output
