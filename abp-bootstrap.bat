@echo off
rem One-time: create ABP solution (abp\) and download NuGet packages for Claude
cd /d "%~dp0"
where pwsh >nul 2>nul && (set "PS=pwsh") || (set "PS=powershell")
%PS% -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\abp-bootstrap.ps1"
echo.
pause
