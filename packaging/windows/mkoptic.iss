; Inno Setup script. Build: iscc /DAppVersion=1.0.0 /DAppArch=arm64 /DSourceExe=path\to\MkOptic.exe packaging\windows\mkoptic.iss
#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceExe
  #define SourceExe "..\..\artifacts\win\MkOptic.exe"
#endif
#ifndef AppArch
  #define AppArch "x64"
#endif
#if AppArch == "arm64"
  #define SetupArchitecture "arm64"
#elif AppArch == "x64"
  #define SetupArchitecture "x64compatible"
#else
  #error Unsupported AppArch. Use x64 or arm64.
#endif

#define AppName "MkOptic"
#define AppExe "MkOptic.exe"
#define ProgId "MkOptic.Markdown"

[Setup]
AppId={{6F1C2B3E-5D0A-4C7B-9A41-2E8F0B7D3C15}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=Sergey Gulyaev
AppPublisherURL=https://github.com/ambyte/MkOptic
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
UninstallDisplayIcon={app}\{#AppExe}
LicenseFile=..\..\LICENSE
OutputDir=..\..\artifacts
OutputBaseFilename=MkOptic-{#AppVersion}-win-{#AppArch}-setup
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed={#SetupArchitecture}
ArchitecturesInstallIn64BitMode={#SetupArchitecture}
; Per-user install by default, the user may choose "for all users" (admin) in the dialog.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ChangesAssociations=yes
WizardStyle=modern

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; Flags: unchecked
Name: "associate"; Description: "Register MkOptic for Markdown files (.md, .markdown, ...)"

[Files]
Source: "{#SourceExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; File type (ProgID) and the application registration shown in "Open with" and Settings > Default apps.
Root: HKA; Subkey: "Software\Classes\{#ProgId}"; ValueType: string; ValueData: "Markdown document"; Flags: uninsdeletekey; Tasks: associate
Root: HKA; Subkey: "Software\Classes\{#ProgId}\DefaultIcon"; ValueType: string; ValueData: "{app}\{#AppExe},0"; Tasks: associate
Root: HKA; Subkey: "Software\Classes\{#ProgId}\shell\open\command"; ValueType: string; ValueData: """{app}\{#AppExe}"" ""%1"""; Tasks: associate

Root: HKA; Subkey: "Software\Classes\.md\OpenWithProgids"; ValueType: none; ValueName: "{#ProgId}"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associate
Root: HKA; Subkey: "Software\Classes\.markdown\OpenWithProgids"; ValueType: none; ValueName: "{#ProgId}"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associate
Root: HKA; Subkey: "Software\Classes\.mdown\OpenWithProgids"; ValueType: none; ValueName: "{#ProgId}"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associate
Root: HKA; Subkey: "Software\Classes\.mkd\OpenWithProgids"; ValueType: none; ValueName: "{#ProgId}"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associate

Root: HKA; Subkey: "Software\{#AppName}\Capabilities"; ValueType: string; ValueName: "ApplicationName"; ValueData: "{#AppName}"; Flags: uninsdeletekey; Tasks: associate
Root: HKA; Subkey: "Software\{#AppName}\Capabilities"; ValueType: string; ValueName: "ApplicationDescription"; ValueData: "Markdown editor and viewer"; Tasks: associate
Root: HKA; Subkey: "Software\{#AppName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".md"; ValueData: "{#ProgId}"; Tasks: associate
Root: HKA; Subkey: "Software\{#AppName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".markdown"; ValueData: "{#ProgId}"; Tasks: associate
Root: HKA; Subkey: "Software\{#AppName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".mdown"; ValueData: "{#ProgId}"; Tasks: associate
Root: HKA; Subkey: "Software\{#AppName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".mkd"; ValueData: "{#ProgId}"; Tasks: associate
Root: HKA; Subkey: "Software\RegisteredApplications"; ValueType: string; ValueName: "{#AppName}"; ValueData: "Software\{#AppName}\Capabilities"; Flags: uninsdeletevalue; Tasks: associate

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent
