#define AppName "Auvryxel"
#define AppVersion "0.1.0"
#define AppPublisher "Auvryxel"
#define AppExeName "Auvryxel.exe"

[Setup]
AppId={{A44B538A-302E-4D80-867A-6A92DA5065F1}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={localappdata}\Programs\Auvryxel
DefaultGroupName={#AppName}
UninstallDisplayIcon={app}\{#AppExeName}
SetupIconFile=..\Assets\Auvryxel.ico
OutputDir=..\artifacts\installer
OutputBaseFilename=Auvryxel-Setup-{#AppVersion}-win-x64
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
DisableProgramGroupPage=yes
CloseApplications=yes
RestartApplications=no
Uninstallable=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "..\artifacts\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\artifacts\MicrosoftEdgeWebView2Setup.exe"; Flags: dontcopy

[Icons]
Name: "{autoprograms}\Auvryxel"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\Auvryxel"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch Auvryxel"; Flags: nowait postinstall skipifsilent

[Code]
const
  WebView2ClientKey = 'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';

function HasWebView2At(RootKey: Integer; const Key: String): Boolean;
var
  Version: String;
begin
  Result := RegQueryStringValue(RootKey, Key, 'pv', Version) and
    (Version <> '') and (Version <> '0.0.0.0');
end;

function HasWebView2: Boolean;
begin
  Result := HasWebView2At(HKLM64, WebView2ClientKey) or
    HasWebView2At(HKCU64, 'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}') or
    HasWebView2At(HKCU32, 'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}');
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ExitCode: Integer;
begin
  Result := '';
  if HasWebView2 then
    exit;

  ExtractTemporaryFile('MicrosoftEdgeWebView2Setup.exe');

  if not Exec(ExpandConstant('{tmp}\MicrosoftEdgeWebView2Setup.exe'),
    '/silent /install', '', SW_HIDE, ewWaitUntilTerminated, ExitCode) then
  begin
    Result := 'Auvryxel needs the Microsoft WebView2 Runtime, but its installer could not be started.';
    exit;
  end;

  if (ExitCode <> 0) and (ExitCode <> 3010) then
    Result := 'The Microsoft WebView2 Runtime could not be installed (error ' + IntToStr(ExitCode) + '). Connect to Wi-Fi and try setup again.'
  else
    NeedsRestart := ExitCode = 3010;
end;
