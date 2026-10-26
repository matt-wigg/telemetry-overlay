#ifndef AppVersion
  #define AppVersion "0.3.0"
#endif
[Setup]
AppId={{7B9F1ED2-2A30-45F1-BD53-D6A32EC88F73}
AppName=Telemetry
AppVersion={#AppVersion}
AppPublisher=Telemetry contributors
AppPublisherURL=https://github.com/matt-wigg/telemetry-overlay
DefaultDirName={localappdata}\Programs\Telemetry
DefaultGroupName=Telemetry
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22000
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE
OutputDir=..\artifacts
OutputBaseFilename=Telemetry-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\Telemetry.exe
CloseApplications=yes
RestartApplications=no
[Files]
Source: "..\artifacts\portable\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
[Icons]
Name: "{group}\Telemetry Controls"; Filename: "{app}\Telemetry.exe"; Parameters: "--settings"
Name: "{group}\Uninstall Telemetry"; Filename: "{uninstallexe}"
[Tasks]
Name: "startup"; Description: "Launch Telemetry when I sign in to Windows"
[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Telemetry"; ValueData: """{app}\Telemetry.exe"" --startup"; Tasks: startup; Flags: uninsdeletevalue
[Run]
Filename: "{app}\Telemetry.exe"; Parameters: "--settings"; Description: "Open Telemetry Controls"; Flags: nowait postinstall skipifsilent
