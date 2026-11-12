; xFreeRamWindows - Windows installer (Inno Setup 6)
; Developer: Jose Rodriguez Arroyo <jrpcone@gmail.com>
; Build with installer\build-installer.ps1 (publishes the app, then runs ISCC with the version).

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "out\publish"
#endif

#define AppName "xFreeRamWindows"
#define AppExe "xFreeRamWindows.exe"
#define AppPublisher "Jose Rodriguez Arroyo"
#define AppUrl "https://github.com/jorodriguezpr/xFreeRamWindows"

[Setup]
; Never change AppId: upgrades and the uninstaller find the installed copy by it.
AppId={{0EB9370D-7499-4409-AFC3-0C1FCC9A0762}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
VersionInfoVersion={#AppVersion}
VersionInfoCompany={#AppPublisher}
VersionInfoDescription={#AppName} Setup
; Same folder the older install.ps1 used, so upgrading from it just replaces the files.
DefaultDirName={autopf}\{#AppName}
DisableProgramGroupPage=yes
; The app needs administrator rights to trim other processes' memory.
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
LicenseFile=..\LICENSE
SetupIconFile=..\src\xFreeRamWindows\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
OutputDir=out
OutputBaseFilename={#AppName}-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[CustomMessages]
english.StartWithWindows=Start {#AppName} when I sign in to Windows
spanish.StartWithWindows=Iniciar {#AppName} al iniciar sesión en Windows
english.Startup=Startup:
spanish.Startup=Inicio:

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "startup"; Description: "{cm:StartWithWindows}"; GroupDescription: "{cm:Startup}"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; Comment: "Free up RAM on demand and automatically"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
; Same scheduled task the app's own "Start with Windows" menu item creates (task name "xFreeRamWindows"):
; starts elevated at sign-in without a UAC prompt each time.
Filename: "{sys}\schtasks.exe"; Parameters: "/Create /TN ""{#AppName}"" /TR ""\""{app}\{#AppExe}\"""" /SC ONLOGON /RL HIGHEST /F"; Flags: runhidden; Tasks: startup
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/IM {#AppExe} /F"; Flags: runhidden; RunOnceId: "StopApp"
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /TN ""{#AppName}"" /F"; Flags: runhidden; RunOnceId: "RemoveStartupTask"

; Settings in %APPDATA%\xFreeRamWindows are kept on uninstall (same as uninstall.ps1 without -RemoveConfig).

[Code]
// The app holds its files open while it runs: close it before copying the new version.
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM {#AppExe} /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(500);
  Result := '';
end;
