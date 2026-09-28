@echo off
rem One-time download of NuGet packages for offline build (result: .run\nuget-packages.zip)
cd /d "%~dp0"
where pwsh >nul 2>nul && (set "PS=pwsh") || (set "PS=powershell")
%PS% -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\fetch-packages.ps1"
echo.
pause
