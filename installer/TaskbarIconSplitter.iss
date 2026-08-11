#ifndef AppVersion
  #error AppVersion must be supplied by scripts\build-release.ps1
#endif

#define AppName "Taskbar Icon Splitter Companion"
#define AppPublisher "OutisNyel"
#define ProjectUrl "https://github.com/OutisNyel/taskbar-icon-splitter"
#define NativeHostName "com.outis.taskbariconsplitter"
#define NativeExeName "TaskbarIconSplitter.Native.exe"
#ifndef RegistryHostName
  #define RegistryHostName NativeHostName
#endif
#ifndef SetupOutputBaseFilename
  #define SetupOutputBaseFilename "TaskbarIconSplitter-Setup-x64"
#endif

[Setup]
AppId={{6C3D264A-504D-4D91-B73D-228C79184E68}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#ProjectUrl}
AppSupportURL={#ProjectUrl}/issues
AppUpdatesURL={#ProjectUrl}/releases/latest
DefaultDirName={localappdata}\TaskbarIconSplitter
DisableDirPage=auto
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22000
OutputDir=..\artifacts
OutputBaseFilename={#SetupOutputBaseFilename}
SetupIconFile=..\assets\taskbar-icon-splitter.ico
LicenseFile=..\LICENSE
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\native\{#NativeExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "..\dist\native\{#NativeExeName}"; DestDir: "{app}\native"; Flags: ignoreversion
Source: "..\dist\native\{#NativeHostName}.json"; DestDir: "{app}"; Flags: ignoreversion

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Edge\NativeMessagingHosts\{#RegistryHostName}"; ValueType: string; ValueName: ""; ValueData: "{app}\{#NativeHostName}.json"; Flags: uninsdeletekey

[UninstallDelete]
Type: filesandordirs; Name: "{app}\icons"
Type: filesandordirs; Name: "{app}\logs"

[Messages]
FinishedLabel=Taskbar Icon Splitter Companion has been installed. Return to Edge and click "Check again".
