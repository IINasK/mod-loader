#ifndef AppVersion
  #error AppVersion is required
#endif
#ifndef AppFile
  #error AppFile is required
#endif
#ifndef IconFile
  #error IconFile is required
#endif
#ifndef OutputPath
  #error OutputPath is required
#endif

[Setup]
#ifdef FixtureId
AppId=IINasK.ModLoader.Test.{#FixtureId}
AppName=Mod Loader Test {#FixtureId}
UninstallDisplayName=Mod Loader Test {#FixtureId}
DefaultDirName={#FixtureRoot}\App
#else
AppId=IINasK.ModLoader
AppName=Mod Loader
UninstallDisplayName=Mod Loader
DefaultDirName={localappdata}\Programs\Mod Loader
#endif
AppVersion={#AppVersion}
AppPublisher=Nask and Codex
AppPublisherURL=https://github.com/IINasK/mod-loader
AppSupportURL=https://github.com/IINasK/mod-loader/issues
AppUpdatesURL=https://github.com/IINasK/mod-loader/releases/latest
UninstallDisplayIcon={app}\ModLoader.exe
SetupIconFile={#IconFile}
VersionInfoDescription=Mod Loader Setup
OutputDir={#OutputPath}
OutputBaseFilename=Mod-Loader-{#AppVersion}
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
WizardStyle=modern
DisableProgramGroupPage=yes
DisableDirPage=yes
DisableWelcomePage=no
Uninstallable=yes
CreateUninstallRegKey=yes
Compression=lzma2/normal
SolidCompression=yes
RestartApplications=no
CloseApplications=yes
CloseApplicationsFilter=ModLoader.exe

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "pt"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[CustomMessages]
en.DesktopShortcut=Create a desktop shortcut
pt.DesktopShortcut=Criar atalho na area de trabalho
en.StartProgram=Open Mod Loader
pt.StartProgram=Abrir Mod Loader
en.DotNetMissing=Mod Loader requires .NET Framework 4.7.2 or later. Install it from Microsoft, then run this installer again.
pt.DotNetMissing=O Mod Loader precisa do .NET Framework 4.7.2 ou mais recente. Instale-o pelo site da Microsoft e execute este instalador novamente.

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopShortcut}"; Flags: unchecked

[Dirs]
Name: "{code:ConfigDir}"; Flags: uninsneveruninstall
Name: "{code:CacheDir}"; Flags: uninsneveruninstall
Name: "{code:LogsDir}"; Flags: uninsneveruninstall

[Files]
Source: "{#AppFile}"; DestDir: "{app}"; DestName: "ModLoader.exe"; Flags: ignoreversion

[Icons]
#ifdef FixtureId
Name: "{app}\TestShortcut"; Filename: "{app}\ModLoader.exe"; WorkingDir: "{app}"
#else
Name: "{autoprograms}\Mod Loader"; Filename: "{app}\ModLoader.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\Mod Loader"; Filename: "{app}\ModLoader.exe"; WorkingDir: "{app}"; Tasks: desktopicon
#endif

#ifndef FixtureId
[Run]
Filename: "{app}\ModLoader.exe"; Description: "{cm:StartProgram}"; Flags: nowait postinstall skipifsilent
#endif

[Code]
function ConfigDir(Param: String): String;
var Modern, Legacy: String;
begin
#ifdef FixtureId
  Result := ExpandConstant('{#FixtureRoot}\Data\Config');
#else
  Modern := ExpandConstant('{localappdata}\ModLoader\Config');
  Legacy := ExpandConstant('{localappdata}\ModLoaderGeral');
  if (FileExists(Modern + '\projects.json') or FileExists(Modern + '\lista.json')) or
     not (FileExists(Legacy + '\projects.json') or FileExists(Legacy + '\lista.json')) then
    Result := Modern
  else
    Result := Legacy;
#endif
end;

function CacheDir(Param: String): String;
var Legacy: String;
begin
#ifdef FixtureId
  Result := ExpandConstant('{#FixtureRoot}\Data\Cache');
#else
  Legacy := ExpandConstant('{localappdata}\CyberpunkCoop\Downloads');
  if DirExists(Legacy) then Result := Legacy
  else Result := ExpandConstant('{localappdata}\ModLoader\Cache');
#endif
end;

function LogsDir(Param: String): String;
var Legacy: String;
begin
#ifdef FixtureId
  Result := ExpandConstant('{#FixtureRoot}\Data\Logs');
#else
  Legacy := ExpandConstant('{localappdata}\CyberpunkCoop\Logs');
  if DirExists(Legacy) then Result := Legacy
  else Result := ExpandConstant('{localappdata}\ModLoader\Logs');
#endif
end;

function InitializeSetup(): Boolean;
var Release: Cardinal;
begin
  Result := RegQueryDWordValue(HKLM32, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', Release);
  if Result then Result := Release >= 461808;
  if not Result then SuppressibleMsgBox(ExpandConstant('{cm:DotNetMissing}'), mbError, MB_OK, IDOK);
end;
