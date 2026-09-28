@echo off
rem Dental Admin - start (infra in Docker, API + web locally, then role check)
rem Options: start.bat -NoSeed -Worker -NoCheck -NoBrowser
cd /d "%~dp0"
where pwsh >nul 2>nul && (set "PS=pwsh") || (set "PS=powershell")
%PS% -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\start.ps1" %*
set "CODE=%ERRORLEVEL%"
echo.
pause
exit /b %CODE%
