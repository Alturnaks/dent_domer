@echo off
rem Dental Admin - check site health and login for every role (report: .run\check-report.txt)
cd /d "%~dp0"
where pwsh >nul 2>nul && (set "PS=pwsh") || (set "PS=powershell")
%PS% -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\check-roles.ps1" %*
set "CODE=%ERRORLEVEL%"
echo.
pause
exit /b %CODE%
