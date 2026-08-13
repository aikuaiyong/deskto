@echo off
echo ============================================
echo   deskto build script
echo   If it says "dotnet not found", install
echo   .NET 8 SDK first, then run this again.
echo ============================================
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1"
echo.
echo Done. Press any key to close.
pause >nul
