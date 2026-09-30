@echo off
rem Dental (ABP) - start. Options: -Local -Rebuild -NoCheck -NoBrowser
cd /d "%~dp0"
where pwsh >nul 2>nul && (set "PS=pwsh") || (set "PS=powershell")
%PS% -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\start-abp.ps1" %*
set "CODE=%ERRORLEVEL%"
echo.
pause
exit /b %CODE%
