@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0inspect-community-panel.ps1"
echo.
pause
