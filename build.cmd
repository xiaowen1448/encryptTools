@echo off
setlocal enabledelayedexpansion

rem ============================================================
rem encryptTools build script ( .NET 8 )
rem - Outputs: dist\encryptTools\encryptTools.exe
rem - Target: net8.0-windows, framework-dependent single-file
rem ============================================================

cd /d "%~dp0"

set CONFIG=Release
set RID=win-x64
set OUT_DIR=%CD%\dist\encryptTools
set TARGET_FX=net8.0-windows

for /f "tokens=1 delims=." %%a in ('dotnet --version 2^>nul') do set DOTNET_MAJOR=%%a
if "%DOTNET_MAJOR%"=="" (
  echo.
  echo [encryptTools] ERROR: dotnet SDK not found. Please install .NET 8 SDK.
  exit /b 1
)
if %DOTNET_MAJOR% LSS 8 (
  echo.
  echo [encryptTools] ERROR: Need .NET 8 SDK. Current: %DOTNET_MAJOR%.
  exit /b 1
)
echo [encryptTools] Using .NET %DOTNET_MAJOR% SDK, target: %TARGET_FX%

echo.
echo [encryptTools] Cleaning output...
rem kill running app to avoid obj/bin file lock
taskkill /f /im encryptTools.exe >nul 2>&1

rem kill also if started from dist folder name differs (best-effort)
taskkill /f /im EncryptTools.FrameworkDependent.exe >nul 2>&1
taskkill /f /im EncryptTools.exe >nul 2>&1

rem clean build intermediates (avoid CS2012 file in use)
if exist "%CD%\bin" rmdir /s /q "%CD%\bin"
if exist "%CD%\obj" rmdir /s /q "%CD%\obj"

if exist "%OUT_DIR%" rmdir /s /q "%OUT_DIR%"
rem if rmdir failed due to lock, try deleting exe directly then retry
if exist "%OUT_DIR%\encryptTools.exe" (
  del /f /q "%OUT_DIR%\encryptTools.exe" >nul 2>&1
)
if exist "%OUT_DIR%" rmdir /s /q "%OUT_DIR%"
mkdir "%OUT_DIR%" >nul 2>&1

echo.
echo [encryptTools] Restoring...
dotnet restore "%CD%\EncryptTools.FrameworkDependent.csproj"
if errorlevel 1 goto :fail

echo.
echo [encryptTools] Publishing with .NET 8 (framework-dependent single-file)...
dotnet publish "%CD%\EncryptTools.FrameworkDependent.csproj" ^
  -c %CONFIG% ^
  -r %RID% ^
  -f %TARGET_FX% ^
  --self-contained false ^
  -o "%OUT_DIR%" ^
  /p:PublishSingleFile=true ^
  /p:EnableCompressionInSingleFile=false ^
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

