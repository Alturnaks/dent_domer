# Остановка ABP-версии Dental. -KeepInfra — оставить PostgreSQL/Redis; -Wipe — удалить данные БД (volume).
param([switch]$KeepInfra, [switch]$Wipe)
$root = Split-Path $PSScriptRoot -Parent
$compose = Join-Path $root "abp\docker-compose.yml"
$pidFile = Join-Path $root ".run\abp-pids.json"
if (Test-Path $pidFile) {
    $pids = Get-Content $pidFile -Raw | ConvertFrom-Json
    foreach ($p in $pids.PSObject.Properties) {
        try { Stop-Process -Id $p.Value -Force -ErrorAction Stop; Write-Host "остановлен $($p.Name) (PID $($p.Value))" } catch { }
    }
    Remove-Item $pidFile -Force
}
if ($KeepInfra) {
    docker compose -f $compose stop web migrator 2>$null | Out-Null
} elseif ($Wipe) {
    docker compose -f $compose down -v
} else {
    docker compose -f $compose down
}
