; encryptTools 安装包脚本（由 build.cmd 调用）
; 要求：先由 build.cmd 发布 dist\encryptTools\encryptTools.exe

#define SourceRootDir GetSourceDir()

[Setup]
AppName=encryptTools
AppVersion=1.0.0
DefaultDirName={pf}\encryptTools
DefaultGroupName=encryptTools
OutputDir={#SourceRootDir}\dist\encryptTools
OutputBaseFilename=encryptTools_setup
Compression=lzma
SolidCompression=yes
WizardStyle=modern
SetupIconFile={#SourceRootDir}\app.ico

[Files]
; 主程序：framework-dependent 单文件
Source: "{#SourceRootDir}\dist\encryptTools\encryptTools.exe"; DestDir: "{app}"; Flags: ignoreversion
; 如有额外必须文件，可继续在此处追加 Source 行

[Icons]
Name: "{group}\encryptTools"; Filename: "{app}\encryptTools.exe"
Name: "{group}\卸载 encryptTools"; Filename: "{uninstallexe}"
Name: "{commondesktop}\encryptTools"; Filename: "{app}\encryptTools.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "在桌面创建快捷方式"; GroupDescription: "附加任务："

[Run]
Filename: "{app}\encryptTools.exe"; Description: "运行 encryptTools"; Flags: nowait postinstall skipifsilent

