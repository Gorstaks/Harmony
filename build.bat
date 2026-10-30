@echo off
echo =============================================
echo   Harmony - Build Script (.NET Framework 4.8)
echo =============================================
echo.

dotnet build Harmony.csproj -c Release
if errorlevel 1 (
    echo BUILD FAILED
    pause
    exit /b 1
)

echo.
echo =============================================
echo   Build complete: bin\Release\net48\Harmony.exe
echo =============================================
pause
