#ifndef AppVersion
  #error AppVersion must be supplied by the packaging script.
#endif
#ifndef PayloadDir
  #error PayloadDir must be supplied by the packaging script.
#endif
#ifndef ArtifactDir
  #error ArtifactDir must be supplied by the packaging script.
#endif

#define AppName "ExtSieve"
#define AppExeName "ExtSieve.App.exe"
#define AppPublisher "Giuseppe Russo"
#define AppId "{{E85F83F4-50BD-4B20-8B81-8D3793EAE130}"
#define AppMutexName "Local\ExtSieve_E85F83F4_50BD_4B20_8B81_8D3793EAE130"

[Setup]
AppId={#AppId}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppComments=@melnorme6, github.com/melnorme6
AppCopyright=Copyright (c) 2026 Giuseppe Russo
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
LicenseFile={#SourcePath}\..\..\LICENSE
OutputDir={#ArtifactDir}
OutputBaseFilename=ExtSieve-Setup-{#AppVersion}-win-x64
SetupIconFile={#SourcePath}\..\..\branding\app\extsieve-app-icon.ico
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName}
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
SetupArchitecture=x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
CloseApplicationsFilter={#AppExeName}
RestartApplications=yes
MinVersion=10.0.17763
VersionInfoVersion={#AppVersion}.0
VersionInfoCompany={#AppPublisher}
VersionInfoDescription={#AppName} installer
VersionInfoProductName={#AppName}
VersionInfoProductVersion={#AppVersion}
VersionInfoCopyright=Copyright (c) 2026 Giuseppe Russo

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; \
  GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}"; \
  Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"; \
  WorkingDir: "{app}"; IconFilename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; \
  WorkingDir: "{app}"; IconFilename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch {#AppName}"; \
  Flags: nowait postinstall skipifsilent

[Code]
function InitializeUninstall(): Boolean;
begin
  Result := True;
  if CheckForMutexes('{#AppMutexName}') then
  begin
    SuppressibleMsgBox(
      'ExtSieve is currently running. Close the application, then start the uninstall again.',
      mbError,
      MB_OK,
      IDOK);
    Result := False;
  end;
end;
