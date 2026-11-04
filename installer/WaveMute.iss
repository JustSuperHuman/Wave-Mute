#ifndef AppVersion
  #define AppVersion "0.1.1"
#endif
#ifndef PublishDir
  #define PublishDir SourcePath + "..\artifacts\publish\" + AppVersion + "\win-x64"
#endif
#ifndef AppNumericVersion
  #define AppNumericVersion AppVersion
#endif

[Setup]
AppId={{3E396866-779A-49B2-A6F5-0334F19CF260}
AppName=Wave Mute
AppVersion={#AppVersion}
AppPublisher=JustSuperHuman
AppPublisherURL=https://github.com/JustSuperHuman/Wave-Mute
AppSupportURL=https://github.com/JustSuperHuman/Wave-Mute/issues
AppUpdatesURL=https://github.com/JustSuperHuman/Wave-Mute/releases/latest
DefaultDirName={localappdata}\Programs\WaveMute
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir=..\artifacts
OutputBaseFilename=WaveMute-{#AppVersion}-Setup
VersionInfoVersion={#AppNumericVersion}
UninstallDisplayIcon={app}\WaveMute.exe
SetupMutex=Local\WaveMute.Setup
CloseApplications=yes
CloseApplicationsFilter=WaveMute.exe
RestartApplications=no
Compression=lzma2
SolidCompression=yes
WizardStyle=modern

[Tasks]
Name: "startup"; Description: "Start Wave Mute when I sign in to Windows"; GroupDescription: "Startup:"

[Files]
Source: "{#PublishDir}\WaveMute.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\THIRD-PARTY-NOTICES.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{userprograms}\Wave Mute"; Filename: "{app}\WaveMute.exe"; WorkingDir: "{app}"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "WaveMute"; ValueData: """{app}\WaveMute.exe"""; Tasks: startup; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "WaveMute"; Tasks: not startup; Flags: deletevalue

[Run]
Filename: "{app}\WaveMute.exe"; Description: "Launch Wave Mute"; Flags: nowait postinstall skipifsilent runhidden

[Code]
function StopWaveMute(const Executable: String): Boolean;
var
  ResultCode: Integer;
  Attempt: Integer;
begin
  Result := not CheckForMutexes('Local\WaveMute.v1');
  if Result then Exit;

  if not Exec(Executable, '--exit', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then Exit;
  for Attempt := 1 to 100 do
  begin
    if not CheckForMutexes('Local\WaveMute.v1') then
    begin
      Result := True;
      Exit;
    end;
    Sleep(100);
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if CheckForMutexes('Local\WaveMute.v1') then
  begin
    { The bundled command client also handles an already-running portable copy. }
    ExtractTemporaryFile('WaveMute.exe');
    if not StopWaveMute(ExpandConstant('{tmp}\WaveMute.exe')) then
      Result := 'Wave Mute is still running. Exit it from the tray and try again.';
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    if not StopWaveMute(ExpandConstant('{app}\WaveMute.exe')) then
      RaiseException('Wave Mute is still running. Exit it from the tray and try again.');
end;
