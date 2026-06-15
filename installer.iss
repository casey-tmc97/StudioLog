; ============================================================================
; StudioLog - Inno Setup Installer Script
; ============================================================================
; Prerequisites:
;   1. Install Inno Setup from https://jrsoftware.org/isinfo.php
;   2. Run publish.ps1 first to generate the publish folder
;   3. Open this .iss file in Inno Setup Compiler and click Build
;
; Or from command line:
;   iscc installer.iss
; ============================================================================

#define MyAppName "StudioLog"
#define MyAppVersion "2.1.5"
#define MyAppPublisher "StudioLog"
#define MyAppURL "https://studiolog.app"
#define MyAppExeName "StudioLog.exe"
#define MyAppDescription "Professional SMPTE LTC Timecode Logger"

[Setup]
AppId={{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
OutputDir=installer
OutputBaseFilename=StudioLog-v{#MyAppVersion}-Setup
SetupIconFile=icon.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
LicenseFile=
; Uncomment and point to a LICENSE.txt if you have one:
; LicenseFile=LICENSE.txt

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Main application files from publish output
Source: "publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Clean up settings on uninstall (optional - uncomment to remove user data)
; Type: filesandordirs; Name: "{userappdata}\StudioLog"
