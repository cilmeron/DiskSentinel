; Inno Setup script for DiskSentinel. Build with installer\build.ps1.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef StagingDir
  #define StagingDir "staging"
#endif

[Setup]
AppId={{B6B6C1E4-3D0F-4C2A-9A55-5D3C0A1E7F10}
AppName=DiskSentinel
AppVersion={#AppVersion}
AppVerName=DiskSentinel {#AppVersion}
AppPublisher=DiskSentinel contributors
AppPublisherURL=https://github.com/cilmeron/DiskSentinel
AppSupportURL=https://github.com/cilmeron/DiskSentinel/issues
DefaultDirName={autopf}\DiskSentinel
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\DiskSentinel.exe
OutputDir=Output
OutputBaseFilename=DiskSentinel-Setup-{#AppVersion}
LicenseFile={#StagingDir}\LICENSES.txt
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
CloseApplications=no
SetupLogging=yes

[Tasks]
Name: "autostart"; Description: "Start DiskSentinel automatically when I sign in (recommended)"
Name: "desktopicon"; Description: "Create a &desktop shortcut"; Flags: unchecked
Name: "removehds"; Description: "Uninstall Hard Disk Sentinel once DiskSentinel has proven it can read all drives"; Flags: unchecked; Check: HdsInstalled

[Files]
; The app is self-contained, so no .NET runtime is needed on the PC.
Source: "{#StagingDir}\app\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; smartctl (smartmontools, GPL) is bundled so nothing has to be downloaded at install time.
Source: "{#StagingDir}\smartctl\*"; DestDir: "{app}\smartctl"; Flags: ignoreversion
Source: "{#StagingDir}\LICENSES.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\scripts\register-task.ps1"; DestDir: "{app}\scripts"; Flags: ignoreversion
; Extracted early so the system check can run before anything is installed.
Source: "{#StagingDir}\smartctl\smartctl.exe"; Flags: dontcopy

[InstallDelete]
; Leftovers from older install methods
Type: files; Name: "{app}\*.pdb"

[Icons]
Name: "{autoprograms}\DiskSentinel"; Filename: "{app}\DiskSentinel.exe"; Parameters: "--show"
Name: "{autodesktop}\DiskSentinel"; Filename: "{app}\DiskSentinel.exe"; Parameters: "--show"; Tasks: desktopicon

[Run]
; With autostart the scheduled task starts the app (already elevated, no UAC prompt). Without it, launch directly.
Filename: "{app}\DiskSentinel.exe"; Parameters: "--show"; Description: "Start DiskSentinel now"; Flags: nowait postinstall skipifsilent; Check: not AutostartSelected

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM DiskSentinel.exe"; Flags: runhidden; RunOnceId: "KillApp"
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /TN DiskSentinel /F"; Flags: runhidden; RunOnceId: "DeleteTask"

[Code]
var
  CheckPage: TOutputMsgMemoWizardPage;
  PreflightPassed: Boolean;

function HdsInstalled: Boolean;
begin
  Result := FileExists(ExpandConstant('{commonpf32}\Hard Disk Sentinel\unins000.exe'));
end;

function AutostartSelected: Boolean;
begin
  Result := WizardIsTaskSelected('autostart');
end;

{ Runs a program through cmd so its output can be captured in a string. }
function RunCapture(const Cmd: string; var Output: string): Integer;
var
  TmpFile: string;
  Raw: AnsiString;
  Code: Integer;
begin
  TmpFile := ExpandConstant('{tmp}\capture.txt');
  DeleteFile(TmpFile);
  Output := '';
  { cmd /C strips the first and last quote of its argument, so the whole command is wrapped in an extra pair. }
  if not Exec(ExpandConstant('{cmd}'), '/C "' + Cmd + ' > "' + TmpFile + '" 2>&1"', '', SW_HIDE, ewWaitUntilTerminated, Code) then
    Code := -1;
  if LoadStringFromFile(TmpFile, Raw) then
    Output := String(Raw);
  Result := Code;
end;

function CountLines(const S: string): Integer;
var
  I: Integer;
  Line: string;
begin
  Result := 0;
  Line := '';
  for I := 1 to Length(S) do
  begin
    if S[I] = #10 then
    begin
      if Trim(Line) <> '' then Result := Result + 1;
      Line := '';
    end
    else
      Line := Line + S[I];
  end;
  if Trim(Line) <> '' then Result := Result + 1;
end;

procedure RunPreflight;
var
  Report, Output, Devices: string;
  Code, Found: Integer;
  Tool: string;
begin
  PreflightPassed := True;
  Report := '';

  Report := Report + '[OK]  Windows 10 or later, 64-bit' + #13#10;
  Report := Report + '[OK]  Running with administrator rights' + #13#10;
  Report := Report + '[OK]  No .NET runtime needed (DiskSentinel includes its own)' + #13#10;

  ExtractTemporaryFile('smartctl.exe');
  Tool := ExpandConstant('{tmp}\smartctl.exe');
  Code := RunCapture('"' + Tool + '" --version', Output);
  if (Code = 0) and (Pos('smartctl', Output) > 0) then
    Report := Report + '[OK]  smartctl (drive reader) starts correctly' + #13#10
  else
  begin
    PreflightPassed := False;
    Report := Report + '[FAIL]  smartctl could not start (exit code ' + IntToStr(Code) + ').' + #13#10 +
      '       Security software may be blocking it, or a Windows component is missing.' + #13#10;
  end;

  if PreflightPassed then
  begin
    RunCapture('"' + Tool + '" --scan', Output);
    Found := CountLines(Output);
    Devices := Trim(Output);
    StringChangeEx(Devices, #13#10, #13#10 + '       ', True);
    if Found > 0 then
      Report := Report + '[OK]  ' + IntToStr(Found) + ' storage device(s) detected:' + #13#10 + '       ' +
        Devices + #13#10
    else
    begin
      Report := Report + '[WARN]  No storage devices were detected.' + #13#10 +
        '       DiskSentinel will install, but cannot show anything until a drive is visible.' + #13#10;
    end;
  end;

  if HdsInstalled then
    Report := Report + '[INFO]  Hard Disk Sentinel is installed. You can remove it on the next pages.' + #13#10;

  if PreflightPassed then
    Report := Report + #13#10 + 'Everything needed is in place. Click Next to continue.'
  else
    Report := Report + #13#10 + 'DiskSentinel will not work on this PC until the problem above is fixed.';

  CheckPage.RichEditViewer.Lines.Text := Report;
end;

procedure InitializeWizard;
begin
  CheckPage := CreateOutputMsgMemoPage(wpLicense, 'System check',
    'Checking whether DiskSentinel can run on this PC',
    'The installer looked for everything DiskSentinel needs:', '');
  PreflightPassed := True;
  { In silent mode the page is never shown, so run the check now (a failure aborts the install). }
  if WizardSilent then
    RunPreflight;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = CheckPage.ID then
    RunPreflight;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (CurPageID = CheckPage.ID) and (not PreflightPassed) then
    Result := SuppressibleMsgBox('The system check found a problem, so DiskSentinel probably will not work.' + #13#10#13#10 +
      'Install anyway?', mbConfirmation, MB_YESNO, IDNO) = IDYES;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Code: Integer;
begin
  { Stop a running copy so its files can be replaced. }
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM DiskSentinel.exe', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Result := '';
end;

{ Counts "Temperature   : <digit>" lines in a --dump report = drives that were fully readable. }
function CountReadable(const Dump: string): Integer;
var
  Lines: TArrayOfString;
  I: Integer;
  S: string;
begin
  Result := 0;
  if LoadStringsFromFile(Dump, Lines) then
    for I := 0 to GetArrayLength(Lines) - 1 do
    begin
      S := Lines[I];
      if (Pos('Temperature   : ', S) = 1) and (Length(S) > 16) and (S[17] >= '0') and (S[17] <= '9') then
        Result := Result + 1;
    end;
end;

procedure RemoveHardDiskSentinel;
var
  Code: Integer;
  Uninstaller: string;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM HDSentinel.exe', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Exec('powershell.exe',
    '-NoProfile -ExecutionPolicy Bypass -Command "Get-ScheduledTask | Where-Object { $_.TaskName -like ''*Hard Disk Sentinel*'' } | Unregister-ScheduledTask -Confirm:$false"',
    '', SW_HIDE, ewWaitUntilTerminated, Code);
  Uninstaller := ExpandConstant('{commonpf32}\Hard Disk Sentinel\unins000.exe');
  if FileExists(Uninstaller) then
    Exec(Uninstaller, '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Log('Hard Disk Sentinel uninstaller finished, exit code ' + IntToStr(Code));
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Code, Readable, Expected: Integer;
  Output, Dump: string;
  SelfTestOk: Boolean;
begin
  if CurStep <> ssPostInstall then Exit;

  if AutostartSelected then
  begin
    Exec('powershell.exe',
      '-NoProfile -ExecutionPolicy Bypass -File "' + ExpandConstant('{app}\scripts\register-task.ps1') +
      '" -ExePath "' + ExpandConstant('{app}\DiskSentinel.exe') + '"',
      '', SW_HIDE, ewWaitUntilTerminated, Code);
    Log('register-task.ps1 exit code ' + IntToStr(Code));
    if Code <> 0 then
      SuppressibleMsgBox('DiskSentinel was installed, but the startup task could not be created, so it will not start automatically.',
        mbError, MB_OK, IDOK);
  end;

  { Self-test: can the installed app read the drives on this PC? }
  Dump := ExpandConstant('{tmp}\dump.txt');
  DeleteFile(Dump);
  Exec(ExpandConstant('{app}\DiskSentinel.exe'), '--dump "' + Dump + '"', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Readable := CountReadable(Dump);
  RunCapture('powershell.exe -NoProfile -Command "@(Get-PhysicalDisk).Count"', Output);
  Expected := StrToIntDef(Trim(Output), 0);
  SelfTestOk := (Readable > 0) and (Readable >= Expected);
  Log('Self-test: readable=' + IntToStr(Readable) + ' expected=' + IntToStr(Expected));

  if Readable = 0 then
    SuppressibleMsgBox('DiskSentinel was installed, but it could not read any drive on this PC.' + #13#10#13#10 +
      'Some storage drivers block S.M.A.R.T. access. Check the Log tab in DiskSentinel, or open an issue on GitHub.',
      mbError, MB_OK, IDOK)
  else if not SelfTestOk then
    SuppressibleMsgBox('DiskSentinel can read ' + IntToStr(Readable) + ' of ' + IntToStr(Expected) + ' drives on this PC.' + #13#10#13#10 +
      'Drives it cannot read (for example some USB drives) will not appear.', mbInformation, MB_OK, IDOK);

  if WizardIsTaskSelected('removehds') then
  begin
    if SelfTestOk then
      RemoveHardDiskSentinel
    else
      SuppressibleMsgBox('Hard Disk Sentinel was NOT removed, because DiskSentinel could not read every drive yet.',
        mbInformation, MB_OK, IDOK);
  end;

  if AutostartSelected then
    Exec(ExpandConstant('{sys}\schtasks.exe'), '/Run /TN DiskSentinel', '', SW_HIDE, ewWaitUntilTerminated, Code);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
    if (not UninstallSilent) and
       (MsgBox('Also delete DiskSentinel''s saved temperature history?', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES) then
      DelTree(ExpandConstant('{commonappdata}\DiskSentinel'), True, True, True);
end;
