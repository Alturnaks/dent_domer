@echo off
rem Dental (ABP) - stop. Options: -KeepInfra -Wipe
cd /d "%~dp0"
where pwsh >nul 2>nul && (set "PS=pwsh") || (set "PS=powershell")
%PS% -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\stop-abp.ps1" %*
