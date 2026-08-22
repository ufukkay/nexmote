; NexMote Technician Console Inno Setup Script
; Ultra-fast (1.5 sec compile) Enterprise Installer

#define MyAppName "NexMote Teknisyen Konsolu"
#define MyAppPublisher "NexMote Inc."
#define MyAppURL "https://nexmote.com"
#define MyAppExeName "NexMote.TechnicianApp.exe"

#ifndef MyAppVersion
  #define MyAppVersion "0.6.3"
#endif

#ifndef SourceDir
  #define SourceDir "..\artifacts\package\technician"
#endif

#ifndef OutputDir
  #define OutputDir "..\downloads"
#endif

[Setup]
AppId={{B81E2A4F-821B-4190-84E1-912A09B2E801}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}/downloads
DefaultDirName={autopf}\NexMote\Technician
DefaultGroupName=NexMote
DisableProgramGroupPage=yes
LicenseFile=..\assets\installer\license.rtf
SetupIconFile=..\assets\nexmote.ico
WizardImageFile=..\assets\installer\dialog.bmp
WizardSmallImageFile=..\assets\installer\banner.bmp
OutputDir={#OutputDir}
OutputBaseFilename=NexMote-Technician-Setup
Compression=lzma2/fast
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
CloseApplications=force
RestartApplications=no

[Languages]
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\NexMote Teknisyen Konsolu"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,NexMote Teknisyen Konsolu}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\NexMote Teknisyen Konsolu"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; nexmote:// Deep-Link Protokol Eşleştirmesi
Root: HKCU; Subkey: "Software\Classes\nexmote"; ValueType: string; ValueName: ""; ValueData: "URL:NexMote Remote Control Protocol"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\nexmote"; ValueType: string; ValueName: "URL Protocol"; ValueData: ""; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\nexmote\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"",0"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\nexmote\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Flags: uninsdeletekey

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "NexMote Teknisyen Konsolunu Başlat"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM NexMote.TechnicianApp.exe"; Flags: runhidden

[UninstallDelete]
Type: filesandordirs; Name: "{localappdata}\NexMote"
Type: filesandordirs; Name: "{app}"
