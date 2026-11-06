param([string]$AppFile,[string]$Compiler)
$ErrorActionPreference='Stop'
$project=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$fixtureId=[Guid]::NewGuid().ToString('N')
$fixture=[IO.Path]::GetFullPath((Join-Path $project ('T\setup-'+$fixtureId)))
$allowed=[IO.Path]::GetFullPath((Join-Path $project 'T')).TrimEnd('\')+'\'
if(-not $fixture.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase)){throw 'Destino de teste fora do projeto'}
$ancestor=New-Object IO.DirectoryInfo($fixture)
while($ancestor){if($ancestor.Exists -and ($ancestor.Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Fixture nao pode atravessar link/junction'};$ancestor=$ancestor.Parent}
New-Item -ItemType Directory -Path $fixture -Force|Out-Null
$env:TEMP=Join-Path $fixture 'Temp';$env:TMP=$env:TEMP
New-Item -ItemType Directory -Path $env:TEMP -Force|Out-Null
$checks=New-Object 'System.Collections.Generic.List[string]'
function Check($ok,[string]$text){if(-not $ok){throw $text};$checks.Add($text)}
$registry=[Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser,[Microsoft.Win32.RegistryView]::Registry64)
$key='Software\Microsoft\Windows\CurrentVersion\Uninstall\IINasK.ModLoader.Test.'+$fixtureId+'_is1'
$prodKey='Software\Microsoft\Windows\CurrentVersion\Uninstall\IINasK.ModLoader_is1'
function ProductionRegistration { $r=$registry.OpenSubKey($prodKey);try{if($r){return [string]$r.GetValue('DisplayVersion')+'|'+[string]$r.GetValue('InstallLocation')}return ''}finally{if($r){$r.Dispose()}} }
$productionBefore=ProductionRegistration
$app=Join-Path $fixture 'App';$exe=Join-Path $app 'ModLoader.exe';$uninstall=Join-Path $app 'unins000.exe'
& (Join-Path $PSScriptRoot 'compilar-setup.ps1') -AppFile $AppFile -Compiler $Compiler -OutputPath (Join-Path $fixture 'Output') -FixtureRoot $fixture -FixtureId $fixtureId|Out-Null
$manifest=Get-Content -LiteralPath (Join-Path $fixture 'Output\setup-verificado.json') -Raw|ConvertFrom-Json
function Install([string]$log){
 $proc=Start-Process -FilePath $manifest.Setup -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/NOCLOSEAPPLICATIONS','/SP-','/LANG=en',('/LOG="'+$log+'"')) -WindowStyle Hidden -Wait -PassThru
 Check ($proc.ExitCode -eq 0) 'Standard installer completed without administrator, reboot or forced application shutdown.'
}
try {
 Install (Join-Path $fixture 'install.log')
 Check ((Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash -eq $manifest.AppSha256) 'Installed application equals the verified payload.'
 Check (Test-Path -LiteralPath $uninstall -PathType Leaf) 'A standalone uninstaller was created.'
 $registration=$registry.OpenSubKey($key)
 try {
  Check ($null -ne $registration) 'The application appears in the current user uninstall registration.'
  Check ($registration.GetValue('DisplayName') -eq ('Mod Loader Test '+$fixtureId)) 'Programs list displays the correct application name.'
  Check ($registration.GetValue('DisplayVersion') -eq $manifest.Version) 'Programs list displays the current version.'
  Check ([IO.Path]::GetFullPath([string]$registration.GetValue('InstallLocation')).TrimEnd('\') -eq $app) 'Registration points to the fixture install directory.'
  Check (([string]$registration.GetValue('UninstallString')).Contains($uninstall)) 'Programs list exposes the installed uninstaller.'
 } finally {if($registration){$registration.Dispose()}}
 foreach($name in @('Config','Cache','Logs')){Check (Test-Path -LiteralPath (Join-Path $fixture ('Data\'+$name)) -PathType Container) ('Dedicated '+$name+' location was created.')}
 $link=Join-Path $app 'TestShortcut.lnk';$shell=New-Object -ComObject WScript.Shell;$shortcut=$shell.CreateShortcut($link)
 Check ($shortcut.TargetPath -eq $exe) 'Installer-created shortcut targets the installed executable.'
 [Runtime.InteropServices.Marshal]::ReleaseComObject($shortcut)|Out-Null;[Runtime.InteropServices.Marshal]::ReleaseComObject($shell)|Out-Null
 $settings=Join-Path $fixture 'Data\Config\keep.json';$cache=Join-Path $fixture 'Data\Cache\keep.cache';$unknown=Join-Path $app 'user-file.txt'
 Set-Content -LiteralPath $settings -Value '{"test":true}' -Encoding utf8;Set-Content -LiteralPath $cache -Value 'cache fixture';Set-Content -LiteralPath $unknown -Value 'user fixture'
 $settingsHash=(Get-FileHash -LiteralPath $settings -Algorithm SHA256).Hash;$cacheHash=(Get-FileHash -LiteralPath $cache -Algorithm SHA256).Hash
 Install (Join-Path $fixture 'upgrade.log')
 Check ((Get-FileHash -LiteralPath $settings -Algorithm SHA256).Hash -eq $settingsHash) 'Reinstall preserves existing preferences.'
 Check ((Get-FileHash -LiteralPath $cache -Algorithm SHA256).Hash -eq $cacheHash) 'Reinstall preserves existing cache.'
 $proc=Start-Process -FilePath $exe -ArgumentList @('--self-test-paths',('"'+(Join-Path $fixture 'Paths')+'"')) -WindowStyle Hidden -Wait -PassThru
 Check ($proc.ExitCode -eq 0) 'Installed executable runs its path-preservation tests.'
 Check ($uninstall.StartsWith($app+'\',[StringComparison]::OrdinalIgnoreCase) -and $app.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase)) 'Uninstall target resolves inside the authorized fixture.'
 $proc=Start-Process -FilePath $uninstall -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/LOG="'+(Join-Path $fixture 'uninstall.log')+'"')) -WindowStyle Hidden -Wait -PassThru
 Check ($proc.ExitCode -eq 0) 'Standard uninstaller completed successfully.'
 $limit=[DateTime]::UtcNow.AddSeconds(20)
 do {Start-Sleep -Milliseconds 100;$left=$registry.OpenSubKey($key);$done=($null -eq $left -and -not(Test-Path -LiteralPath $exe));if($left){$left.Dispose()}}while(-not $done -and [DateTime]::UtcNow -lt $limit)
 Check $done 'Uninstall removes the program entry and installed application.'
 Check ((Get-FileHash -LiteralPath $settings -Algorithm SHA256).Hash -eq $settingsHash) 'Uninstall preserves saved project/configuration data.'
 Check ((Get-FileHash -LiteralPath $cache -Algorithm SHA256).Hash -eq $cacheHash) 'Uninstall preserves cached downloads.'
 Check (Test-Path -LiteralPath $unknown -PathType Leaf) 'Uninstall leaves an untracked user file intact.'
 Check (-not(Test-Path -LiteralPath $link)) 'Uninstall removes its own shortcut.'
 Check ((ProductionRegistration) -eq $productionBefore) 'Fixture does not change the production Mod Loader registration.'
 [ordered]@{Passed=$true;Tests=$checks.ToArray();Fixture=$fixture;TemporaryUninstallRegistrationRemoved=$true;ProductionRegistrationChanged=$false;GameTouched=$false;ProjectsOrCacheDeleted=$false;InstallerVersion=$manifest.Version}|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $fixture 'resultado-setup.json') -Encoding utf8
 Write-Output ('PASS: '+$checks.Count+' setup checks; '+$fixture)
} finally {$registry.Dispose()}
