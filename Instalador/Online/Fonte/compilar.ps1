$ErrorActionPreference='Stop'
$out=Join-Path (Split-Path -Parent $PSScriptRoot) 'Programa'
$shared=Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'Fonte'
$compiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$framework=Split-Path -Parent $compiler
$hash=(Get-FileHash -LiteralPath (Join-Path $out 'catalogo.json') -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath (Join-Path $PSScriptRoot 'OnlineIdentity.cs') -Encoding UTF8 -Value ('internal static class OnlineIdentity { internal const string CatalogHash="'+$hash+'"; }')
$extensions=@('OnlineDensityWindowTests.cs','OnlineDensity.cs','OnlineDensityTests.cs','OnlineDeactivate.cs','OnlineDownloads.cs','OnlineImport.cs','OnlineImportTests.cs','OnlineAutoInstall.cs','OnlineAutoInstallTests.cs','OnlineDownloadWatch.cs','OnlineDownloadWatchTests.cs','OnlineBrowserSession.cs','OnlineBrowserSessionTests.cs','OnlineWindowWatchTests.cs','DefaultBrowser.cs','DefaultBrowserTests.cs')|ForEach-Object {Join-Path $PSScriptRoot $_}
$extensions += '/resource:'+(Join-Path $out 'catalogo.json')+',CyberpunkCoop.catalogo.json'
$extensions += Join-Path $PSScriptRoot 'OnlineGameEdition.cs'
$extensions += Join-Path $PSScriptRoot 'OnlineGameEditionTests.cs'
$extensions += Join-Path $PSScriptRoot 'OnlineModDetails.cs'
$extensions += Join-Path $PSScriptRoot 'OnlineModAudit.cs'
$extensions += Join-Path $PSScriptRoot 'OnlineModAuditTests.cs'
$extensions += Join-Path $PSScriptRoot 'OnlineDiagnostics.cs'
$extensions += Join-Path $PSScriptRoot 'OnlineDiagnosticsTests.cs'
$extensions += Join-Path $PSScriptRoot 'OnlineRemoval.cs'
$extensions += Join-Path $PSScriptRoot 'OnlineRemovalTests.cs'
$extensions += Join-Path $PSScriptRoot 'OnlineFaceDownloads.cs'
$extensions += Join-Path $PSScriptRoot 'OnlineFaceDownloadsTests.cs'
$extensions += Join-Path $PSScriptRoot 'OnlineFaceMainList.cs'
$extensions += Join-Path $PSScriptRoot 'OnlineFaceMainListTests.cs'
$extensions += Join-Path $PSScriptRoot 'OnlineFacialSourcePins.cs'
$extensions += Join-Path $PSScriptRoot 'OnlineFaceQueue.cs'
$extensions += Join-Path $PSScriptRoot 'OnlineFaceQueueTests.cs'
$extensions += Join-Path $PSScriptRoot 'OnlineBrowserDownloadProgress.cs'
$extensions += Join-Path $PSScriptRoot 'OnlineBrowserDownloadProgressTests.cs'
$extensions += Join-Path $PSScriptRoot 'OnlineUnifiedQueueTests.cs'
$extensions += Join-Path $PSScriptRoot 'OnlineFacialInstallTests.cs'
$extensions += Join-Path $PSScriptRoot 'OnlineUserDataTests.cs'
$extensions += Join-Path $PSScriptRoot 'OnlineDefaults.cs'
$extensions += Join-Path $PSScriptRoot 'OnlineDefaultsTests.cs'
$extensions += Join-Path $PSScriptRoot 'OnlineDefaultsQueueTests.cs'
& $compiler /nologo /target:winexe /main:OnlineProgram /platform:x64 /optimize+ /codepage:65001 ('/win32manifest:'+(Join-Path $shared 'app.manifest')) ('/out:'+(Join-Path $out 'Cyberpunk Coop.exe')) /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Core.dll /reference:System.Security.dll ('/reference:'+(Join-Path $framework 'System.Web.Extensions.dll')) ('/reference:'+(Join-Path $framework 'System.IO.Compression.dll')) ('/reference:'+(Join-Path $framework 'System.IO.Compression.FileSystem.dll')) (Join-Path $shared 'Installer.cs') (Join-Path $shared 'SelfTests.cs') (Join-Path $shared 'PackageIdentity.cs') (Join-Path $PSScriptRoot 'OnlineIdentity.cs') (Join-Path $PSScriptRoot 'OnlineCore.cs') (Join-Path $PSScriptRoot 'OnlineWindow.cs') (Join-Path $PSScriptRoot 'OnlineTests.cs') @extensions
if($LASTEXITCODE -ne 0){throw 'Compilação falhou'}
& $compiler /nologo /target:exe ('/out:'+(Join-Path (Split-Path -Parent $PSScriptRoot) 'FixtureGame.exe')) (Join-Path $shared 'FixtureGame.cs')
if($LASTEXITCODE -ne 0){throw 'Fixture falhou'}
& $compiler /nologo /target:exe ('/out:'+(Join-Path (Split-Path -Parent $PSScriptRoot) 'FixtureUnsupported.exe')) (Join-Path $PSScriptRoot 'FixtureUnsupported.cs')
if($LASTEXITCODE -ne 0){throw 'Fixture de atualizacao falhou'}
Get-Item -LiteralPath (Join-Path $out 'Cyberpunk Coop.exe')|Select-Object FullName,Length
