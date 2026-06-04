; Inno Setup script for packaging RKSS Scanner Agent as a Windows EXE installer.
; Build with Inno Setup Compiler:
;   ISCC.exe installer\RKSSScannerAgent.iss

#define MyAppName "RKSS Scanner Agent"
#define MyAppVersion "2.0.0"
#define MyAppPublisher "RK Systems & Services"
#define MyServiceName "RKSSScannerAgent"
#define LegacyServiceName "NamaScannerAgent"
#define MyAppExeName "RKSS.ScannerAgent.exe"

[Setup]
AppId={{4F53E4F0-6F7A-4D51-B5B7-9DF2A35B97A2}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
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
Name: "desktopicon"; Description: "Create a desktop shortcut for service status"; GroupDescription: "Additional icons:"; Flags: unchecked

[Dirs]
Name: "{sd}\RKSS\ScannerAgent\Scans"

[Files]
Source: "..\publish\rkss-win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "logs\*"

[Icons]
Name: "{group}\RKSS Scanner Agent Status"; Filename: "{code:GetHealthUrl}"
Name: "{group}\RKSS Scanner Agent Desktop"; Filename: "{app}\{#MyAppExeName}"; Parameters: "--desktop"; WorkingDir: "{app}"
Name: "{commondesktop}\RKSS Scanner Agent Status"; Filename: "{code:GetHealthUrl}"; Tasks: desktopicon
Name: "{commonstartup}\RKSS Scanner Agent Desktop"; Filename: "{app}\{#MyAppExeName}"; Parameters: "--desktop"; WorkingDir: "{app}"

[Registry]
Root: HKCR; Subkey: "rkss-scanner-agent"; ValueType: string; ValueName: ""; ValueData: "URL:RKSS Scanner Agent"; Flags: uninsdeletekey
Root: HKCR; Subkey: "rkss-scanner-agent"; ValueType: string; ValueName: "URL Protocol"; ValueData: ""; Flags: uninsdeletekey
Root: HKCR; Subkey: "rkss-scanner-agent\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" --desktop ""%1"""; Flags: uninsdeletekey

[Run]
Filename: "{sys}\sc.exe"; Parameters: "create {#MyServiceName} binPath= ""{app}\{#MyAppExeName}"" start= auto DisplayName= ""{#MyAppName}"""; Flags: runhidden waituntilterminated; StatusMsg: "Installing scanner agent service..."; BeforeInstall: ConfigureAppSettings
Filename: "{sys}\sc.exe"; Parameters: "description {#MyServiceName} ""Local RKSS DIAS scanner bridge for WIA/TWAIN-compatible desktop scanners."""; Flags: runhidden waituntilterminated
Filename: "{sys}\sc.exe"; Parameters: "start {#MyServiceName}"; Flags: runhidden waituntilterminated; StatusMsg: "Starting scanner agent service..."
Filename: "{app}\{#MyAppExeName}"; Parameters: "--desktop"; WorkingDir: "{app}"; Flags: nowait runasoriginaluser skipifdoesntexist; StatusMsg: "Starting desktop TWAIN scanner helper..."

[UninstallRun]
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
  ConfigPage.Values[2] := 'http://localhost:3000,http://127.0.0.1:3000,http://localhost:5173,http://127.0.0.1:5173,https://rkssdias.test,http://localhost,http://127.0.0.1';
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
    '"AllowedOrigins": [ "http://localhost:3000", "http://127.0.0.1:3000", "http://localhost:5173", "http://127.0.0.1:5173", "https://rkssdias.test", "http://localhost", "http://127.0.0.1" ]',
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

function RemoveExistingInstallFolder(): Boolean;
var
  ExistingPath: String;
  UninstallKey: String;
begin
  Result := True;
  ExistingPath := '';
  UninstallKey := 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{4F53E4F0-6F7A-4D51-B5B7-9DF2A35B97A2}_is1';

  RegQueryStringValue(HKLM, UninstallKey, 'InstallLocation', ExistingPath);
  if Trim(ExistingPath) = '' then
    ExistingPath := ExpandConstant('{app}');

  if DirExists(ExistingPath) then
  begin
    WizardForm.StatusLabel.Caption := 'Removing existing scanner agent files...';
    Result := DelTree(ExistingPath, True, True, True);
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';

  if ExistingInstallCleaned then
    Exit;

  ExistingInstallCleaned := True;
  WizardForm.StatusLabel.Caption := 'Checking for existing scanner agent installation...';

  StopAndDeleteService('{#LegacyServiceName}', 'legacy scanner agent service');
  StopAndDeleteService('{#MyServiceName}', 'existing scanner agent service');
  StopDesktopAgent();

  if not RemoveExistingInstallFolder() then
    Result := 'The existing RKSS Scanner Agent installation could not be fully removed. Close any running agent files and run this installer again as Administrator.';
end;
