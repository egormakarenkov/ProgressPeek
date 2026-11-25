#ifndef BinaryPath
 #error BinaryPath is required
#endif
#ifndef BuildOutput
 #error BuildOutput is required
#endif
#ifndef FixtureId
 #define ProductId "ProgressPeek"
 #define ProductDir "{localappdata}\Programs\ProgressPeek"
#else
 #define ProductId FixtureId
 #define ProductDir FixtureDir
#endif
[Setup]
AppId={#ProductId}
AppName=ProgressPeek
AppVersion=0.4.0-beta
AppVerName=ProgressPeek 0.4.0-beta
DefaultDirName={#ProductDir}
DefaultGroupName=ProgressPeek
PrivilegesRequired=lowest
OutputDir={#BuildOutput}
OutputBaseFilename=ProgressPeek-0.4.0-beta-setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern dynamic
DisableProgramGroupPage=yes
CloseApplications=no
RestartApplications=no
UninstallDisplayIcon={app}\ProgressPeek.exe
SetupLogging=yes
[Files]
Source: "{#BinaryPath}"; DestDir: "{app}"; Flags: ignoreversion
#ifndef FixtureId
[Tasks]
Name: desktopicon; Description: "Create a desktop shortcut"; Flags: unchecked
[Icons]
Name: "{userprograms}\ProgressPeek\ProgressPeek"; Filename: "{app}\ProgressPeek.exe"
Name: "{userdesktop}\ProgressPeek"; Filename: "{app}\ProgressPeek.exe"; Tasks: desktopicon
#endif
; No Run action, credentials, dependency installers, data deletion or license grant.
