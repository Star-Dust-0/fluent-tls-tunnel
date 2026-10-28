#define AppName "Fluent TLS Tunnel"
#define AppExe "FluentTlsTunnel.exe"
#ifndef BuildVersion
#define BuildVersion "0.1.7"
#endif
#ifndef SourceDir
#define SourceDir "artifacts\portable"
#endif
#ifndef Flavor
#define Flavor "installer"
#endif
[Setup]
AppId={{79B51A3D-2181-4CAC-83A4-68190B5DD994}
AppName={#AppName}
AppVersion={#BuildVersion}
AppPublisher=Mohamed Elnaggar
DefaultDirName={autopf}\FluentTlsTunnel
DefaultGroupName={#AppName}
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=artifacts\packages
OutputBaseFilename=Fluent-TLS-Tunnel-{#Flavor}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupIconFile=1-AppIcon.ico
DisableProgramGroupPage=yes
LicenseFile=LICENSE
CloseApplications=yes
CloseApplicationsFilter={#AppExe}
RestartApplications=yes
UninstallDisplayIcon={app}\{#AppExe}
[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon
[Run]
Filename: "{app}\{#AppExe}"; Description: "Launch Fluent TLS Tunnel"; Flags: nowait postinstall skipifsilent
[Code]
procedure StopRunningApp;
var Code: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /T /IM {#AppExe}', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Sleep(500);
end;
function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopRunningApp;
  Result := '';
end;
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then StopRunningApp;
end;
procedure InitializeWizard;
begin
  WizardForm.WelcomeLabel2.Caption := WizardForm.WelcomeLabel2.Caption + #13#10#13#10 + 'brought to you by Instinct - https://instinct.com';
end;
function DesktopRuntimeInDir(Root: String): Boolean;
var R: TFindRec;
begin
  Result := False;
  if Root = '' then Exit;
  if FindFirst(Root + '\shared\Microsoft.WindowsDesktop.App\8.*', R) then
  begin
    try
      repeat
        if (R.Attributes and 16) <> 0 then Result := True;
      until Result or not FindNext(R);
    finally
      FindClose(R);
    end;
  end;
end;
function DesktopRuntimeInRegistry(RootKey: Integer): Boolean;
var Names: TArrayOfString; I: Integer;
begin
  Result := False;
  if RegGetValueNames(RootKey, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App', Names) then
    for I := 0 to GetArrayLength(Names)-1 do
      if Pos('8.', Names[I]) = 1 then Result := True;
end;
function HasDesktopRuntime8: Boolean;
begin
  Result := DesktopRuntimeInRegistry(HKLM64) or DesktopRuntimeInRegistry(HKLM32)
    or DesktopRuntimeInDir(ExpandConstant('{commonpf64}') + '\dotnet')
    or DesktopRuntimeInDir(ExpandConstant('{commonpf32}') + '\dotnet')
    or DesktopRuntimeInDir(GetEnv('DOTNET_ROOT'))
    or DesktopRuntimeInDir(ExpandConstant('{localappdata}') + '\Microsoft\dotnet');
end;
function InitializeSetup: Boolean;
begin
  Result := True;
#ifdef Dependent
  if not HasDesktopRuntime8 then
    Result := MsgBox('Setup could not find the .NET 8 Desktop Runtime (x64). The app will not start without it. Get it from https://dotnet.microsoft.com/download/dotnet/8.0 or use the bundled installer.' + #13#10#13#10 + 'Continue installing anyway?', mbError, MB_YESNO or MB_DEFBUTTON2) = IDYES;
#endif
end;