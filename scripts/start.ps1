# Запуск Dental Admin для разработки на Windows.
# Инфраструктура (postgres, redis, mailpit, minio, seq) - в Docker,
# API и фронтенд - локально в отдельных окнах.
# Совместим с Windows PowerShell 5.1 и PowerShell 7+.
param(
    [switch]$NoSeed,      # не запускать seed
    [switch]$Worker,      # запустить воркер Hangfire (появится на этапе 3)
    [switch]$NoCheck,     # не запускать проверку ролей после старта
    [switch]$NoBrowser,   # не открывать браузер
    [int]$ApiPort = 5000,
    [int]$WebPort = 3000
)

$ErrorActionPreference = "Stop"
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }

$root = Split-Path $PSScriptRoot -Parent
$runDir = Join-Path $root ".run"
$pidFile = Join-Path $runDir "pids.json"
$apiProject = Join-Path $root "backend\src\Dental.Api"
$frontend = Join-Path $root "frontend"
$apiUrl = "http://localhost:$ApiPort"
$webUrl = "http://localhost:$WebPort"

function Step([string]$t) { Write-Host ""; Write-Host "==> $t" -ForegroundColor Cyan }
function Fail([string]$t) { Write-Host ""; Write-Host "ОШИБКА: $t" -ForegroundColor Red; exit 1 }
function Has([string]$cmd) { return [bool](Get-Command $cmd -ErrorAction SilentlyContinue) }

function Wait-Http([string]$Url, [int]$Seconds, [string]$Name) {
    $deadline = (Get-Date).AddSeconds($Seconds)
    Write-Host -NoNewline "   жду $Name ($Url)"
    while ((Get-Date) -lt $deadline) {
        try {
            $r = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 5
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
Write-Host "=== Dental Admin: запуск ===" -ForegroundColor Cyan

# ---------- 1. Проверка окружения ----------
Step "Проверяю окружение"
if (-not (Test-Path (Join-Path $root "docker-compose.yml"))) {
    Fail "Нет docker-compose.yml. Проект ещё не собран - сначала выполните Этап 1 через Claude Code (см. README.md)."
}
if (-not (Test-Path $apiProject)) { Fail "Нет backend\src\Dental.Api. Этап 1 ещё не выполнен." }
if (-not (Has "docker")) { Fail "Docker не найден. Установите Docker Desktop." }
docker info *> $null
if ($LASTEXITCODE -ne 0) { Fail "Docker Desktop не запущен. Запустите его и повторите." }
if (-not (Has "dotnet")) { Fail ".NET SDK не найден. Установите .NET 10 SDK." }
$sdks = (dotnet --list-sdks) -join "`n"
if ($sdks -notmatch "(?m)^10\.") { Fail ".NET 10 SDK не найден. Установлены: `n$sdks" }
$hasWeb = Test-Path (Join-Path $frontend "package.json")
if ($hasWeb -and -not (Has "npm")) { Fail "Node.js/npm не найден. Установите Node.js 22+." }
Write-Host "   docker, dotnet 10, node - ок" -ForegroundColor Green

# Уже запущено? Остановим прошлые окна.
if (Test-Path $pidFile) {
    Write-Host "   найдены процессы прошлого запуска - останавливаю" -ForegroundColor Yellow
    & (Join-Path $PSScriptRoot "stop.ps1") -KeepInfra
}
if (-not (Test-Path $runDir)) { New-Item -ItemType Directory -Path $runDir | Out-Null }

# .env
$envFile = Join-Path $root ".env"
$envExample = Join-Path $root ".env.example"
if (-not (Test-Path $envFile) -and (Test-Path $envExample)) {
    Copy-Item $envExample $envFile
    Write-Host "   создан .env из .env.example" -ForegroundColor Yellow
}

# ---------- 2. Инфраструктура ----------
Step "Поднимаю инфраструктуру в Docker"
docker compose up -d postgres redis mailpit minio seq
if ($LASTEXITCODE -ne 0) { Fail "docker compose up завершился с ошибкой." }

Write-Host -NoNewline "   жду PostgreSQL"
$ok = $false
for ($i = 0; $i -lt 40; $i++) {
    docker compose exec -T postgres pg_isready *> $null
    if ($LASTEXITCODE -eq 0) { $ok = $true; break }
    Write-Host -NoNewline "."
    Start-Sleep -Seconds 2
}
if (-not $ok) { Fail "PostgreSQL не поднялся за 80 секунд. Смотрите: docker compose logs postgres" }
Write-Host " - готово" -ForegroundColor Green

# ---------- 3. Сборка, миграции, seed ----------
Step "Собираю backend"
dotnet build (Join-Path $root "backend\Dental.sln") -c Debug --nologo -v q
if ($LASTEXITCODE -ne 0) { Fail "dotnet build упал. Исправьте ошибки сборки." }

Step "Применяю миграции"
dotnet run --no-build --project $apiProject -- migrate
if ($LASTEXITCODE -ne 0) { Fail "Миграции не применились." }

if (-not $NoSeed) {
    Step "Заполняю демо-данные (seed)"
    dotnet run --no-build --project $apiProject -- seed
    if ($LASTEXITCODE -ne 0) { Fail "Seed завершился с ошибкой." }
}

# ---------- 4. API, воркер, фронтенд ----------
$pids = @{}
Step "Запускаю API на $apiUrl"
$env:ASPNETCORE_ENVIRONMENT = "Development"
$pids.api = Start-Window "Dental API" $root "`$env:ASPNETCORE_ENVIRONMENT='Development'; dotnet run --no-build --project '$apiProject' --urls $apiUrl"

if ($Worker) {
    Step "Запускаю воркер Hangfire"
    $pids.worker = Start-Window "Dental Worker" $root "`$env:ASPNETCORE_ENVIRONMENT='Development'; dotnet run --no-build --project '$apiProject' -- worker"
}

if ($hasWeb) {
    Step "Запускаю фронтенд на $webUrl"
    if (-not (Test-Path (Join-Path $frontend "node_modules"))) {
        Write-Host "   первый запуск - устанавливаю npm-зависимости"
        Push-Location $frontend
        if (Test-Path "package-lock.json") { npm ci } else { npm install }
        $code = $LASTEXITCODE
        Pop-Location
        if ($code -ne 0) { Fail "npm install завершился с ошибкой." }
    }
    $pids.web = Start-Window "Dental Web" $frontend "`$env:NEXT_PUBLIC_API_URL='$apiUrl'; npm run dev -- -p $WebPort"
} else {
    Write-Host "   frontend\package.json не найден - фронтенд пропущен" -ForegroundColor Yellow
}

$pids | ConvertTo-Json | Set-Content -Path $pidFile -Encoding UTF8

# ---------- 5. Ожидание готовности ----------
Step "Жду готовности сервисов"
$apiOk = Wait-Http "$apiUrl/health" 180 "API"
$webOk = $true
if ($hasWeb) { $webOk = Wait-Http "$webUrl/login" 240 "фронтенд" }

Write-Host ""
Write-Host "Сайт:      $webUrl" -ForegroundColor Green
Write-Host "API:       $apiUrl   (Scalar: $apiUrl/scalar)"
Write-Host "Mailpit:   http://localhost:8025"
Write-Host "Seq:       http://localhost:5341"
Write-Host "Логины:    owner@demo.kz, senior1@demo.kz, admin1@demo.kz, storekeeper@demo.kz, cashier@demo.kz, doctor1@demo.kz  / пароль demo12345"
Write-Host "Остановка: stop.bat"

if ($hasWeb -and $webOk -and -not $NoBrowser) { Start-Process "$webUrl/login" }

if (-not $NoCheck -and $apiOk) {
    Step "Проверяю сайт и входы под каждой ролью"
    & (Join-Path $PSScriptRoot "check-roles.ps1") -ApiUrl $apiUrl -WebUrl $webUrl
    exit $LASTEXITCODE
}
if (-not $apiOk) { exit 1 }
