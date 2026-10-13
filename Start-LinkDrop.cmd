@echo off
cd /d "%~dp0"
if not exist tools\yt-dlp.exe goto setup
if not exist tools\ffmpeg.exe goto setup
if not exist tools\ffprobe.exe goto setup
if not exist tools\deno.exe goto setup
goto run
:setup
call SetupTools.cmd
if errorlevel 1 exit /b 1
:run
start "" "%~dp0LinkDrop.exe"
