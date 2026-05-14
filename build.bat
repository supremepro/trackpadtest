@echo off
setlocal

echo ============================================================
echo  Trackpad Window Control - Build Script
echo ============================================================

:: Check for .NET SDK
where dotnet >nul 2>&1
if errorlevel 1 (
    echo ERROR: .NET SDK not found.
    echo Please install .NET 8 SDK from: https://dotnet.microsoft.com/download
    pause
    exit /b 1
)

echo Restoring packages...
dotnet restore TrackpadWindowControl.csproj
if errorlevel 1 goto :error

echo.
echo Building release (self-contained, single file)...
dotnet publish TrackpadWindowControl.csproj ^
    -c Release ^
    -r win-x64 ^
    --self-contained true ^
    -p:PublishSingleFile=true ^
    -p:IncludeNativeLibrariesForSelfExtract=true ^
    -o publish\

if errorlevel 1 goto :error

echo.
echo ============================================================
echo  Build successful!
echo  Output: publish\TrackpadWindowControl.exe
echo ============================================================
echo.
echo To install: copy TrackpadWindowControl.exe anywhere you like
echo             and run it.  Right-click the tray icon to enable
echo             auto-start with Windows.
goto :end

:error
echo.
echo ============================================================
echo  Build FAILED.  See errors above.
echo ============================================================

:end
pause
endlocal
