# Остановка Dental Admin.
#   stop.bat           - закрыть API/воркер/фронт и остановить контейнеры (данные сохраняются)
#   stop.bat -Purge    - то же + удалить контейнеры и тома (БД будет пустой)
param(
    [switch]$KeepInfra,
    [switch]$Purge
)

try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }
$root = Split-Path $PSScriptRoot -Parent
$pidFile = Join-Path $root ".run\pids.json"

if (Test-Path $pidFile) {
    $pids = Get-Content -Raw $pidFile | ConvertFrom-Json
    foreach ($p in $pids.PSObject.Properties) {
        if ($p.Value) {
            # /T - вместе с дочерними процессами (dotnet, node)
            taskkill /PID $p.Value /T /F *> $null
            Write-Host "   остановлен: $($p.Name) (PID $($p.Value))"
        }
    }
    Remove-Item $pidFile -Force
} else {
    Write-Host "   запущенных окон не найдено"
}

if (-not $KeepInfra -and (Test-Path (Join-Path $root "docker-compose.yml"))) {
    Push-Location $root
    if ($Purge) {
        docker compose down -v
        Write-Host "   контейнеры и тома удалены" -ForegroundColor Yellow
    } else {
        docker compose stop
        Write-Host "   контейнеры остановлены (данные сохранены)"
    }
    Pop-Location
}
