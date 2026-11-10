param([string]$VelopackBuildDirectory,[string]$NuGetPackages,[string]$DevelopmentBuildDirectory,[switch]$Dev)
$ErrorActionPreference='Stop'
if($VelopackBuildDirectory -and $DevelopmentBuildDirectory){throw 'Escolha uma edição para compilar.'}
& (Join-Path $PSScriptRoot 'preparar-leitor.ps1')
& (Join-Path $PSScriptRoot 'preparar-logo.ps1')
$out=Join-Path (Split-Path -Parent $PSScriptRoot) 'Programa'
$shared=Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'Fonte'
$compiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$framework=Split-Path -Parent $compiler
$hash=(Get-FileHash -LiteralPath (Join-Path $out 'catalogo.json') -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath (Join-Path $PSScriptRoot 'OnlineIdentity.cs') -Encoding UTF8 -Value ('internal static class OnlineIdentity { internal const string CatalogHash="'+$hash+'"; }')
$extensions=@('OnlineDensityWindowTests.cs','OnlineDensity.cs','OnlineDensityTests.cs','OnlineDeactivate.cs','OnlineDownloads.cs','OnlineImport.cs','OnlineImportTests.cs','OnlineAutoInstall.cs','OnlineAutoInstallTests.cs','OnlineDownloadWatch.cs','OnlineDownloadWatchTests.cs','OnlineBrowserSession.cs','OnlineBrowserSessionTests.cs','OnlineWindowWatchTests.cs','DefaultBrowser.cs','DefaultBrowserTests.cs')|ForEach-Object {Join-Path $PSScriptRoot $_}
$extensions += '/resource:'+(Join-Path $out 'catalogo.json')+',CyberpunkCoop.catalogo.json'
$project=Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))
$extensions += '/resource:'+(Join-Path $project 'Conhecimento\Candidatos\Hyst - corpo opcional.modloader.json')+',ModLoader.Hyst.modloader.json'
$extensions += Join-Path $PSScriptRoot 'OnlineGameEdition.cs'
$extensions += Join-Path $PSScriptRoot 'OnlineGameEditionTests.cs'
$extensions += Join-Path $PSScriptRoot 'OnlineCategories.cs'
$extensions += Join-Path $PSScriptRoot 'OnlineLocalModPackage.cs'
$extensions += Join-Path $PSScriptRoot 'OnlineLocalImportQueue.cs'
$extensions += Join-Path $PSScriptRoot 'OnlineLocalImportTests.cs'
$extensions += Join-Path $PSScriptRoot 'OnlineModDetails.cs'
$extensions += Join-Path $PSScriptRoot 'OnlineModAudit.cs'
$extensions += Join-Path $PSScriptRoot 'OnlineModAuditTests.cs'
$extensions += Join-Path $PSScriptRoot 'OnlineDiagnostics.cs'
$extensions += Join-Path $PSScriptRoot 'OnlineDiagnosticsTests.cs'
$extensions += Join-Path $PSScriptRoot 'HistoricalReceipts.cs'
$extensions += Join-Path $PSScriptRoot 'OnlineRemoval.cs'
$extensions += Join-Path $PSScriptRoot 'InstalledFileOwnership.cs'
$extensions += Join-Path $PSScriptRoot 'OnlineExtraCleanup.cs'
$extensions += Join-Path $PSScriptRoot 'OnlineExtraCleanupTests.cs'
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
$extensions += Join-Path $PSScriptRoot 'EquipmentExOptions.cs'
$extensions += Join-Path $PSScriptRoot 'OnlineFPVCustom.cs'
$extensions += Join-Path $PSScriptRoot 'VehicleCompatibility.cs'
$extensions += Join-Path $PSScriptRoot 'OnlineFileApprovals.cs'
$extensions += Join-Path $PSScriptRoot 'OnlineFileApprovalWindowTests.cs'
$extensions += Join-Path $PSScriptRoot 'OnlineFileApprovalsTests.cs'
$extensions += Join-Path $PSScriptRoot 'OnlineSkipMods.cs'
$extensions += Join-Path $PSScriptRoot 'OnlineSkipModsTests.cs'
$extensions += Join-Path $PSScriptRoot 'ArchiveReaderIdentity.cs'
$extensions += Join-Path $PSScriptRoot 'OnlineArchiveReader.cs'
$extensions += Join-Path $PSScriptRoot 'OnlineArchiveReaderTests.cs'
$extensions += Join-Path $PSScriptRoot 'OnlineArchiveLicenses.cs'
$reader=Join-Path (Split-Path -Parent $PSScriptRoot) 'Leitor\7zip\26.05'
$extensions += '/resource:'+(Join-Path $reader '7z.exe')+',CyberpunkCoop.Reader.7z.exe'
$extensions += '/resource:'+(Join-Path $reader '7z.dll')+',CyberpunkCoop.Reader.7z.dll'
$extensions += '/resource:'+(Join-Path $reader 'Licenses.txt')+',CyberpunkCoop.Reader.Licenses.txt'
$extensions += '/resource:'+(Join-Path $reader '7z2605-src.tar.xz')+',CyberpunkCoop.Reader.Source.tar.xz'
$extensions += Join-Path $PSScriptRoot 'OnlineDefaults.cs'
$extensions += Join-Path $PSScriptRoot 'OnlineDefaultsTests.cs'
$extensions += Join-Path $PSScriptRoot 'OnlineDefaultsQueueTests.cs'
$extensions += Join-Path $PSScriptRoot 'OnlineRemovedPackageTests.cs'
$extensions += Join-Path $PSScriptRoot 'RetiredPackEntries.cs'
$extensions += @('GeneralModLibrary.cs','GeneralInstallRoutes.cs','GeneralModLibraryV2Tests.cs','GeneralModDownload.cs','GeneralModInstaller.cs','GeneralRouteChanges.cs','GeneralModInstallerTests.cs','GeneralModEditor.cs','GeneralArchivePreview.cs','GeneralModAssociation.cs','DisabledMods.cs','GeneralModActions.cs','GeneralModWindow.cs','GeneralModRun.cs','GeneralModVerification.cs','GeneralModVerificationTests.cs','GeneralModTests.cs','GeneralModMenus.cs','GeneralModProgressTests.cs','GeneralModUiTests.cs','GeneralStyle.cs')|ForEach-Object {Join-Path $PSScriptRoot $_}
$extensions += '/resource:'+(Join-Path $PSScriptRoot 'Assets\Jost.ttf')+',ModLoader.Jost.ttf'
$extensions += Join-Path $PSScriptRoot 'BrowserDownloadBridge.cs'
foreach($file in @('manifest.json','downloads.js')){
    $extensions += '/resource:'+(Join-Path $PSScriptRoot "BrowserExtension\$file")+',ModLoader.Browser.'+$file
}
$extensions += Join-Path $PSScriptRoot 'GeneralButtonPaintTests.cs'
$extensions += @('GeneralFloating.cs','GeneralOptionsDialog.cs','GeneralStatusPresentation.cs')|ForEach-Object {Join-Path $PSScriptRoot $_}
$extensions += '/resource:'+(Join-Path $PSScriptRoot 'Assets\Lucide.ttf')+',ModLoader.Lucide.ttf'
$extensions += '/resource:'+(Join-Path $PSScriptRoot 'Assets\Lucide-LICENSE.txt')+',ModLoader.Lucide-LICENSE.txt'
$extensions += Join-Path $PSScriptRoot 'GeneralFloatingTests.cs'
$extensions += '/resource:'+(Join-Path $PSScriptRoot 'Assets\Jost-OFL.txt')+',ModLoader.Jost-OFL.txt'
$extensions += @('GeneralRedesignTests.cs','GeneralScroll.cs','GeneralActionsMenu.cs','GeneralTableFilters.cs','GeneralTableDialogs.cs','GeneralTableDialogTests.cs','GeneralCategoryDeleteTests.cs','OnlineStorageTests.cs','OnlineStorageMaintenance.cs','OnlineSaveBackups.cs')|ForEach-Object {Join-Path $PSScriptRoot $_}
$extensions += @('GeneralP2PDeferred.cs','GeneralP2PDeferredTests.cs','GeneralP2PAcceptance.cs','GeneralP2PLifecycle.cs','GeneralP2PLifecycleTests.cs','GeneralP2PAutomaticTests.cs')|ForEach-Object {Join-Path $PSScriptRoot $_}
$extensions += @('GeneralP2PPack.cs','GeneralP2PEngine.cs','P2PProcessJob.cs','GeneralP2PService.cs','GeneralSyncReview.cs','GeneralP2PDialog.cs','GeneralP2PTests.cs','GeneralP2PDiagnostics.cs','GeneralP2PDiagnosticDialog.cs','GeneralP2PDiagnosticTests.cs')|ForEach-Object {Join-Path $PSScriptRoot $_}
$extensions += Join-Path $PSScriptRoot 'GeneralP2PUiTests.cs'
$extensions += @('InstalledSnapshot.cs','InstalledSnapshotProject.cs','GeneralPackCoverage.cs','InstalledSnapshotConfig.cs','InstalledSnapshotOwnership.cs','InstalledSnapshotInstaller.cs','InstalledSnapshotTests.cs','InstalledSnapshotInstallerTests.cs','InstalledSnapshotP2PTests.cs')|ForEach-Object {Join-Path $PSScriptRoot $_}
$extensions += @('GeneralUpdates.cs','GeneralUpdatesTests.cs','GeneralPresetIsolationTests.cs')|ForEach-Object {Join-Path $PSScriptRoot $_}
$extensions += @('GeneralBrand.cs','GeneralBrandTests.cs')|ForEach-Object {Join-Path $PSScriptRoot $_}
$extensions += @('GeneralProjectStore.cs','GeneralProjectStoreTests.cs','GeneralProjectHome.cs','GeneralProjectHomeTests.cs')|ForEach-Object {Join-Path $PSScriptRoot $_}
$extensions += @('GeneralMenuHost.cs','GeneralMenuHostTests.cs','GeneralPresetDraft.cs','GeneralPresetDraftTests.cs')|ForEach-Object {Join-Path $PSScriptRoot $_}
$extensions += Join-Path $PSScriptRoot 'GeneralPopupStabilityTests.cs'
$extensions += @('GeneralProjectDeletion.cs','GeneralProjectDeleteDialog.cs','GeneralProjectDeletionTests.cs','GeneralProjectsPanel.cs','GeneralProjectStorage.cs','GeneralNexusInfo.cs','GeneralNexusManifest.cs','NexusManualScan.cs','InventoryLinkStore.cs','GeneralInventoryLinks.cs','InventoryLinkSuggestions.cs','InventoryLinkTests.cs','NexusManualScanTests.cs','GeneralProjectHubTests.cs','GeneralProjectHub.cs','GeneralAppPaths.cs','GeneralAppPathsTests.cs','GeneralStorageTests.cs','DownloadSourceIndex.cs','InstalledModpack.cs','GeneralNoCopyTests.cs','GeneralProjectCloneTests.cs')|ForEach-Object {Join-Path $PSScriptRoot $_}
$extensions += @('ModVersionChoice.cs','ModVersionChoiceTests.cs')|ForEach-Object {Join-Path $PSScriptRoot $_}
$extensions += @('GeneralAutomaticUpdate.cs','GeneralAutomaticUpdateTests.cs')|ForEach-Object {Join-Path $PSScriptRoot $_}
$extensions += @('GeneralP2PSync.cs','NightCoopRelease.cs','NightCoopReleaseTests.cs')|ForEach-Object {Join-Path $PSScriptRoot $_}
$extensions += '/reference:System.Xml.dll'
$extensions += '/win32icon:'+(Join-Path $PSScriptRoot 'Assets\ModLoader.ico')
$extensions += '/resource:'+(Join-Path $PSScriptRoot 'Assets\ModLoader.svg')+',ModLoader.Logo.svg'
$extensions += '/resource:'+(Join-Path $PSScriptRoot 'Assets\ModLoader.ico')+',ModLoader.Logo.ico'
$mainOutput=Join-Path $out 'Cyberpunk Coop.exe'
if($VelopackBuildDirectory){
    $velo=Join-Path $NuGetPackages 'velopack\1.2.161\lib\net472\Velopack.dll'
    $json=Join-Path $NuGetPackages 'newtonsoft.json\13.0.4\lib\net45\Newtonsoft.Json.dll'
    New-Item -ItemType Directory -Path $VelopackBuildDirectory -Force|Out-Null
    foreach($dependency in @($velo,$json)){
        Copy-Item -LiteralPath $dependency -Destination $VelopackBuildDirectory -Force
        $extensions+='/reference:'+$dependency
    }
    $extensions+='/define:VELOPACK'
    $extensions+=Join-Path $PSScriptRoot 'GeneralVelopackUpdate.cs'
    $mainOutput=Join-Path $VelopackBuildDirectory 'ModLoader.exe'
}
if($DevelopmentBuildDirectory){
    New-Item -ItemType Directory -Path $DevelopmentBuildDirectory -Force|Out-Null
    $mainOutput=Join-Path $DevelopmentBuildDirectory 'LoaderDev.exe'
}
if($Dev -and !$VelopackBuildDirectory){throw 'Use -DevelopmentBuildDirectory para compilar o Dev portátil.'}
if($Dev -or $DevelopmentBuildDirectory){
    $extensions+='/define:MODLOADER_DEV'
    if($Dev){$mainOutput=Join-Path $VelopackBuildDirectory 'LoaderDev.exe'}
}
& $compiler /nologo /target:winexe /main:OnlineProgram /platform:x64 /optimize+ /codepage:65001 ('/win32manifest:'+(Join-Path $shared 'app.manifest')) ('/out:'+$mainOutput) /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Core.dll /reference:System.Security.dll ('/reference:'+(Join-Path $framework 'System.Web.Extensions.dll')) ('/reference:'+(Join-Path $framework 'System.IO.Compression.dll')) ('/reference:'+(Join-Path $framework 'System.IO.Compression.FileSystem.dll')) (Join-Path $shared 'Installer.cs') (Join-Path $shared 'SelfTests.cs') (Join-Path $shared 'PackageIdentity.cs') (Join-Path $PSScriptRoot 'OnlineIdentity.cs') (Join-Path $PSScriptRoot 'OnlineCore.cs') (Join-Path $PSScriptRoot 'OnlineWindow.cs') (Join-Path $PSScriptRoot 'OnlineTests.cs') @extensions
if($LASTEXITCODE -ne 0){throw 'Compilação falhou'}
& $compiler /nologo /target:exe ('/out:'+(Join-Path (Split-Path -Parent $PSScriptRoot) 'FixtureGame.exe')) (Join-Path $shared 'FixtureGame.cs')
if($LASTEXITCODE -ne 0){throw 'Fixture falhou'}
& $compiler /nologo /target:exe ('/out:'+(Join-Path (Split-Path -Parent $PSScriptRoot) 'FixtureUnsupported.exe')) (Join-Path $PSScriptRoot 'FixtureUnsupported.cs')
if($LASTEXITCODE -ne 0){throw 'Fixture de atualizacao falhou'}
Get-Item -LiteralPath $mainOutput|Select-Object FullName,Length
