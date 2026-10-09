#ifndef ProductVersion
  #define ProductVersion "0.2.0"
#endif
[Setup]
AppId={{D6CFF2F3-7792-49A5-9B87-20CC37617180}
AppName=Compositor
AppVersion={#ProductVersion}
AppPublisher=sun-dove
AppPublisherURL=https://github.com/sun-dove/Compositor
DefaultDirName=D:\Applications\Compositor
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22000
OutputDir={#Stage}\release
OutputBaseFilename=Compositor-{#ProductVersion}-Setup
Compression=lzma2/fast
SolidCompression=no
UninstallDisplayIcon={app}\CompositorStart.exe
SetupLogging=yes
[Files]
Source: "{#Stage}\bootstrap\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#Stage}\feed\feed.json"; DestDir: "{tmp}\CompositorFeed"; Flags: deleteafterinstall
Source: "{#Stage}\feed\payload\*"; DestDir: "{tmp}\CompositorFeed\payload"; Flags: deleteafterinstall recursesubdirs createallsubdirs
[Icons]
Name: "{userprograms}\Compositor"; Filename: "{app}\CompositorStart.exe"
[Run]
Filename: "{app}\CompositorStart.exe"; Description: "启动 Compositor"; Flags: nowait postinstall skipifsilent
[Code]
procedure CurStepChanged(CurStep: TSetupStep);
var ResultCode: Integer;
begin
 if CurStep = ssPostInstall then begin
  if not Exec(ExpandConstant('{app}\CompositorStart.exe'), '--initialize --install --quiet --feed "' + ExpandConstant('{tmp}\CompositorFeed') + '"', ExpandConstant('{app}'), SW_HIDE, ewWaitUntilTerminated, ResultCode) then
   RaiseException('无法启动安装校验程序。');
  if ResultCode <> 0 then RaiseException('安装校验失败，已有版本保留。请检查应用目录权限、磁盘空间和安装包完整性。');
 end;
end;
