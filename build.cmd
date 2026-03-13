@echo off
setlocal enabledelayedexpansion

rem ============================================================
rem encryptTools 打包脚本（.NET 4.8，确保在 .NET 4.6 环境可运行）
rem - 输出: dist\encryptTools\（仅 net48 一档）
rem - 仅当用户选用 AES-GCM 时检查并提示安装 .NET 8；其他算法不检查、不提示
rem ============================================================

cd /d "%~dp0"

set CONFIG=Release
set RID=win-x64
set OUT_DIR=%CD%\dist\encryptTools

for /f "tokens=1 delims=." %%a in ('dotnet --version 2^>nul') do set DOTNET_MAJOR=%%a
if "%DOTNET_MAJOR%"=="" (
  echo.
  echo [encryptTools] ERROR: dotnet SDK not found. Please install .NET SDK.
  exit /b 1
)
echo [encryptTools] Using .NET SDK %DOTNET_MAJOR%, target: net48, runs on .NET 4.6+

echo.
echo [encryptTools] Cleaning output...
taskkill /f /im encryptTools.exe >nul 2>&1
taskkill /f /im EncryptTools.FrameworkDependent.exe >nul 2>&1
taskkill /f /im EncryptTools.exe >nul 2>&1

if exist "%CD%\bin" rmdir /s /q "%CD%\bin"
if exist "%CD%\obj" rmdir /s /q "%CD%\obj"

if exist "%OUT_DIR%" rmdir /s /q "%OUT_DIR%"
mkdir "%OUT_DIR%" >nul 2>&1

echo.
echo [encryptTools] Restoring...
dotnet restore "%CD%\EncryptTools.FrameworkDependent.csproj"
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
echo [encryptTools] Done.
echo Output: "%OUT_DIR%\encryptTools.exe"
exit /b 0

:fail
echo.
echo [encryptTools] Build failed.
exit /b 1
