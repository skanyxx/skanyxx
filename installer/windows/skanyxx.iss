; ─────────────────────────────────────────────────────────────────
; Skanyxx desktop launcher — InnoSetup Installer Script
; Builds a native Windows .exe installer wizard. A fresh install runs in Remote mode (D200): it opens the company's
; Skanyxx; the server is the Helm chart.
;
; Usage (CI):
;   iscc /DAppVersion=%VERSION% installer\windows\skanyxx.iss
; Silent install with the address:
;   Skanyxx-Setup-<version>-windows-x64.exe /VERYSILENT /REMOTEURL=https://skanyxx.example.com
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
; Windows 10 1607, the oldest .NET 10 supports (dotnet/core release-notes/10.0/supported-os.md)
MinVersion=10.0.14393
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
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Parameters: "--urls http://localhost:{#AppPort}"; WorkingDir: "{app}"; Comment: "Open your company's Skanyxx"
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
WelcomeLabel2=This will install [name/ver] on your computer.%n%nSkanyxx is your company's agent workspace. This installs the desktop launcher: http://localhost:{#AppPort} on this PC opens your company's Skanyxx. The Skanyxx server itself is installed with the Helm chart, not here.%n%nClick Next to continue.
FinishedLabel=Setup has finished installing [name] on your computer.%n%nOpen Skanyxx from the Start Menu: http://localhost:{#AppPort} takes you to your company's Skanyxx. Its address is Skanyxx:RemoteUrl in appsettings.json in the install folder; change it there and restart Skanyxx.%n%nUpgraded? Your existing appsettings.json was kept as it was. Without "Mode" it still runs a full Skanyxx with its own data on this PC: a dev console, not your company's workspace (the model and its key are set on the owner's /Model page, the owner account needs Identity:BootstrapToken). Set "Mode": "Remote" and "RemoteUrl" under "Skanyxx" to open the company's Skanyxx instead.

[Code]
var
  AddressPage: TInputQueryWizardPage;
  FreshConfig: Boolean;

function ConfigPath: String;
begin
  Result := ExpandConstant('{app}\appsettings.json');
end;

// "http://localhost", "http://localhost:8080", "http://localhost/..." but not "http://localhost.example.com".
function HasHostPrefix(const Lower, Prefix: String): Boolean;
begin
  Result := (Pos(Prefix, Lower) = 1)
    and ((Length(Lower) = Length(Prefix)) or (Lower[Length(Prefix) + 1] = ':') or (Lower[Length(Prefix) + 1] = '/'));
end;

// The Host's own rule (Skanyxx:RemoteUrl, D201) checked early, and nothing that could break the JSON string it goes
// into: https://, or http:// to a loopback host; no spaces, quotes, backslashes, query, fragment or user info.
function IsValidAddress(const S: String): Boolean;
var
  I: Integer;
  Lower: String;
begin
  Result := S = '';
  if Result then
    exit;
  for I := 1 to Length(S) do
    if (Ord(S[I]) <= 32) or (Ord(S[I]) > 126) or (S[I] = '"') or (S[I] = '\') or (S[I] = '?') or (S[I] = '#') or (S[I] = '@') then
      exit;
  Lower := Lowercase(S);
  Result := ((Pos('https://', Lower) = 1) and (Length(Lower) > Length('https://')))
    or HasHostPrefix(Lower, 'http://localhost') or HasHostPrefix(Lower, 'http://127.0.0.1') or HasHostPrefix(Lower, 'http://[::1]');
end;

function Address: String;
begin
  Result := Trim(AddressPage.Values[0]);
end;

// A silent install never shows the address page, so a bad /REMOTEURL would otherwise surface only after the files
// are copied (ssPostInstall). Refuse it here, before anything is touched; Setup exits with code 1.
function InitializeSetup: Boolean;
var
  Param: String;
begin
  Param := Trim(ExpandConstant('{param:REMOTEURL|}'));
  Result := not WizardSilent or IsValidAddress(Param);
  if not Result then
  begin
    Log('Refusing /REMOTEURL=' + Param + ': not an https:// address (http:// only for localhost).');
    SuppressibleMsgBox('/REMOTEURL must be an https:// address (http:// only for localhost), with no spaces, quotes, ' +
      'backslashes, query (?), fragment (#) or user name - or empty. Nothing was installed.', mbCriticalError, MB_OK, IDOK);
  end;
end;

procedure InitializeWizard;
begin
  WizardForm.WelcomeLabel2.Font.Size := 9;
  AddressPage := CreateInputQueryPage(wpSelectDir,
    'Your company''s Skanyxx',
    'Where does your company run Skanyxx?',
    'This app opens your company''s Skanyxx. Enter its address (ask its owner), e.g. https://skanyxx.example.com.' + #13#10 +
    'You can leave it empty and set Skanyxx:RemoteUrl in appsettings.json in the install folder later.');
  AddressPage.Add('Address:', False);
  AddressPage.Values[0] := ExpandConstant('{param:REMOTEURL|}');
end;

// Only a fresh install writes appsettings.json; an upgrade keeps the existing one untouched (D204).
function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := (PageID = AddressPage.ID) and FileExists(ConfigPath);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := (CurPageID <> AddressPage.ID) or IsValidAddress(Address);
  if not Result then
    MsgBox('Enter an https:// address (http:// only for localhost), with no spaces, query (?), fragment (#) or user name - or leave it empty.',
      mbError, MB_OK);
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Raw: AnsiString;
  Config: String;
begin
  if CurStep = ssInstall then
    FreshConfig := not FileExists(ConfigPath)
  else if (CurStep = ssPostInstall) and FreshConfig then
  begin
    // Same verify-or-remove rule as the macOS and Linux installers: the config is either right or gone, so a
    // reinstall writes it again.
    if not IsValidAddress(Address) or not LoadStringFromFile(ConfigPath, Raw) then
      Config := ''
    else
      Config := String(Raw);
    if (Config = '')
      or (StringChangeEx(Config, '"Mode": "Server"', '"Mode": "Remote"', True) <> 1)
      or (StringChangeEx(Config, '"RemoteUrl": ""', '"RemoteUrl": "' + Address + '"', True) <> 1)
      or not SaveStringToFile(ConfigPath, AnsiString(Config), False) then
    begin
      DeleteFile(ConfigPath);
      RaiseException('Could not write Skanyxx:Mode and Skanyxx:RemoteUrl into ' + ConfigPath +
        ' (an invalid /REMOTEURL, or the template no longer has "Mode": "Server" and "RemoteUrl": "").');
    end;
  end;
end;
