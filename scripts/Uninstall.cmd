@echo off
rem RobloxGuard uninstaller launcher. ASCII-only (cmd.exe dislikes BOM/UTF-8).
rem Double-click this file. It will ask for administrator permission, then remove everything.
setlocal
cd /d "%~dp0"

net session >nul 2>&1
if %errorlevel% neq 0 (
    echo Requesting administrator permission. Please click "Yes".
    powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0uninstall.ps1"
echo.
echo ----------------------------------------------------------
echo Finished. You can close this window.
pause
