#define AppName "ConverTool"
#define AppVersion "1.0.1"
#define AppPublisher "ConverTool"
#define AppExeName "ConverTool.exe"

[Setup]
AppId={{F2C4EC54-2D31-4C0F-8E05-86D86E8A4ED3}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\{#AppName}
DisableDirPage=no
UsePreviousAppDir=no
DefaultGroupName={#AppName}
; Trash icon for Apps list / uninstall shortcut (unins000.exe still uses SetupIconFile).
UninstallDisplayIcon={app}\uninstall.ico
; Use icon next to this script (also copied from Host/Assets for reliable builds).
SetupIconFile=Assets\convertool-icon.ico
Compression=lzma
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\artifacts
OutputBaseFilename=ConverTool-v{#AppVersion}-setup

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "chinesesimp"; MessagesFile: "Languages\ChineseSimplified.isl"

[Types]
Name: "custom"; Description: "{cm:TypeCustom}"; Flags: iscustom

[Components]
Name: "plugins"; Description: "{cm:BundledPlugins}"; Types: custom
Name: "plugins\ffmpeg"; Description: "{cm:PluginFfmpeg}"; Types: custom; Flags: checkablealone
Name: "plugins\imagemagick"; Description: "{cm:PluginImagemagick}"; Types: custom; Flags: checkablealone

[Tasks]
Name: "mode_full"; Description: "{cm:InstallModeFull}"; GroupDescription: "{cm:CorePackageMode}"; Flags: exclusive
Name: "mode_lite"; Description: "{cm:InstallModeLite}"; GroupDescription: "{cm:CorePackageMode}"; Flags: exclusive
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"

[Files]
; Uninstall branding (referenced by UninstallDisplayIcon + uninstall shortcut).
Source: "Assets\uninstall.ico"; DestDir: "{app}"; DestName: "uninstall.ico"; Flags: ignoreversion

; Core payload (full/lite mode selection is controlled by tasks).
Source: "..\artifacts\host\v{#AppVersion}\win-x64\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion; Excludes: "plugins\*"; Tasks: mode_full
Source: "..\artifacts\host\v{#AppVersion}\win-x64-lite\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion; Excludes: "plugins\*"; Tasks: mode_lite

; Plugins (component-controlled, default all selected).
Source: "..\Host\Plugins\ffmpeg.video.transcoder\*"; DestDir: "{app}\plugins\ffmpeg.video.transcoder"; Flags: recursesubdirs createallsubdirs ignoreversion; Components: plugins\ffmpeg
Source: "..\Host\Plugins\imagemagick.image.transcoder\*"; DestDir: "{app}\plugins\imagemagick.image.transcoder"; Flags: recursesubdirs createallsubdirs ignoreversion; Components: plugins\imagemagick

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"; IconFilename: "{app}\uninstall.ico"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[CustomMessages]
english.TypeCustom=Custom installation
english.BundledPlugins=Bundled plugins
english.PluginFfmpeg=FFmpeg Video Transcoder
english.PluginImagemagick=ImageMagick Image Transcoder
english.InstallModeFull=Install Full (self-contained, no .NET runtime required)
english.InstallModeLite=Install Lite (requires .NET 8 Desktop Runtime)
english.CorePackageMode=Core package mode:

chinesesimp.TypeCustom=自定义安装
chinesesimp.BundledPlugins=内置插件
chinesesimp.PluginFfmpeg=FFmpeg 视频转码插件
chinesesimp.PluginImagemagick=ImageMagick 图像转码插件
chinesesimp.InstallModeFull=安装 Full 完整版（自带运行时，无需 .NET 环境）
chinesesimp.InstallModeLite=安装 Lite 精简版（需要 .NET 8 Desktop Runtime）
chinesesimp.CorePackageMode=核心安装模式：

[Code]
var
  TaskDefaultsApplied: Boolean;

function HasAnySubdirWithPrefix(const RootDir, Prefix: string): Boolean;
var
  FindRec: TFindRec;
begin
  Result := False;
  if not DirExists(RootDir) then
    Exit;

  if FindFirst(AddBackslash(RootDir) + '*', FindRec) then
  begin
    try
      repeat
        if ((FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0)
          and (FindRec.Name <> '.')
          and (FindRec.Name <> '..')
          and (Copy(FindRec.Name, 1, Length(Prefix)) = Prefix) then
        begin
          Result := True;
          Exit;
        end;
      until not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;
end;

function HasDotNet8DesktopRuntime: Boolean;
begin
  Result :=
    HasAnySubdirWithPrefix(ExpandConstant('{commonpf}\dotnet\shared\Microsoft.WindowsDesktop.App'), '8.')
    or HasAnySubdirWithPrefix(ExpandConstant('{commonpf32}\dotnet\shared\Microsoft.WindowsDesktop.App'), '8.');
end;

function HasModeTaskFromCommandLine: Boolean;
var
  TaskParam: string;
begin
  TaskParam := Lowercase(ExpandConstant('{param:tasks|}'));
  Result := (Pos('mode_full', TaskParam) > 0) or (Pos('mode_lite', TaskParam) > 0);
end;

procedure InitializeWizard;
begin
  TaskDefaultsApplied := False;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if (CurPageID = wpSelectTasks) and (not TaskDefaultsApplied) then
  begin
    if not HasModeTaskFromCommandLine then
    begin
      if HasDotNet8DesktopRuntime then
        WizardSelectTasks('mode_lite')
      else
        WizardSelectTasks('mode_full');
    end;

    // Keep plugins selected by default.
    WizardSelectComponents('plugins\ffmpeg,plugins\imagemagick');
    TaskDefaultsApplied := True;
  end;
end;
