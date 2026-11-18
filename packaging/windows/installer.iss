; Inno Setup script for the Windows installer. Driven by build.ps1, which passes:
;   /DAppVersion=0.2.0 /DArch=x64|arm64 /DPublishDir=<published tree> /DOutDir=<output dir>
#ifndef AppVersion
  #define AppVersion "0.2.0"
#endif
#ifndef Arch
  #define Arch "x64"
#endif
#ifndef PublishDir
  #error PublishDir must be defined (the output of dotnet publish)
#endif
#ifndef OutDir
  #define OutDir "..\..\artifacts\dist"
#endif

#define AppName "Git Extensions Next"
#define AppExe "GitExtensions.Avalonia.exe"

[Setup]
; Fixed GUID: identifies this application across upgrades. Never change it.
AppId={{6B9F3C52-4D0E-4A57-9E2B-3F1A7C8D5E41}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=Git Extensions Next
AppPublisherURL=https://github.com/khunder/git-extensions-next
AppSupportURL=https://github.com/khunder/git-extensions-next/issues
DefaultDirName={autopf}\GitExtensions
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
LicenseFile=..\..\LICENSE.md
SetupIconFile=git-extensions.ico
UninstallDisplayIcon={app}\{#AppExe}
OutputDir={#OutDir}
OutputBaseFilename=git-extensions-{#AppVersion}-windows-{#Arch}-setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
MinVersion=10.0
#if Arch == "arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#else
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#endif

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\..\LICENSE.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent
