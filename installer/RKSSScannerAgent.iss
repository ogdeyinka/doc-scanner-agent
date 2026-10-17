; Inno Setup script for packaging RKSS Scanner Agent as a Windows EXE installer.
; Build with Inno Setup Compiler:
;   ISCC.exe installer\RKSSScannerAgent.iss

#define MyAppName "RKSS Scanner Agent"
#define MyAppVersion "2.1.3"
#define MyAppPublisher "RK Systems & Services"
#define MyServiceName "RKSSScannerAgent"
#define LegacyServiceName "NamaScannerAgent"
#define MyAppExeName "RKSS.ScannerAgent.exe"

[Setup]
AppId={{4F53E4F0-6F7A-4D51-B5B7-9DF2A35B97A2}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
; Smart App Control judges the setup engine (the .tmp it unpacks to %TEMP%) by its exact
; bytes, and the only per-build byte is this version label. Keeping it at 2.1.0 keeps the
; engine identical to one Windows already allows, until the installer is code-signed.
VersionInfoProductTextVersion=2.1.0
AppPublisher={#MyAppPublisher}
DefaultDirName={sd}\RKSS\ScannerAgent\app
DefaultGroupName=RKSS Scanner Agent
DisableProgramGroupPage=yes
DisableDirPage=no
OutputDir=output
OutputBaseFilename=RKSS-Scanner-Agent-Setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=admin
WizardStyle=modern
UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
SetupLogging=yes
#ifdef SIGN_INSTALLER
SignedUninstaller=yes
SignTool=rkss
#endif

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut to the RKSS Scanner Agent Manager"; GroupDescription: "Additional icons:"; Flags: unchecked

[Dirs]
Name: "{sd}\RKSS\ScannerAgent\Scans"
Name: "{app}\logs"

[Files]
Source: "..\publish\rkss-win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "logs\*"

[Icons]
Name: "{group}\RKSS Scanner Agent Manager"; Filename: "{app}\{#MyAppExeName}"; Parameters: "--manage"; WorkingDir: "{app}"
Name: "{group}\Start RKSS Scanner Agent (desktop helper)"; Filename: "{app}\{#MyAppExeName}"; Parameters: "--desktop"; WorkingDir: "{app}"
Name: "{group}\RKSS Scanner Agent Status"; Filename: "{code:GetHealthUrl}"
Name: "{commondesktop}\RKSS Scanner Agent Manager"; Filename: "{app}\{#MyAppExeName}"; Parameters: "--manage"; WorkingDir: "{app}"; Tasks: desktopicon

[InstallDelete]
; Older versions started the helper from the Startup folder; it now uses the Run key below.
Type: files; Name: "{commonstartup}\RKSS Scanner Agent Desktop.lnk"

[Registry]
; The desktop helper starts in the background whenever anyone signs in (switchable in the manager).
Root: HKLM; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "RKSSScannerAgentDesktop"; ValueData: """{app}\{#MyAppExeName}"" --desktop"; Flags: uninsdeletevalue
Root: HKCR; Subkey: "rkss-scanner-agent"; ValueType: string; ValueName: ""; ValueData: "URL:RKSS Scanner Agent"; Flags: uninsdeletekey
Root: HKCR; Subkey: "rkss-scanner-agent"; ValueType: string; ValueName: "URL Protocol"; ValueData: ""; Flags: uninsdeletekey
Root: HKCR; Subkey: "rkss-scanner-agent\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" --desktop ""%1"""; Flags: uninsdeletekey

[Run]
; The service runs as LocalSystem from this folder, so only administrators may change its files.
; Users keep read/run access, plus write access to logs (the desktop helper runs as the user).
; Set on the folders only (no /T): files inherit from them. With /T each file loses its
; inherited permissions and the folder-only grants do not apply to files, leaving them unreadable.
Filename: "{sys}\icacls.exe"; Parameters: """{app}"" /inheritance:r /grant:r *S-1-5-18:(OI)(CI)F *S-1-5-32-544:(OI)(CI)F *S-1-5-32-545:(OI)(CI)RX /C /Q"; Flags: runhidden waituntilterminated; StatusMsg: "Securing the installation folder..."
Filename: "{sys}\icacls.exe"; Parameters: """{app}\logs"" /grant *S-1-5-32-545:(OI)(CI)M /C /Q"; Flags: runhidden waituntilterminated
Filename: "{sys}\sc.exe"; Parameters: "create {#MyServiceName} binPath= ""{app}\{#MyAppExeName}"" start= auto DisplayName= ""{#MyAppName}"""; Flags: runhidden waituntilterminated; StatusMsg: "Installing scanner agent service..."; BeforeInstall: ConfigureAppSettings
; Restart the service by itself if it ever stops unexpectedly.
Filename: "{sys}\sc.exe"; Parameters: "failure {#MyServiceName} reset= 86400 actions= restart/5000/restart/10000/restart/30000"; Flags: runhidden waituntilterminated
Filename: "{sys}\sc.exe"; Parameters: "description {#MyServiceName} ""Local RKSS DIAS scanner bridge for WIA/TWAIN-compatible desktop scanners."""; Flags: runhidden waituntilterminated
Filename: "{sys}\sc.exe"; Parameters: "start {#MyServiceName}"; Flags: runhidden waituntilterminated; StatusMsg: "Starting scanner agent service..."
Filename: "{app}\{#MyAppExeName}"; Parameters: "--desktop"; WorkingDir: "{app}"; Flags: nowait runasoriginaluser skipifdoesntexist; StatusMsg: "Starting desktop TWAIN scanner helper..."
Filename: "{app}\{#MyAppExeName}"; Parameters: "--manage"; WorkingDir: "{app}"; Description: "Open the RKSS Scanner Agent Manager"; Flags: nowait postinstall skipifsilent unchecked runasoriginaluser

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/IM {#MyAppExeName} /F"; Flags: runhidden waituntilterminated; RunOnceId: "StopRKSSScannerAgentDesktop"
Filename: "{sys}\sc.exe"; Parameters: "stop {#MyServiceName}"; Flags: runhidden waituntilterminated; RunOnceId: "StopRKSSScannerAgent"; Check: ServiceExists
Filename: "{sys}\sc.exe"; Parameters: "delete {#MyServiceName}"; Flags: runhidden waituntilterminated; RunOnceId: "DeleteRKSSScannerAgent"; Check: ServiceExists

[Code]
var
  ConfigPage: TInputQueryWizardPage;
  ExistingInstallCleaned: Boolean;

function JsonEscape(Value: String): String;
var
  I: Integer;
  Ch: String;
begin
  Result := '';
  for I := 1 to Length(Value) do
  begin
    Ch := Copy(Value, I, 1);
    if Ch = '\' then
      Result := Result + '\\'
    else if Ch = '"' then
      Result := Result + '\"'
    else
      Result := Result + Ch;
  end;
end;

function BuildAllowedOriginsJson(Value: String): String;
var
  Work: String;
  Origin: String;
  CommaPos: Integer;
  Count: Integer;
begin
  Result := '[';
  Work := Value;
  Count := 0;

  while Length(Trim(Work)) > 0 do
  begin
    CommaPos := Pos(',', Work);
    if CommaPos > 0 then
    begin
      Origin := Trim(Copy(Work, 1, CommaPos - 1));
      Work := Copy(Work, CommaPos + 1, MaxInt);
    end
    else
    begin
      Origin := Trim(Work);
      Work := '';
    end;

    if Origin <> '' then
    begin
      if Count > 0 then
        Result := Result + ', ';
      Result := Result + '"' + JsonEscape(Origin) + '"';
      Count := Count + 1;
    end;
  end;

  Result := Result + ']';
end;

procedure InitializeWizard();
begin
  ConfigPage := CreateInputQueryPage(
    wpSelectDir,
    'Scanner Agent Configuration',
    'Configure the local browser-to-scanner ports and allowed web origins.',
    'The service agent uses WIA on 127.0.0.1:8890. The desktop helper uses TWAIN on 127.0.0.1:8891. Add each allowed web origin separated by commas.'
  );
  ConfigPage.Add('Service WIA Bind URL:', False);
  ConfigPage.Add('Desktop TWAIN Bind URL:', False);
  ConfigPage.Add('Allowed Origins (comma-separated):', False);
  ConfigPage.Values[0] := 'http://127.0.0.1:8890';
  ConfigPage.Values[1] := 'http://127.0.0.1:8891';
  ConfigPage.Values[2] := 'https://dias.nama.gov.ng,http://localhost:3000,http://127.0.0.1:3000';
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if CurPageID = ConfigPage.ID then
  begin
    if Trim(ConfigPage.Values[0]) = '' then
    begin
      MsgBox('Service WIA Bind URL is required. Use http://127.0.0.1:8890 unless that port is already in use.', mbError, MB_OK);
      Result := False;
    end
    else if Pos('http://127.0.0.1:', Trim(ConfigPage.Values[0])) <> 1 then
    begin
      MsgBox('Service WIA Bind URL must start with http://127.0.0.1: for security.', mbError, MB_OK);
      Result := False;
    end
    else if Trim(ConfigPage.Values[1]) = '' then
    begin
      MsgBox('Desktop TWAIN Bind URL is required. Use http://127.0.0.1:8891 unless that port is already in use.', mbError, MB_OK);
      Result := False;
    end
    else if Pos('http://127.0.0.1:', Trim(ConfigPage.Values[1])) <> 1 then
    begin
      MsgBox('Desktop TWAIN Bind URL must start with http://127.0.0.1: for security.', mbError, MB_OK);
      Result := False;
    end
    else if Trim(ConfigPage.Values[2]) = '' then
    begin
      MsgBox('At least one Allowed Origin is required.', mbError, MB_OK);
      Result := False;
    end;
  end;
end;

procedure ConfigureAppSettings();
var
  SettingsPath: String;
  RawContent: AnsiString;
  Content: String;
  BindUrlJson: String;
  DesktopBindUrlJson: String;
  OriginsJson: String;
begin
  SettingsPath := ExpandConstant('{app}\appsettings.json');
  if not LoadStringFromFile(SettingsPath, RawContent) then
    RaiseException('Could not read appsettings.json from ' + SettingsPath);
  Content := RawContent;

  BindUrlJson := '"BindUrl": "' + JsonEscape(ConfigPage.Values[0]) + '"';
  DesktopBindUrlJson := '"DesktopBindUrl": "' + JsonEscape(ConfigPage.Values[1]) + '"';
  OriginsJson := '"AllowedOrigins": ' + BuildAllowedOriginsJson(ConfigPage.Values[2]);

  StringChangeEx(Content, '"BindUrl": "http://127.0.0.1:8890"', BindUrlJson, True);
  StringChangeEx(Content, '"DesktopBindUrl": "http://127.0.0.1:8891"', DesktopBindUrlJson, True);
  StringChangeEx(
    Content,
    '"AllowedOrigins": [ "https://dias.nama.gov.ng", "http://localhost:3000", "http://127.0.0.1:3000" ]',
    OriginsJson,
    True
  );

  if not SaveStringToFile(SettingsPath, Content, False) then
    RaiseException('Could not write appsettings.json to ' + SettingsPath);
end;

function GetHealthUrl(Param: String): String;
begin
  if Assigned(ConfigPage) and (Trim(ConfigPage.Values[0]) <> '') then
    Result := Trim(ConfigPage.Values[0]) + '/health'
  else
    Result := 'http://127.0.0.1:8890/health';
end;

function LegacyServiceExists(): Boolean;
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\sc.exe'), 'query {#LegacyServiceName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := ResultCode = 0;
end;

function ServiceExists(): Boolean;
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\sc.exe'), 'query {#MyServiceName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := ResultCode = 0;
end;

procedure StopAndDeleteService(ServiceName: String; ServiceDisplayName: String);
var
  ResultCode: Integer;
begin
  if Exec(ExpandConstant('{sys}\sc.exe'), 'query ' + ServiceName, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0) then
  begin
    WizardForm.StatusLabel.Caption := 'Stopping ' + ServiceDisplayName + '...';
    Exec(ExpandConstant('{sys}\sc.exe'), 'stop ' + ServiceName, '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Sleep(3000);

    WizardForm.StatusLabel.Caption := 'Removing ' + ServiceDisplayName + '...';
    Exec(ExpandConstant('{sys}\sc.exe'), 'delete ' + ServiceName, '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Sleep(3000);
  end;
end;

procedure StopDesktopAgent();
var
  ResultCode: Integer;
begin
  WizardForm.StatusLabel.Caption := 'Stopping desktop scanner helper...';
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM {#MyAppExeName} /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(1000);
end;

function RemoveExistingInstallFolder(ExistingPath: String): Boolean;
begin
  Result := True;
  if DirExists(ExistingPath) then
  begin
    WizardForm.StatusLabel.Caption := 'Removing leftover scanner agent files...';
    Result := DelTree(ExistingPath, True, True, True);
  end;
end;

const
  UninstallKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{4F53E4F0-6F7A-4D51-B5B7-9DF2A35B97A2}_is1';

function ExistingInstallPath(): String;
begin
  Result := '';
  RegQueryStringValue(HKLM, UninstallKey, 'InstallLocation', Result);
  if Trim(Result) = '' then
    Result := ExpandConstant('{app}');
  Result := RemoveBackslashUnlessRoot(Trim(Result));
end;

// Remove the previous version with its own uninstaller (service, shortcuts, registry, files)
// before installing, instead of copying over it.
procedure RunPreviousUninstaller();
var
  UninstallString: String;
  ResultCode: Integer;
  I: Integer;
begin
  if not RegQueryStringValue(HKLM, UninstallKey, 'UninstallString', UninstallString) then
    Exit;
  UninstallString := RemoveQuotes(Trim(UninstallString));
  if not FileExists(UninstallString) then
    Exit;
  WizardForm.StatusLabel.Caption := 'Uninstalling the previous RKSS Scanner Agent...';
  Exec(UninstallString, '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  // The uninstaller hands over to a copy of itself and returns at once: wait (up to 60 s)
  // until it has removed its own registration.
  for I := 1 to 120 do
  begin
    if not RegKeyExists(HKLM, UninstallKey) then
      Break;
    Sleep(500);
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ExistingPath: String;
begin
  Result := '';

  if ExistingInstallCleaned then
    Exit;

  ExistingInstallCleaned := True;
  WizardForm.StatusLabel.Caption := 'Checking for existing scanner agent installation...';

  StopDesktopAgent();
  StopAndDeleteService('{#LegacyServiceName}', 'legacy scanner agent service');
  StopAndDeleteService('{#MyServiceName}', 'existing scanner agent service');
  ExistingPath := ExistingInstallPath();
  RunPreviousUninstaller();

  // Anything the uninstaller left behind (logs, settings backups) goes too.
  if not RemoveExistingInstallFolder(ExistingPath) then
    Result := 'The existing RKSS Scanner Agent installation could not be fully removed. Close any running agent files and run this installer again as Administrator.';
end;
