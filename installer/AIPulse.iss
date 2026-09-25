#ifndef AppVersion
  #define AppVersion "1.0.5"
#endif

[Setup]
AppId={{A31E4829-D1F8-4BA6-80C1-E0E554683597}
AppName=AI Pulse
AppVersion={#AppVersion}
AppPublisher=AI Pulse
AppCopyright=AI Pulse contributors
DefaultDirName={localappdata}\Programs\AI Pulse
DefaultGroupName=AI Pulse
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible arm64
ArchitecturesInstallIn64BitMode=x64compatible arm64
MinVersion=10.0.17763
OutputDir=..\release
OutputBaseFilename=AI-Pulse-{#AppVersion}-Setup
SetupIconFile=..\assets\icon.ico
UninstallDisplayIcon={app}\AIUsageHub.App.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupLogging=yes
CloseApplications=no
RestartApplications=no
ChangesAssociations=no
InfoBeforeFile=..\docs\DISTRIBUTION.txt

[Tasks]
Name: desktopicon; Description: "Create a desktop shortcut"; Flags: unchecked

[Files]
Source: "..\publish\win-x64\AIUsageHub.App.exe"; DestDir: "{app}"; Check: not IsArm64; Flags: ignoreversion
Source: "..\publish\win-arm64\AIUsageHub.App.exe"; DestDir: "{app}"; Check: IsArm64; Flags: ignoreversion
Source: "..\docs\DISTRIBUTION.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\docs\THIRD-PARTY-NOTICES.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\AI Pulse"; Filename: "{app}\AIUsageHub.App.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\AI Pulse"; Filename: "{app}\AIUsageHub.App.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\AIUsageHub.App.exe"; Description: "Launch AI Pulse"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{app}\AIUsageHub.App.exe"; Parameters: "--uninstall-cleanup"; Flags: runhidden waituntilterminated; RunOnceId: "CleanupIntegration"

[Code]
function OpenEvent(DesiredAccess: LongWord; InheritHandle: Boolean; Name: String): THandle;
  external 'OpenEventW@kernel32.dll stdcall';
function SignalEvent(EventHandle: THandle): Boolean;
  external 'SetEvent@kernel32.dll stdcall';
function CloseKernelHandle(Handle: THandle): Boolean;
  external 'CloseHandle@kernel32.dll stdcall';

function StopPulse(): Boolean;
var
  Handle: THandle;
  Attempt: Integer;
begin
  Result := True;
  if not CheckForMutexes('Local\AIUsageHub.SingleInstance') then exit;
  Handle := OpenEvent(2, False, 'Local\AIUsageHub.Exit');
  if Handle <> 0 then begin
    SignalEvent(Handle);
    CloseKernelHandle(Handle);
  end;
  for Attempt := 1 to 100 do begin
    if not CheckForMutexes('Local\AIUsageHub.SingleInstance') then exit;
    Sleep(100);
  end;
  Result := False;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if not StopPulse() then
    Result := 'Please exit AI Pulse from its tray menu, then retry installation.';
end;

function InitializeUninstall(): Boolean;
begin
  Result := StopPulse();
  if not Result then
    MsgBox('Please exit AI Pulse from its tray menu, then retry uninstalling.', mbError, MB_OK);
end;
