@echo off
chcp 65001 >nul
title EncryptTools - Self-Contained Single File Build Script

echo ========================================
echo    EncryptTools Single File Builder
echo ========================================
echo.
echo This script creates a single executable file that:
echo - Includes complete .NET 8.0 Runtime
echo - Requires no installation on target computer
echo - Can run directly on any compatible Windows system
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

REM Select target platform
echo Please select target platform:
echo 1. Windows x64 (Recommended)
echo 2. Windows x86 (32-bit systems)
echo 3. Windows ARM64
echo.
echo Note: Linux and macOS are not supported due to Windows Forms dependency
echo.
set /p platform="Enter option (1-3, default is 1): "

if "%platform%"=="" set platform=1
if "%platform%"=="1" set rid=win-x64
if "%platform%"=="2" set rid=win-x86
if "%platform%"=="3" set rid=win-arm64

if "%rid%"=="" (
    echo [ERROR] Invalid platform selection
    pause
    exit /b 1
)

echo [INFO] Selected target platform: %rid%
echo.

REM Clean previous builds
echo [Step 1/5] Cleaning previous build files...
if exist "bin\Release" rmdir /s /q "bin\Release"
if exist "obj\Release" rmdir /s /q "obj\Release"
if exist "publish-self-contained-%rid%" rmdir /s /q "publish-self-contained-%rid%"
echo Clean completed
echo.

REM Restore NuGet packages for specific runtime
echo [Step 2/5] Restoring NuGet packages for %rid%...
dotnet restore EncryptTools.SelfContained.csproj --runtime %rid%
if %errorlevel% neq 0 (
    echo [ERROR] NuGet package restore failed
    pause
    exit /b 1
)
echo Package restore completed
echo.

REM Build project for specific runtime
echo [Step 3/5] Building project for %rid%...
dotnet build EncryptTools.SelfContained.csproj --configuration Release --runtime %rid% --no-restore
if %errorlevel% neq 0 (
    echo [ERROR] Project build failed
    pause
    exit /b 1
)
echo Build completed
echo.

REM Publish project (self-contained)
echo [Step 4/5] Publishing project (self-contained single file mode)...
echo Creating optimized single executable file with embedded .NET Runtime...
if exist "publish-single-file-%rid%" rmdir /s /q "publish-single-file-%rid%"
dotnet publish EncryptTools.SelfContained.csproj --configuration Release --runtime %rid% --self-contained true --output "publish-single-file-%rid%" -p:PublishSingleFile=true --verbosity minimal
if %errorlevel% neq 0 (
    echo [ERROR] Single file publish failed
    pause
    exit /b 1
)

echo [Step 5/5] Optimizing single file...
echo Single file created successfully!

echo.
echo ========================================
echo           Build Completed!
echo ========================================
echo.
echo Single executable file location: %cd%\publish-single-file-%rid%\EncryptTools.exe
echo.
echo File description:
echo - EncryptTools.exe     : Single executable file (includes .NET 8.0 Runtime)
echo - No additional files needed
echo.
echo Usage instructions:
echo 1. Copy EncryptTools.exe to target computer
echo 2. No dependencies required, double-click to run directly
echo 3. Compatible with %rid% platform
echo 4. Can run on computers without .NET installed
echo.

REM Show file size information
echo Package file size information:
for %%f in ("publish-single-file-%rid%\EncryptTools.exe") do set fileSize=%%~zf
echo Single file size: %fileSize% bytes (approximately %fileSize:~0,-6% MB)
echo.

REM Ask if open publish folder
set /p openFolder="Open publish folder? (Y/N): "
if /i "%openFolder%"=="Y" (
    explorer "publish-single-file-%rid%"
)

echo Press any key to exit...
pause >nul