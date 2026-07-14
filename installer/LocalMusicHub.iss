; Inno Setup script for "Local Music Hub"
; Build: scripts\build-installer.ps1  (do not compile from IDE without running that script first)
; Version: Directory.Build.props → scripts\write-version-inc.ps1 → version.inc + ISCC /DAppVersion=

#define AppName        "Local Music Hub"
#define AppShortName   "LocalMusicHub"
#ifexist "version.inc"
  #include "version.inc"
#endif
#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#define AppPublisher   "Local Music Hub"
#define AppExe         "LocalMusicHub.exe"
#define PublishDir     "..\src\LocalMusicHub\bin\Publish\win-x64"

[Setup]
AppId={{B7F2E9D1-4A8C-4B3E-9F01-2C5D8E6A7B90}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppVerName={#AppName} {#AppVersion}
DefaultDirName={localappdata}\Programs\{#AppShortName}
DefaultGroupName={#AppName}
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
MinVersion=10.0.17763
OutputDir=Output
OutputBaseFilename={#AppShortName}-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
SetupIconFile=..\src\LocalMusicHub\app.ico
InfoBeforeFile=legal\notice-before-install.txt
LicenseFile=legal\terms-license.txt
InfoAfterFile=legal\notice-after-install.txt

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{userprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"
Name: "{userdesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent

[Code]
const
  APP_UNINSTALL_SUBKEY = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{B7F2E9D1-4A8C-4B3E-9F01-2C5D8E6A7B90}_is1';
  TERMS_VERSION = '1';
  REG_APP_KEY = 'Software\{#AppShortName}';

function GetExistingDisplayVersion: String;
begin
  Result := '';
  RegQueryStringValue(HKCU, APP_UNINSTALL_SUBKEY, 'DisplayVersion', Result);
  Result := Trim(Result);
end;

function IsExistingInstallLikely: Boolean;
begin
  Result := (GetExistingDisplayVersion <> '') or
    FileExists(ExpandConstant('{localappdata}') + '\Programs\{#AppShortName}\{#AppExe}');
end;

function GetAcceptedTermsVersion: String;
begin
  Result := '';
  if RegQueryStringValue(HKCU, REG_APP_KEY, 'TermsAcceptedVersion', Result) then
    Result := Trim(Result);
end;

procedure SetAcceptedTermsVersion(const Ver: String);
begin
  RegWriteStringValue(HKCU, REG_APP_KEY, 'TermsAcceptedVersion', Ver);
end;

function ShouldSkipLegalPagesOnUpdate: Boolean;
begin
  Result := IsExistingInstallLikely and (CompareText(GetAcceptedTermsVersion, TERMS_VERSION) = 0);
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := False;
  if not ShouldSkipLegalPagesOnUpdate then
    Exit;
  if (PageID = wpLicense) or (PageID = wpInfoBefore) then
    Result := True;
end;

procedure ApplyWizardCaption;
var
  Ev: String;
begin
  Ev := GetExistingDisplayVersion;
  if Ev <> '' then
    WizardForm.Caption := '{#AppName} Setup — update from ' + Ev + ' to {#AppVersion}'
  else if IsExistingInstallLikely then
    WizardForm.Caption := '{#AppName} Setup — reinstall / update to {#AppVersion}'
  else
    WizardForm.Caption := '{#AppName} Setup — {#AppVersion}';
end;

procedure InitializeWizard;
begin
  ApplyWizardCaption;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  ApplyWizardCaption;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    SetAcceptedTermsVersion(TERMS_VERSION);
end;
