; ─────────────────────────────────────────────────────────────────
; Skanyxx SRE Platform — InnoSetup Installer Script
; Builds a native Windows .exe installer wizard
;
; Usage (CI):
;   iscc /DAppVersion=%VERSION% installer\windows\skanyxx.iss
; ─────────────────────────────────────────────────────────────────

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

#define AppName       "Skanyxx"
#define AppPublisher  "Skanyxx"
#define AppURL        "https://github.com/skanyxx/skanyxx"
#define AppExeName    "Skanyxx.Host.exe"
#define AppPort       "5282"
; Source directory must match the dotnet publish output path used in CI
#define SourceDir     "..\..\publish\win-x64"

[Setup]
AppId={{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}
AppUpdatesURL={#AppURL}/releases
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
AllowNoIcons=yes
LicenseFile=
OutputDir=..\..\dist
OutputBaseFilename=Skanyxx-Setup-{#AppVersion}-windows-x64
SetupIconFile=
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
WizardResizable=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#AppExeName}
ChangesEnvironment=no
DisableDirPage=no
DisableProgramGroupPage=yes
; Open browser after install
CloseApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
; All published app files (self-contained .NET executable + modules)
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; Config template — copied as appsettings.json only if it doesn't already exist
Source: "..\..\appsettings.template.json"; DestDir: "{app}"; DestName: "appsettings.json"; Flags: onlyifdoesntexist uninsneveruninstall

[Icons]
; Start Menu
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Parameters: "--urls http://localhost:{#AppPort}"; WorkingDir: "{app}"; Comment: "Start Skanyxx SRE Platform"
Name: "{group}\Open Skanyxx in Browser"; Filename: "{app}\open-browser.cmd"; WorkingDir: "{app}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
; Desktop
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Parameters: "--urls http://localhost:{#AppPort}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
; Write the launch helper batch file
Filename: "{cmd}"; Parameters: "/C echo @echo off > ""{app}\open-browser.cmd"" && echo start http://localhost:{#AppPort} >> ""{app}\open-browser.cmd"""; Flags: runhidden

; Offer to launch after install
Filename: "{app}\{#AppExeName}"; Parameters: "--urls http://localhost:{#AppPort}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent runasoriginaluser

[UninstallDelete]
; Remove files created at runtime (config, database) only if user agrees
; (the files marked uninsneveruninstall above are kept)
Type: dirifempty; Name: "{app}"

[Messages]
WelcomeLabel2=This will install [name/ver] on your computer.%n%nSkanyxx is a modular SRE platform for Kubernetes management, agent orchestration, and monitoring. It runs as a local web application on http://localhost:{#AppPort}.%n%nClick Next to continue.
FinishedLabel=Setup has finished installing [name] on your computer.%n%nBefore starting, open the file below and add your Anthropic API key:%n    {app}\appsettings.json%n%nThe application is now available from the Start Menu.

[Code]
// Warn user if they are upgrading over an existing installation
procedure InitializeWizard;
begin
  WizardForm.WelcomeLabel2.Font.Size := 9;
end;
