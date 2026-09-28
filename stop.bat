@echo off
rem Dental Admin - stop. Use "stop.bat -Purge" to also delete DB volumes.
cd /d "%~dp0"
where pwsh >nul 2>nul && (set "PS=pwsh") || (set "PS=powershell")
%PS% -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\stop.ps1" %*
pause
