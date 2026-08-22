; NexMote Agent Inno Setup Script
; Ultra-fast (1.5 sec compile) Enterprise Installer

#define MyAppName "NexMote Agent"
#define MyAppPublisher "NexMote Inc."
#define MyAppURL "https://nexmote.com"
#define MyAppExeName "NexMote.Agent.Tray.exe"
#define MyServiceExeName "NexMote.Agent.Windows.exe"

#ifndef MyAppVersion
  #define MyAppVersion "0.6.3"
#endif

#ifndef SourceDir
  #define SourceDir "..\artifacts\package\agent"
#endif

#ifndef OutputDir
  #define OutputDir "..\downloads"
#endif

[Setup]
AppId={{A76F12C0-94A1-420E-B6D7-90E0F3628101}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}/downloads
DefaultDirName={autopf}\NexMote\Agent
DefaultGroupName=NexMote
DisableProgramGroupPage=yes
LicenseFile=..\assets\installer\license.rtf
SetupIconFile=..\assets\nexmote.ico
WizardImageFile=..\assets\installer\dialog.bmp
WizardSmallImageFile=..\assets\installer\banner.bmp
OutputDir={#OutputDir}
OutputBaseFilename=NexMote-Agent-Setup
Compression=lzma2/fast
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=force
RestartApplications=no

[Languages]
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#SourceDir}\appsettings.json"; DestDir: "{commonappdata}\NexMote\Agent"; Flags: ignoreversion

[Icons]
Name: "{group}\NexMote Agent"; Filename: "{app}\{#MyAppExeName}"; Parameters: "--dashboard"; IconFilename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,NexMote Agent}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\NexMote Agent"; Filename: "{app}\{#MyAppExeName}"; Parameters: "--dashboard"; IconFilename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; Madde 1: Windows Açılışında Tüm Kullanıcılar İçin Otomatik Başlama
Root: HKLM; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "NexMoteAgentTray"; ValueData: """{app}\{#MyAppExeName}"" --tray"; Flags: uninsdeletevalue

; UAC ve Secure Desktop Düzeltmeleri
Root: HKLM; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System"; ValueType: dword; ValueName: "PromptOnSecureDesktop"; ValueData: 0; Flags: createvalueifdoesntexist
Root: HKLM; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System"; ValueType: dword; ValueName: "SoftwareSASGeneration"; ValueData: 3; Flags: createvalueifdoesntexist

[Run]
; Servisi kur ve başlat
Filename: "{sys}\sc.exe"; Parameters: "create ""NexMote Agent"" binPath= """"{app}\{#MyServiceExeName}"""" start= auto DisplayName= ""NexMote Agent Service"""; Flags: runhidden; StatusMsg: "NexMote Windows Servisi yükleniyor..."
Filename: "{sys}\sc.exe"; Parameters: "description ""NexMote Agent"" ""NexMote Kurumsal Uzaktan Yönetim ve Canlı Destek Servisi."""; Flags: runhidden
Filename: "{sys}\sc.exe"; Parameters: "failure ""NexMote Agent"" reset= 86400 actions= restart/5000/restart/5000/restart/5000"; Flags: runhidden
Filename: "{sys}\sc.exe"; Parameters: "start ""NexMote Agent"""; Flags: runhidden; StatusMsg: "NexMote Servisi başlatılıyor..."

; Madde 2 & 3: Kurulum Biter Bitmez Kullanıcı Oturumunda Sessizce Tepside Başlat
Filename: "{app}\{#MyAppExeName}"; Parameters: "--tray"; Flags: nowait postinstall skipifsilent; Description: "NexMote Ajanını şimdi başlat (Sistem tepsisinde)"

[UninstallRun]
; Kaldırılırken önce servisi durdur ve sil
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM NexMote*"; Flags: runhidden
Filename: "{sys}\sc.exe"; Parameters: "stop ""NexMote Agent"""; Flags: runhidden
Filename: "{sys}\sc.exe"; Parameters: "delete ""NexMote Agent"""; Flags: runhidden
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM NexMote*"; Flags: runhidden

[UninstallDelete]
Type: filesandordirs; Name: "{commonappdata}\NexMote\Agent"
Type: filesandordirs; Name: "{localappdata}\NexMote"
Type: filesandordirs; Name: "{app}"
