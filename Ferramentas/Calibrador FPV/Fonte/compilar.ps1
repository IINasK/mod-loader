param([string]$Root=$PSScriptRoot,[string]$Deps=(Join-Path (Split-Path -Parent $PSScriptRoot) 'Dependencias'),[string]$Output=(Join-Path (Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))) 'Calibrador FPV 0.1.2.exe'))
$ErrorActionPreference='Stop'
$compiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe';$framework=Split-Path -Parent $compiler
$pins=@{'ViGEmClient.dll'='00303f99e1968d523fbd08a7c691d60d145b7335059ec593947841d124c10875';'SDL3.dll'='eb4c055ba3a974ee81414e8b357afa2de35a00336d29b2bd944a460e6dfd00c8'}
foreach($name in $pins.Keys){if((Get-FileHash -LiteralPath (Join-Path $Deps $name)).Hash.ToLowerInvariant()-ne $pins[$name]){throw "Dependency hash mismatch: $name"}}
$resources=@('ViGEmClient.dll','SDL3.dll','ViGEm-LICENSE.txt','SDL-LICENSE.txt')|ForEach-Object {'/resource:'+(Join-Path $Deps $_)+',Calibrator.'+$_}
& $compiler /nologo /target:winexe /main:CalibratorProgram /platform:x64 /optimize+ /langversion:5 /codepage:65001 ('/out:'+$Output) /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Core.dll ('/reference:'+(Join-Path $framework 'System.Web.Extensions.dll')) @resources (Join-Path $Root 'CalibratorApp.cs') (Join-Path $Root 'CalibratorSequence.cs') (Join-Path $Root 'CalibratorGamepad.cs') (Join-Path $Root 'CalibratorCapture.cs')
if($LASTEXITCODE-ne 0){throw 'Calibrador compilation failed'}
Get-Item -LiteralPath $Output|Select-Object FullName,Length
