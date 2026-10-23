; Inno Setup script for the Ohrbob Windows desktop client.
;
; Built by .github/workflows/release.yml, which passes the version and the
; publish directory on the command line:
;
;   ISCC.exe /DAppVersion=0.0.5 /DAppDir=D:\a\ohrbob.app\publish\win ohrbob.iss
;
; The app registers its own ohrbob:// URL scheme on first run (see
; Ohrbob.App.Desktop/WindowsUriScheme.cs), so the installer writes no registry
; keys and the uninstaller leaves nothing behind.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

#ifndef AppDir
  #define AppDir "..\publish\win"
#endif

#define AppName "Ohrbob"
#define AppPublisher "Ohrbob"
#define AppUrl "https://ohrbob.org"
#define AppExeName "Ohrbob.App.Desktop.exe"

[Setup]
; A fixed AppId keeps upgrades and the uninstall entry working across versions.
AppId={{7C6A1E4B-2F3D-4A9E-9B5C-1D8E0F2A3B4C}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}
AppUpdatesURL={#AppUrl}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE
OutputDir=..\dist\win
OutputBaseFilename=OhrbobSetup-{#AppVersion}
SetupIconFile=..\art\ohrbob-logo.ico
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
; Per-user install: no administrator prompt, everything lands under
; %LOCALAPPDATA%\Programs\Ohrbob.
PrivilegesRequired=lowest
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#AppExeName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#AppDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
