@echo off
setlocal enabledelayedexpansion

rem ============================================================
rem encryptTools 打包脚本（单 exe 兼容）
rem - 主程序 net48，.NET 4.6 可运行
rem - 选 GCM 且本机已装 .NET 8 时通过同目录 EncryptTools.GcmCli.dll 执行；未装则自动用 CBC
rem - 输出: dist\encryptTools\encryptTools.exe + EncryptTools.GcmCli.dll + .runtimeconfig.json
rem ============================================================

cd /d "%~dp0"

set CONFIG=Release
set RID=win-x64
set OUT_DIR=%CD%\dist\encryptTools
set OUT_GCM=%CD%\dist\encryptTools_gcm_temp

for /f "tokens=1 delims=." %%a in ('dotnet --version 2^>nul') do set DOTNET_MAJOR=%%a
if "%DOTNET_MAJOR%"=="" (
  echo.
  echo [encryptTools] ERROR: dotnet SDK not found. Please install .NET SDK.
  exit /b 1
)
echo [encryptTools] Using .NET SDK %DOTNET_MAJOR%

echo.
echo [encryptTools] Cleaning output...
taskkill /f /im encryptTools.exe >nul 2>&1
taskkill /f /im EncryptTools.FrameworkDependent.exe >nul 2>&1

if exist "%CD%\bin" rmdir /s /q "%CD%\bin"
if exist "%CD%\obj" rmdir /s /q "%CD%\obj"

if exist "%OUT_DIR%" rmdir /s /q "%OUT_DIR%"
if exist "%OUT_GCM%" rmdir /s /q "%OUT_GCM%"
mkdir "%OUT_DIR%" >nul 2>&1
mkdir "%OUT_GCM%" >nul 2>&1

echo.
echo [encryptTools] Restoring...
dotnet restore "%CD%\EncryptTools.FrameworkDependent.csproj"
if errorlevel 1 goto :fail
dotnet restore "%CD%\EncryptTools.GcmCli\EncryptTools.GcmCli.csproj"
if errorlevel 1 goto :fail

echo.
echo [encryptTools] Publishing for .NET 4.8...
dotnet publish "%CD%\EncryptTools.FrameworkDependent.csproj" ^
  -c %CONFIG% ^
  -r %RID% ^
  -f net48 ^
  --self-contained false ^
  -o "%OUT_DIR%" ^
  /p:DebugType=None ^
  /p:DebugSymbols=false ^
  /p:GenerateDocumentationFile=false
if errorlevel 1 goto :fail

echo.
echo [encryptTools] Publishing GcmCli for .NET 8...
dotnet publish "%CD%\EncryptTools.GcmCli\EncryptTools.GcmCli.csproj" ^
  -c %CONFIG% ^
  -r %RID% ^
  -f net8.0 ^
  --self-contained false ^
  -o "%OUT_GCM%" ^
  /p:DebugType=None ^
  /p:DebugSymbols=false
if errorlevel 1 goto :fail

copy /Y "%OUT_GCM%\EncryptTools.GcmCli.dll" "%OUT_DIR%\" >nul 2>&1
copy /Y "%OUT_GCM%\EncryptTools.GcmCli.runtimeconfig.json" "%OUT_DIR%\" >nul 2>&1
rmdir /s /q "%OUT_GCM%" >nul 2>&1

echo.
echo [encryptTools] Done.
echo Output: "%OUT_DIR%\encryptTools.exe"
echo GCM helper: "%OUT_DIR%\EncryptTools.GcmCli.dll" (used when .NET 8 installed)
exit /b 0

:fail
echo.
echo [encryptTools] Build failed.
exit /b 1
