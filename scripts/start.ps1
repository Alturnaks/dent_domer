# Запуск Dental Admin.
#   start.bat            — всё в Docker (нужен только Docker Desktop): БД, API, воркер, сайт
#   start.bat -Local     — режим разработчика: инфраструктура в Docker, API и сайт локально (нужны .NET 10 SDK и Node 22)
# Совместим с Windows PowerShell 5.1 и PowerShell 7+.
param(
    [switch]$Local,
    [switch]$NoCheck,
    [switch]$NoBrowser,
    [switch]$Rebuild,
    [int]$ApiPort = 5100,
    [int]$WebPort = 3100
)

$ErrorActionPreference = "Continue"  # нативные команды пишут прогресс в stderr; ошибки проверяем по $LASTEXITCODE
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }

$root = Split-Path $PSScriptRoot -Parent
$runDir = Join-Path $root ".run"
$pidFile = Join-Path $runDir "pids.json"
$apiProject = Join-Path $root "backend\src\Dental.Api"
$frontend = Join-Path $root "frontend"
$apiUrl = "http://localhost:$ApiPort"
$webUrl = "http://localhost:$WebPort"

function Step([string]$t) { Write-Host ""; Write-Host "==> $t" -ForegroundColor Cyan }
function Fail([string]$t) { Write-Host ""; Write-Host "ОШИБКА: $t" -ForegroundColor Red; Write-Host "Лог: .run\start.log"; try { Stop-Transcript | Out-Null } catch { }; exit 1 }
function Has([string]$cmd) { return [bool](Get-Command $cmd -ErrorAction SilentlyContinue) }

function Wait-Http([string]$Url, [int]$Seconds, [string]$Name) {
    $deadline = (Get-Date).AddSeconds($Seconds)
    Write-Host -NoNewline "   жду $Name ($Url)"
    while ((Get-Date) -lt $deadline) {
        try {
            $r = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 5 -ErrorAction Stop
            if ([int]$r.StatusCode -lt 500) { Write-Host " - готово" -ForegroundColor Green; return $true }
        } catch {
            if ($_.Exception.Response -and [int]$_.Exception.Response.StatusCode -lt 500) { Write-Host " - готово" -ForegroundColor Green; return $true }
        }
        Write-Host -NoNewline "."
        Start-Sleep -Seconds 3
    }
    Write-Host " - не дождался" -ForegroundColor Red
    return $false
}

function Start-Window([string]$Title, [string]$WorkDir, [string]$Command) {
    $full = "`$Host.UI.RawUI.WindowTitle = '$Title'; Set-Location '$WorkDir'; $Command"
    $exe = if (Has "pwsh") { "pwsh" } else { "powershell" }
    $p = Start-Process -FilePath $exe -ArgumentList @("-NoExit", "-ExecutionPolicy", "Bypass", "-Command", $full) -PassThru
    return $p.Id
}

Set-Location $root
if (-not (Test-Path $runDir)) { New-Item -ItemType Directory -Path $runDir | Out-Null }
$logFile = Join-Path $runDir "start.log"
try { Start-Transcript -Path $logFile -Force | Out-Null } catch { }
Write-Host "=== Dental Admin: запуск ===" -ForegroundColor Cyan

# ---------- Проверка окружения ----------
Step "Проверяю окружение"
if (-not (Test-Path (Join-Path $root "docker-compose.yml"))) { Fail "Нет docker-compose.yml в $root." }
if (-not (Has "docker")) { Fail "Docker не найден. Установите Docker Desktop: https://www.docker.com/products/docker-desktop/" }
docker info *> $null
if ($LASTEXITCODE -ne 0) { Fail "Docker Desktop не запущен. Запустите его, дождитесь зелёного статуса и повторите." }
Write-Host "   Docker - ок" -ForegroundColor Green

if (-not (Test-Path $runDir)) { New-Item -ItemType Directory -Path $runDir | Out-Null }
$envFile = Join-Path $root ".env"
$envExample = Join-Path $root ".env.example"
if (-not (Test-Path $envFile) -and (Test-Path $envExample)) {
    Copy-Item $envExample $envFile
    Write-Host "   создан .env из .env.example" -ForegroundColor Yellow
}

if (-not $Local) {
    # ---------- Всё в Docker ----------
    Step "Собираю и запускаю контейнеры (первый запуск занимает 5-15 минут)"
    $composeArgs = @("compose", "up", "-d", "--build")
    if ($Rebuild) { $composeArgs += "--force-recreate" }
    & docker @composeArgs 2>&1 | ForEach-Object { "$_" } | Tee-Object -FilePath (Join-Path $runDir "compose.log")
    if ($LASTEXITCODE -ne 0) {
        Write-Host ""
        Write-Host "Последние строки логов:" -ForegroundColor Yellow
        docker compose logs --tail 40 migrate seed api 2>$null
        Fail "docker compose up завершился с ошибкой. Полный лог: docker compose logs"
    }
} else {
    # ---------- Режим разработчика ----------
    if (-not (Has "dotnet")) { Fail ".NET SDK не найден. Установите .NET 10 SDK или запускайте без -Local." }
    $sdks = (dotnet --list-sdks) -join "`n"
    if ($sdks -notmatch "(?m)^10\.") { Fail ".NET 10 SDK не найден. Установлены: `n$sdks" }
    if (-not (Has "npm")) { Fail "Node.js/npm не найден. Установите Node.js 22+." }
    if (Test-Path $pidFile) { & (Join-Path $PSScriptRoot "stop.ps1") -KeepInfra }

    Step "Поднимаю инфраструктуру в Docker"
    docker compose up -d postgres redis mailpit
    if ($LASTEXITCODE -ne 0) { Fail "docker compose up завершился с ошибкой." }
    Write-Host -NoNewline "   жду PostgreSQL"
    $ok = $false
    for ($i = 0; $i -lt 40; $i++) {
        docker compose exec -T postgres pg_isready -U postgres -d dental *> $null
        if ($LASTEXITCODE -eq 0) { $ok = $true; break }
        Write-Host -NoNewline "."
        Start-Sleep -Seconds 2
    }
    if (-not $ok) { Fail "PostgreSQL не поднялся за 80 секунд. Смотрите: docker compose logs postgres" }
    Write-Host " - готово" -ForegroundColor Green

    Step "Собираю backend"
    dotnet build (Join-Path $root "backend\Dental.sln") -c Debug --nologo -v q
    if ($LASTEXITCODE -ne 0) { Fail "dotnet build упал." }
    Step "Миграции и демо-данные"
    dotnet run --no-build --project $apiProject -- migrate
    if ($LASTEXITCODE -ne 0) { Fail "Миграции не применились." }
    dotnet run --no-build --project $apiProject -- seed
    if ($LASTEXITCODE -ne 0) { Fail "Seed завершился с ошибкой." }

    $pids = @{}
    $pids.api = Start-Window "Dental API" $root "`$env:ASPNETCORE_ENVIRONMENT='Development'; dotnet run --no-build --project '$apiProject' --urls $apiUrl"
    $pids.worker = Start-Window "Dental Worker" $root "`$env:ASPNETCORE_ENVIRONMENT='Development'; dotnet run --no-build --project '$apiProject' --urls http://localhost:5101 -- worker"
    if (-not (Test-Path (Join-Path $frontend "node_modules"))) {
        Push-Location $frontend
        npm ci
        $code = $LASTEXITCODE
        Pop-Location
        if ($code -ne 0) { Fail "npm ci завершился с ошибкой." }
    }
    $pids.web = Start-Window "Dental Web" $frontend "`$env:API_URL='$apiUrl'; npm run dev -- -p $WebPort"
    $pids | ConvertTo-Json | Set-Content -Path $pidFile -Encoding UTF8
}

# ---------- Ожидание готовности ----------
Step "Жду готовности сервисов"
$apiOk = Wait-Http "$apiUrl/health" 300 "API"
$webOk = Wait-Http "$webUrl/login" 300 "сайт"
if (-not $apiOk -and -not $Local) {
    docker compose ps
    docker compose logs --tail 60 api
}

Write-Host ""
Write-Host "Сайт:      $webUrl" -ForegroundColor Green
Write-Host "API:       $apiUrl   (документация: $apiUrl/scalar)"
Write-Host "Почта:     http://localhost:58025   Логи: http://localhost:55341   Задачи: http://localhost:5101/jobs"
Write-Host "Логины:    owner@demo.kz, senior1@demo.kz, admin1@demo.kz, storekeeper@demo.kz, cashier@demo.kz, doctor1@demo.kz  / пароль demo12345"
Write-Host "Остановка: stop.bat"

if ($webOk -and -not $NoBrowser) { Start-Process "$webUrl/login" }

if (-not $NoCheck -and $apiOk) {
    Step "Проверяю сайт и входы под каждой ролью"
    & (Join-Path $PSScriptRoot "check-roles.ps1") -ApiUrl $apiUrl -WebUrl $webUrl
    exit $LASTEXITCODE
}
if (-not $apiOk) { exit 1 }
