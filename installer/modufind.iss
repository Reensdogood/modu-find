#ifndef AppVersion
  #define AppVersion "1.1.1"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\app"
#endif
#ifndef ReleaseDir
  #define ReleaseDir "..\artifacts\release"
#endif

[Setup]
AppId={{49C94EA7-7F80-4BAC-BB30-2E23BA2D5F4A}
AppName=모두찾아줘
AppVersion={#AppVersion}
AppVerName=모두찾아줘 {#AppVersion}
AppPublisher=펀네트
AppPublisherURL=https://github.com/Reensdogood/modu-find
AppSupportURL=https://github.com/Reensdogood/modu-find/issues
AppUpdatesURL=https://github.com/Reensdogood/modu-find/releases/latest
DefaultDirName={localappdata}\Programs\ModuFind
DefaultGroupName=모두찾아줘
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
DisableDirPage=yes
DisableProgramGroupPage=yes
UsePreviousAppDir=yes
UsePreviousTasks=yes
UninstallDisplayName=모두찾아줘
UninstallDisplayIcon={app}\ModuFind.exe
SetupIconFile=..\assets\modufind.ico
OutputDir={#ReleaseDir}
OutputBaseFilename=ModuFind-Setup-{#AppVersion}-win-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
AppMutex=Local\FunnetDocumentFinder-{username}
CloseApplications=yes
RestartApplications=no
SetupLogging=yes
VersionInfoDescription=모두찾아줘 설치 프로그램
VersionInfoProductName=모두찾아줘
VersionInfoProductVersion={#AppVersion}
VersionInfoVersion={#AppVersion}

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "바탕화면에 모두찾아줘 바로가기 만들기"; GroupDescription: "바로가기:"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\모두찾아줘"; Filename: "{app}\ModuFind.exe"; AppUserModelID: "Funnet.ModuFind"; Check: NotTestInstall
Name: "{userdesktop}\모두찾아줘"; Filename: "{app}\ModuFind.exe"; Tasks: desktopicon; AppUserModelID: "Funnet.ModuFind"; Check: NotTestInstall

[Run]
Filename: "{app}\ModuFind.exe"; Description: "모두찾아줘 실행"; Flags: nowait postinstall skipifsilent; Check: NotUpdating
Filename: "{app}\ModuFind.exe"; Flags: nowait; Check: IsUpdating

[Code]
function OpenProcess(DesiredAccess: LongWord; InheritHandle: Boolean; ProcessId: LongWord): THandle;
external 'OpenProcess@kernel32.dll stdcall';
function WaitForSingleObject(Handle: THandle; Milliseconds: LongWord): LongWord;
external 'WaitForSingleObject@kernel32.dll stdcall';
function CloseHandle(Handle: THandle): Boolean;
external 'CloseHandle@kernel32.dll stdcall';

function HasParam(const Name: String): Boolean;
var
  I: Integer;
begin
  Result := False;
  for I := 1 to ParamCount do
    if CompareText(ParamStr(I), Name) = 0 then Result := True;
end;

function NotTestInstall: Boolean;
begin
  Result := not HasParam('/TESTINSTALL');
end;

function IsUpdating: Boolean;
begin
  Result := HasParam('/UPDATE') and NotTestInstall;
end;

function NotUpdating: Boolean;
begin
  Result := not IsUpdating;
end;

function InitializeSetup: Boolean;
var
  ProcessId: LongWord;
  Handle: THandle;
begin
  Result := True;
  ProcessId := StrToIntDef(ExpandConstant('{param:WAITPID|0}'), 0);
  if ProcessId = 0 then Exit;
  Handle := OpenProcess($00100000, False, ProcessId);
  if Handle = 0 then Exit;
  try
    if WaitForSingleObject(Handle, 60000) <> 0 then begin
      MsgBox('실행 중인 모두찾아줘가 종료되지 않았습니다. 프로그램을 닫고 다시 설치해 주세요.', mbError, MB_OK);
      Result := False;
    end;
  finally
    CloseHandle(Handle);
  end;
end;

procedure RemoveLegacyShortcut;
var
  Shell, Shortcut: Variant;
  Link, Target, OldRoot: String;
begin
  // Remove only our old portable launcher, never an unrelated same-named shortcut.
  Link := ExpandConstant('{userdesktop}\문서찾기.lnk');
  if not FileExists(Link) then Exit;
  try
    Shell := CreateOleObject('WScript.Shell');
    Shortcut := Shell.CreateShortcut(Link);
    Target := Shortcut.TargetPath;
    Target := Lowercase(Target);
    OldRoot := Lowercase(ExpandConstant('{localappdata}\Programs\FunnetDocumentFinder\'));
    if (Pos(OldRoot, Target) = 1) and (Lowercase(ExtractFileName(Target)) = 'documentfinder.exe') then
      DeleteFile(Link);
  except
    Log('기존 바로가기 정리를 건너뛰었습니다.');
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssPostInstall) and NotTestInstall then RemoveLegacyShortcut;
end;

// No UninstallDelete section: existing indexes/settings and all original documents survive removal.
