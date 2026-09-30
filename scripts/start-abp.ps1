# Запуск ABP-версии Dental (abp/Dental).
#   start-abp.bat          — всё в Docker: PostgreSQL, Redis, DbMigrator (миграции+seed), Web на http://localhost:3100
#   start-abp.bat -Local   — PostgreSQL в Docker, DbMigrator и Web локально (.NET 10 SDK + Node 24 для клиентских библиотек)
#   -Rebuild               — пересобрать образы/контейнеры;  -NoCheck — без проверки входов;  -NoBrowser — не открывать браузер
# Логи: .run\start-abp.log, .run\abp-compose.log, .run\abp-web.log (Local), отчёт проверки: .run\abp-check-report.txt
# Совместим с Windows PowerShell 5.1 и PowerShell 7+.
param(
    [switch]$Local,
    [switch]$Rebuild,
    [switch]$NoCheck,
    [switch]$NoBrowser,
    [int]$WebPort = 3100
)

$ErrorActionPreference = "Continue"
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }

$root = Split-Path $PSScriptRoot -Parent
$abpDir = Join-Path $root "abp"
$sln = Join-Path $abpDir "Dental"
$compose = Join-Path $abpDir "docker-compose.yml"
$runDir = Join-Path $root ".run"
$pidFile = Join-Path $runDir "abp-pids.json"
$webUrl = "http://localhost:$WebPort"
$tenant = "dental-plus"

function Step([string]$t) { Write-Host ""; Write-Host "==> $t" -ForegroundColor Cyan }
function Fail([string]$t) { Write-Host ""; Write-Host "ОШИБКА: $t" -ForegroundColor Red; Write-Host "Лог: .run\start-abp.log"; try { Stop-Transcript | Out-Null } catch { }; exit 1 }
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

if (-not (Test-Path $runDir)) { New-Item -ItemType Directory -Path $runDir | Out-Null }
try { Start-Transcript -Path (Join-Path $runDir "start-abp.log") -Force | Out-Null } catch { }
Write-Host "=== Dental (ABP): запуск ===" -ForegroundColor Cyan

Step "Проверяю окружение"
if (-not (Has "docker")) { Fail "Docker не найден. Установите Docker Desktop." }
docker info *> $null
if ($LASTEXITCODE -ne 0) { Fail "Docker Desktop не запущен." }
Write-Host "   Docker - ок" -ForegroundColor Green

if (-not $Local) {
    Step "Собираю и запускаю контейнеры (первый запуск 5-15 минут)"
    $args1 = @("compose", "-f", $compose, "up", "-d", "--build")
    if ($Rebuild) { $args1 += "--force-recreate" }
    $env:ABP_WEB_PORT = "$WebPort"
    & docker @args1 2>&1 | ForEach-Object { "$_" } | Tee-Object -FilePath (Join-Path $runDir "abp-compose.log")
    if ($LASTEXITCODE -ne 0) {
        docker compose -f $compose logs --tail 60 migrator web 2>$null
        Fail "docker compose up завершился с ошибкой. Лог: .run\abp-compose.log"
    }
} else {
    if (-not (Has "dotnet")) { Fail ".NET 10 SDK не найден." }
    if (Test-Path $pidFile) { & (Join-Path $PSScriptRoot "stop-abp.ps1") -KeepInfra }

    Step "Поднимаю PostgreSQL и Redis в Docker"
    docker compose -f $compose up -d postgres redis
    if ($LASTEXITCODE -ne 0) { Fail "docker compose up завершился с ошибкой." }
    $ok = $false
    Write-Host -NoNewline "   жду PostgreSQL"
    for ($i = 0; $i -lt 40; $i++) {
        docker compose -f $compose exec -T postgres pg_isready -U abp -d dental_abp *> $null
        if ($LASTEXITCODE -eq 0) { $ok = $true; break }
        Write-Host -NoNewline "."; Start-Sleep -Seconds 2
    }
    if (-not $ok) { Fail "PostgreSQL не поднялся." }
    Write-Host " - готово" -ForegroundColor Green

    $env:ConnectionStrings__Default = "Host=localhost;Port=55433;Database=dental_abp;Username=abp;Password=abp"
    $env:App__SelfUrl = $webUrl
    $env:AuthServer__Authority = $webUrl

    $libs = Join-Path $sln "src\Dental.Web\wwwroot\libs"
    if (-not (Test-Path (Join-Path $libs "abp"))) {
        Step "Клиентские библиотеки (wwwroot/libs)"
        if (-not (Has "node")) { Fail "Node.js не найден. Нужен Node 24 (с Node 18 install-libs падает)." }
        $nodeMajor = [int]((node --version).TrimStart('v').Split('.')[0])
        if ($nodeMajor -lt 20) { Fail "Node $(node --version) слишком старый. Установите Node 24." }
        Push-Location (Join-Path $sln "src\Dental.Web")
        if (Has "yarn") { yarn install --non-interactive } else { npm install --no-audit --no-fund }
        $code = $LASTEXITCODE
        if ($code -eq 0) { node (Join-Path $abpDir "docker\install-libs.js") . ; $code = $LASTEXITCODE }
        Pop-Location
        if ($code -ne 0) { Fail "Установка клиентских библиотек не удалась." }
    }

    Step "Собираю решение"
    dotnet build (Join-Path $sln "Dental.slnx") -c Debug --nologo -v q
    if ($LASTEXITCODE -ne 0) { Fail "dotnet build упал." }

    Step "Миграции и демо-данные (DbMigrator)"
    dotnet run --no-build --project (Join-Path $sln "src\Dental.DbMigrator") 2>&1 | Tee-Object -FilePath (Join-Path $runDir "abp-migrator.log")
    if ($LASTEXITCODE -ne 0) { Fail "DbMigrator завершился с ошибкой. Лог: .run\abp-migrator.log" }

    Step "Запускаю Web"
    $webLog = Join-Path $runDir "abp-web.log"
    $env:ASPNETCORE_ENVIRONMENT = "Development"
    $p = Start-Process -FilePath "dotnet" -ArgumentList @("run", "--no-build", "--project", (Join-Path $sln "src\Dental.Web"), "--urls", $webUrl) `
        -RedirectStandardOutput $webLog -RedirectStandardError (Join-Path $runDir "abp-web.err.log") -PassThru -WindowStyle Hidden
    @{ web = $p.Id } | ConvertTo-Json | Set-Content -Path $pidFile -Encoding UTF8
}

Step "Жду готовности"
$webOk = Wait-Http "$webUrl/health-status" 300 "сайт"
if (-not $webOk) {
    if (-not $Local) { docker compose -f $compose logs --tail 80 web }
    Fail "Сайт не ответил."
}

Write-Host ""
Write-Host "Сайт:      $webUrl   (Swagger: $webUrl/swagger)" -ForegroundColor Green
Write-Host "Клиника:   арендатор «$tenant» (на странице входа: «Сменить» -> $tenant)"
Write-Host "Логины:    owner@demo.kz, senior1@demo.kz, admin1@demo.kz, doctor1@demo.kz ... / пароль demo12345"
Write-Host "Хост:      admin / 1q2w3E*  (без арендатора: управление арендаторами)"
Write-Host "Остановка: stop-abp.bat"

if (-not $NoBrowser) { Start-Process "$webUrl/Account/Login?__tenant=$tenant" }

if (-not $NoCheck) {
    Step "Проверяю входы под демо-пользователями"
    $report = @()
    $fail = 0
    $users = @("owner", "senior1", "senior2", "admin1", "admin2", "admin3", "doctor1", "doctor2", "doctor3", "doctor4", "doctor5", "doctor6")
    foreach ($u in $users) {
        $login = "$u@demo.kz"
        try {
            $tok = Invoke-RestMethod -Method Post -Uri "$webUrl/connect/token" -Headers @{ "__tenant" = $tenant } `
                -ContentType "application/x-www-form-urlencoded" `
                -Body "grant_type=password&client_id=Dental_App&username=$login&password=demo12345&scope=Dental"
            $cfg = Invoke-RestMethod -Uri "$webUrl/api/abp/application-configuration" -Headers @{ Authorization = "Bearer $($tok.access_token)" }
            $perms = @($cfg.auth.grantedPolicies.PSObject.Properties | Where-Object { $_.Name -like "Dental.*" -and $_.Value }).Count
            $line = "PASS  $login  роли: $($cfg.currentUser.roles -join ', ')  прав Dental: $perms"
            Write-Host "   $line" -ForegroundColor Green
        } catch {
            $fail++
            $line = "FAIL  $login  $($_.Exception.Message)"
            Write-Host "   $line" -ForegroundColor Red
        }
        $report += $line
    }
    $report += "Итого FAIL: $fail"
    $report | Set-Content -Path (Join-Path $runDir "abp-check-report.txt") -Encoding UTF8
    Write-Host "   отчёт: .run\abp-check-report.txt"
    try { Stop-Transcript | Out-Null } catch { }
    if ($fail -gt 0) { exit 1 }
}
try { Stop-Transcript | Out-Null } catch { }
