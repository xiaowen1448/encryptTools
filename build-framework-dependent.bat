@echo off
echo ========================================
echo    EncryptTools - Framework Dependent Build
echo ========================================
echo.
echo This script creates a framework-dependent build
echo Users need to install .NET 8.0 Runtime to run
echo Pros: Small file size, fast startup
echo Cons: Requires .NET 8.0 Runtime installation
echo.

REM Check if .NET SDK exists
dotnet --version >nul 2>&1
if %errorlevel% neq 0 (
    echo [ERROR] .NET SDK not found, please install .NET 8.0 SDK first
    echo Download: https://dotnet.microsoft.com/download/dotnet/8.0
    pause
    exit /b 1
)

echo [INFO] Detected .NET SDK version:
dotnet --version
echo.

REM Clean previous builds
echo [Step 1/4] Cleaning previous build files...
if exist "bin\Release" rmdir /s /q "bin\Release"
if exist "obj\Release" rmdir /s /q "obj\Release"
if exist "publish-framework-dependent" rmdir /s /q "publish-framework-dependent"
echo Clean completed
echo.

REM Restore NuGet packages
echo [Step 2/4] Restoring NuGet packages...
dotnet restore EncryptTools.FrameworkDependent.csproj
if %errorlevel% neq 0 (
    echo [ERROR] NuGet package restore failed
    pause
    exit /b 1
)
echo Package restore completed
echo.

REM Build project
echo [Step 3/4] Building project...
dotnet build EncryptTools.FrameworkDependent.csproj --configuration Release --no-restore
if %errorlevel% neq 0 (
    echo [ERROR] Project build failed
    pause
    exit /b 1
)
echo Build completed
echo.

REM Publish project (framework-dependent)
echo [Step 4/4] Publishing project (framework-dependent mode)...
dotnet publish EncryptTools.FrameworkDependent.csproj --configuration Release --output "publish-framework-dependent" --no-build --verbosity minimal
if %errorlevel% neq 0 (
    echo [ERROR] Project publish failed
    pause
    exit /b 1
)

echo.
echo ========================================
echo           Build Completed!
echo ========================================
echo.
echo Published files location: %cd%\publish-framework-dependent
echo.
echo File description:
echo - EncryptTools.exe     : Main executable
echo - EncryptTools.dll     : Program library
echo - EncryptTools.runtimeconfig.json : Runtime configuration
echo - Other dependency files
echo.
echo Usage instructions:
echo 1. Copy publish-framework-dependent folder to target computer
echo 2. Ensure target computer has .NET 8.0 Runtime installed
echo 3. Double-click EncryptTools.exe to run
echo.
echo .NET 8.0 Runtime download:
echo https://dotnet.microsoft.com/download/dotnet/8.0
echo.

REM Show file size information
echo Package file size information:
dir "publish-framework-dependent" /s /-c | find "File(s)"
echo.

REM Ask if open publish folder
set /p openFolder="Open publish folder? (Y/N): "
if /i "%openFolder%"=="Y" (
    explorer "publish-framework-dependent"
)

echo Press any key to exit...
pause >nul