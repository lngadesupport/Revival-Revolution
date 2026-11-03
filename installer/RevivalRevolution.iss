#define MyAppName "Revival Revolution Launcher"
#define MyAppVersion "0.7.2"
#define MyAppPublisher "Revival Revolution"
#define PublishDir "..\\artifacts\\RevivalRevolution-win-x64"

[Setup]
AppId={{A98B2B41-CC4B-4F22-AC4A-1A9E7E7B0C72}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={code:GetDefaultGameDir}
DisableDirPage=no
DirExistsWarning=no
UsePreviousAppDir=no
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=..\\artifacts
OutputBaseFilename=RevivalRevolution-Setup-{#MyAppVersion}-win-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64
Uninstallable=yes
CloseApplications= no

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\\BrazilianPortuguese.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Criar atalho na área de trabalho"; GroupDescription: "Atalhos adicionais:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\\{#MyAppName}"; Filename: "{app}\\RevivalRevolution.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\\{#MyAppName}"; Filename: "{app}\\RevivalRevolution.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\\RevivalRevolution.exe"; Description: "Abrir {#MyAppName}"; Flags: postinstall nowait skipifsilent

[Code]
function GetDefaultGameDir(Param: string): string;
begin
  Result := ExpandConstant('{autopf}\\Metal Revolution');
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  GameExe: String;
  ManagedDir: String;
begin
  Result := True;
  if CurPageID = wpSelectDir then
  begin
    GameExe := ExpandConstant('{app}\\ufo.exe');
    ManagedDir := ExpandConstant('{app}\\ufo_Data\\Managed');
    if (not FileExists(GameExe)) or (not DirExists(ManagedDir)) then
    begin
      MsgBox('Selecione a pasta principal do jogo Metal Revolution. Ela precisa conter ufo.exe e ufo_Data\\Managed.', mbError, MB_OK);
      Result := False;
    end;
  end;
end;
