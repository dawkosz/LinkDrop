@echo off
cd /d "%~dp0"
echo Downloading verified media tools from official sources...
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Setup.ps1"
if errorlevel 1 (
  echo Setup failed. Check the error above and your internet connection.
  pause
  exit /b 1
)
echo Setup complete. Open Start-LinkDrop.cmd or LinkDrop.exe.
pause
