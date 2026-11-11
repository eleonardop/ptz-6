; Instalador do PTZ Studio (Inno Setup 6)
[Setup]
AppName=PTZ Studio
AppVersion=1.0.0
AppPublisher=PTZ Studio
DefaultDirName={autopf}\PTZ Studio
DefaultGroupName=PTZ Studio
OutputDir=Output
OutputBaseFilename=PTZStudio-Setup
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
UninstallDisplayName=PTZ Studio

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Tasks]
Name: "desktopicon"; Description: "Criar atalho na área de trabalho"; GroupDescription: "Atalhos:"

[Files]
Source: "..\publish\PTZStudio.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\docs\MANUAL.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\PTZ Studio"; Filename: "{app}\PTZStudio.exe"
Name: "{autodesktop}\PTZ Studio"; Filename: "{app}\PTZStudio.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\PTZStudio.exe"; Description: "Abrir o PTZ Studio"; Flags: nowait postinstall skipifsilent
