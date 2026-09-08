#ifndef AppVersion
  #define AppVersion "1.1.0"
#endif

#define AppName "刷脸认证"
#define AppPublisher "alterTom"
#define AppExecutable "FaceCaptureAgent.exe"

[Setup]
AppId={{8A8BD462-66C4-4DD6-B9C0-E671F92D02C7}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={localappdata}\Programs\FaceCaptureAgent
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\installer-output
OutputBaseFilename=刷脸认证
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\FaceCaptureAgent\Assets\face-capture.ico
CloseApplications=force
RestartApplications=no
UninstallDisplayIcon={app}\{#AppExecutable}
VersionInfoVersion={#AppVersion}
VersionInfoProductName={#AppName}
VersionInfoProductVersion={#AppVersion}

[Languages]
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Files]
Source: "..\publish\windows-x64\*"; Excludes: "config.toml"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\publish\windows-x64\config.toml"; DestDir: "{app}"; Flags: onlyifdoesntexist
Source: "..\scripts\stop-installed-agent.ps1"; DestDir: "{app}"; Flags: ignoreversion

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "FaceCaptureAgent"; ValueData: """{app}\{#AppExecutable}"""; Flags: uninsdeletevalue

[Icons]
Name: "{userdesktop}\{#AppName}"; Filename: "{app}\{#AppExecutable}"; WorkingDir: "{app}"; IconFilename: "{app}\{#AppExecutable}"
Name: "{group}\启动 {#AppName}"; Filename: "{app}\{#AppExecutable}"; WorkingDir: "{app}"
Name: "{group}\卸载 {#AppName}"; Filename: "{uninstallexe}"

[Run]
Filename: "{app}\{#AppExecutable}"; WorkingDir: "{app}"; Flags: nowait; Description: "启动 {#AppName}"

[UninstallRun]
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File ""{app}\stop-installed-agent.ps1"" -ExecutablePath ""{app}\{#AppExecutable}"""; Flags: runhidden waituntilterminated; RunOnceId: "StopFaceCaptureAgent"
