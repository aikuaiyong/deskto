; deskto 安装包脚本（可选）
; 用途：把 publish\ 目录打成专业安装程序 deskto-Setup.exe，方便分发给不懂解压的用户
;
; 用法：
;   1) 先运行 .\build.ps1 生成 publish\ 目录
;   2) 安装 Inno Setup —— https://jrsoftware.org/isdl.php
;   3) 用 Inno Setup 打开本文件，点「编译」
;   4) 产出 Output\deskto-Setup.exe
;
; 说明：本脚本假设 publish\ 已存在。如需自动调用 build.ps1，
;      可在 [Run] 段之前用 Preproc 或手动先发布。

#define MyAppName "deskto"
#define MyAppVersion "2.0"
#define MyPublisher "deskto"
#define MyAppExeName "deskto.exe"

[Setup]
AppId={{A1B2C3D4-E5F6-7890-ABCD-1234567890AB}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
OutputBaseFilename=deskto-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64
SetupIconFile=src\Deskto.Wpf\Deskto.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
; 自包含单文件 exe 体积较大，关闭 64 位大小警告
ChangesAssociations=no

[Languages]
Name: "chinese"; MessagesFile: "compiler:Default.isl"
; 如需中文界面，Inno Setup 自带中文语言文件，改用下面这行：
; Name: "chinese"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Files]
Source: "publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\卸载 {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加任务："; Flags: unchecked

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "安装完成后运行 {#MyAppName}"; Flags: nowait postinstall skipifsilent
