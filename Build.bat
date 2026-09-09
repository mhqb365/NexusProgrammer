@echo off
setlocal

set "ROOT=%~dp0"
set "PROJECT=%ROOT%NexusProgrammer.csproj"
set "BUILD_ROOT=%ROOT%build"
set "CONFIGURATION=Release"
set "TARGET_FRAMEWORK=net8.0-windows"

for /f "usebackq delims=" %%v in (`powershell -NoProfile -ExecutionPolicy Bypass -Command "([xml](Get-Content -LiteralPath '%PROJECT%')).Project.PropertyGroup.Version"`) do set "APP_VERSION=%%v"

if "%APP_VERSION%"=="" (
    echo Could not read Version from NexusProgrammer.csproj.
    exit /b 1
)

set "OUTPUT_DIR=%BUILD_ROOT%\NexusProgrammer_v%APP_VERSION%"
set "SOURCE_DIR=%ROOT%bin\%CONFIGURATION%\%TARGET_FRAMEWORK%"

echo Building Nexus Programmer v%APP_VERSION%...
echo Output: %OUTPUT_DIR%

dotnet build "%PROJECT%" -c %CONFIGURATION% -p:UseSharedCompilation=false
if errorlevel 1 (
    echo Build failed.
    exit /b 1
)

if not exist "%SOURCE_DIR%" (
    echo Build output folder not found: %SOURCE_DIR%
    exit /b 1
)

if exist "%OUTPUT_DIR%" (
    rmdir /s /q "%OUTPUT_DIR%"
    if exist "%OUTPUT_DIR%" (
        echo Cannot replace output folder. Close any running app from:
        echo %OUTPUT_DIR%
        exit /b 1
    )
)

mkdir "%OUTPUT_DIR%" >nul
if errorlevel 1 (
    echo Cannot create output folder: %OUTPUT_DIR%
    exit /b 1
)
robocopy "%SOURCE_DIR%" "%OUTPUT_DIR%" /E >nul
if errorlevel 8 (
    echo Copy failed.
    exit /b 1
)

echo.
echo Build completed: %OUTPUT_DIR%
endlocal
