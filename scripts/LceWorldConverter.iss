#ifndef MyAppVersion
  #define MyAppVersion "dev"
#endif

#ifndef MyAppRoot
  #error "MyAppRoot was not provided."
#endif

#ifndef MyOutputDir
  #error "MyOutputDir was not provided."
#endif

#ifndef MyInstallerBaseName
  #define MyInstallerBaseName "LceWorldConverter-setup"
#endif

#define MyAppName "LCE Save Converter"
#define MyPublisher "BanditVault"
#define MyGuiExe "LceWorldConverter.Gui.exe"
#define MyCliExe "LceWorldConverter.exe"

[Setup]
AppId={{7FD3A1A8-BA02-493D-96B5-14F9A9DA3B57}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyPublisher}
DefaultDirName={localappdata}\Programs\LCE Save Converter
DefaultGroupName={#MyAppName}
OutputDir={#MyOutputDir}
OutputBaseFilename={#MyInstallerBaseName}
Compression=lzma
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\{#MyGuiExe}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked

[Files]
Source: "{#MyAppRoot}\*"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyGuiExe}"
Name: "{autoprograms}\{#MyAppName} CLI"; Filename: "{app}\{#MyCliExe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyGuiExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyGuiExe}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent
