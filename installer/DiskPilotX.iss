#define AppName "DiskPilot-X"
#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\artifacts\publish"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts\installer"
#endif
[Setup]
AppId={{E7DBD6CB-0C31-4E44-9B91-DB2A6B1E0A11}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=Bian-Sh
AppPublisherURL=https://github.com/Bian-Sh/DiskPilot-X
DefaultDirName={autopf}\DiskPilot-X
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDir}
OutputBaseFilename=DiskPilot-X-{#AppVersion}-setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
Uninstallable=yes
CloseApplications=yes
RestartApplications=yes
[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion createallsubdirs
[Icons]
Name: "{autoprograms}\DiskPilot-X"; Filename: "{app}\DiskPilot-X.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\DiskPilot-X"; Filename: "{app}\DiskPilot-X.exe"; WorkingDir: "{app}"
[Run]
Filename: "{app}\DiskPilot-X.exe"; Description: "启动 DiskPilot-X"; WorkingDir: "{app}"; Flags: nowait postinstall
