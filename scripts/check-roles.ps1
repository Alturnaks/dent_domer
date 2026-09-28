# Проверка работоспособности сайта и входа под каждой ролью.
# Совместим с Windows PowerShell 5.1 и PowerShell 7+.
# Запуск: check-roles.bat  (или powershell -ExecutionPolicy Bypass -File scripts\check-roles.ps1)
param(
    [string]$ApiUrl = "http://localhost:5100",
    [string]$WebUrl = "http://localhost:3100",
    [string]$Matrix = (Join-Path $PSScriptRoot "roles-matrix.json"),
    [switch]$SkipInfra
)

$ErrorActionPreference = "Stop"
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }

$root = Split-Path $PSScriptRoot -Parent
$runDir = Join-Path $root ".run"
if (-not (Test-Path $runDir)) { New-Item -ItemType Directory -Path $runDir | Out-Null }
$reportPath = Join-Path $runDir "check-report.txt"

$results = New-Object System.Collections.ArrayList
$today = (Get-Date).ToString("yyyy-MM-dd")
$monthStart = (Get-Date -Day 1).ToString("yyyy-MM-dd")

function Add-Result([string]$Group, [string]$Name, [string]$Status, [string]$Detail) {
    [void]$results.Add([pscustomobject]@{ Group = $Group; Check = $Name; Status = $Status; Detail = $Detail })
    $color = switch ($Status) { "PASS" { "Green" } "FAIL" { "Red" } default { "Yellow" } }
    $line = "  [{0}] {1}" -f $Status, $Name
    if ($Detail) { $line += "  - $Detail" }
    Write-Host $line -ForegroundColor $color
}

# Универсальный HTTP-вызов: возвращает статус (0 = нет соединения) и тело.
function Invoke-Http([string]$Method, [string]$Url, $Body = $null, [string]$Token = $null) {
    $headers = @{}
    if ($Token) { $headers["Authorization"] = "Bearer $Token" }
    $params = @{ Method = $Method; Uri = $Url; Headers = $headers; UseBasicParsing = $true; TimeoutSec = 15 }
    if ($null -ne $Body) {
        $params["Body"] = [System.Text.Encoding]::UTF8.GetBytes(($Body | ConvertTo-Json -Compress))
        $params["ContentType"] = "application/json; charset=utf-8"
    }
    try {
        $r = Invoke-WebRequest @params
        return [pscustomobject]@{ Status = [int]$r.StatusCode; Content = $r.Content }
    } catch {
        $resp = $_.Exception.Response
        if ($null -eq $resp) { return [pscustomobject]@{ Status = 0; Content = $_.Exception.Message } }
        $content = $null
        if ($_.ErrorDetails) { $content = $_.ErrorDetails.Message }
        return [pscustomobject]@{ Status = [int]$resp.StatusCode; Content = $content }
    }
}

function From-Json([string]$Text) {
    if (-not $Text) { return $null }
    try { return $Text | ConvertFrom-Json } catch { return $null }
}

function Short([string]$Text) {
    if (-not $Text) { return "" }
    $ej = From-Json $Text
    if ($ej -and $ej.error) { return "$($ej.error.code): $($ej.error.message)" }
    $t = $Text -replace "\s+", " "
    if ($t.Length -gt 160) { $t = $t.Substring(0, 160) + "..." }
    return $t
}

$cfg = Get-Content -Raw -Encoding UTF8 $Matrix | ConvertFrom-Json

Write-Host ""
Write-Host "=== Dental Admin: проверка сайта и ролей ===" -ForegroundColor Cyan
Write-Host "API: $ApiUrl   WEB: $WebUrl"

# ---------- 1. Инфраструктура и сайт ----------
Write-Host ""
Write-Host "[Сайт и сервисы]" -ForegroundColor Cyan
$g = "Сайт"
$h = Invoke-Http GET "$ApiUrl/health"
if ($h.Status -eq 200) { Add-Result $g "API /health" "PASS" "" }
elseif ($h.Status -eq 0) { Add-Result $g "API /health" "FAIL" "API не отвечает: $(Short $h.Content)" }
else { Add-Result $g "API /health" "FAIL" "HTTP $($h.Status)" }
$apiUp = $h.Status -ne 0

$o = Invoke-Http GET "$ApiUrl/openapi/v1.json"
if ($o.Status -eq 200) { Add-Result $g "OpenAPI /openapi/v1.json" "PASS" "" }
elseif ($o.Status -eq 0) { Add-Result $g "OpenAPI /openapi/v1.json" "FAIL" "API не отвечает" }
else { Add-Result $g "OpenAPI /openapi/v1.json" "FAIL" "HTTP $($o.Status)" }

$w = Invoke-Http GET "$WebUrl/login"
if ($w.Status -eq 200) { Add-Result $g "Frontend /login" "PASS" "" }
elseif ($w.Status -eq 0) { Add-Result $g "Frontend /login" "FAIL" "фронт не отвечает" }
else { Add-Result $g "Frontend /login" "FAIL" "HTTP $($w.Status)" }

if (-not $SkipInfra) {
    $m = Invoke-Http GET "http://localhost:58025"
    if ($m.Status -eq 200) { Add-Result $g "Mailpit :58025" "PASS" "" } else { Add-Result $g "Mailpit :58025" "WARN" "не отвечает (нужен для писем)" }
}

# ---------- 2. Негативные проверки auth ----------
if ($apiUp) {
    Write-Host ""
    Write-Host "[Аутентификация]" -ForegroundColor Cyan
    $g = "Auth"
    $bad = Invoke-Http POST "$ApiUrl/api/v1/auth/login" @{ login = "owner@demo.kz"; password = "wrong-password" }
    if ($bad.Status -eq 401) { Add-Result $g "Неверный пароль -> 401" "PASS" "" }
    else { Add-Result $g "Неверный пароль -> 401" "FAIL" "получено HTTP $($bad.Status)" }

    $anon = Invoke-Http GET "$ApiUrl/api/v1/auth/me"
    if ($anon.Status -eq 401) { Add-Result $g "/auth/me без токена -> 401" "PASS" "" }
    else { Add-Result $g "/auth/me без токена -> 401" "FAIL" "получено HTTP $($anon.Status)" }
}

# ---------- 3. Вход под каждой ролью ----------
if ($apiUp) {
    foreach ($role in $cfg.roles) {
        Write-Host ""
        Write-Host ("[Роль: {0} ({1})]" -f $role.title, $role.code) -ForegroundColor Cyan
        $firstToken = $null
        foreach ($login in $role.logins) {
            $g = "$($role.title)"
            $r = Invoke-Http POST "$ApiUrl/api/v1/auth/login" @{ login = $login; password = $cfg.password }
            $tok = $null
            $j = From-Json $r.Content
            if ($j) { $tok = $j.accessToken }
            if ($r.Status -eq 200 -and $tok) {
                Add-Result $g "Вход $login" "PASS" ""
                if (-not $firstToken) { $firstToken = $tok }
            } elseif ($r.Status -eq 200) {
                Add-Result $g "Вход $login" "FAIL" "200, но нет accessToken в ответе"
            } else {
                Add-Result $g "Вход $login" "FAIL" "HTTP $($r.Status) $(Short $r.Content)"
            }
        }
        if (-not $firstToken) { continue }

        # /auth/me: роль, права, лимиты
        $me = Invoke-Http GET "$ApiUrl/api/v1/auth/me" $null $firstToken
        $mj = From-Json $me.Content
        if ($me.Status -ne 200 -or -not $mj) {
            Add-Result $g "/auth/me" "FAIL" "HTTP $($me.Status)"
            continue
        }
        Add-Result $g "/auth/me" "PASS" ""

        $roleCode = $null
        if ($mj.role) { $roleCode = $mj.role.code }
        if ($roleCode -eq $role.code) { Add-Result $g "Код роли = $($role.code)" "PASS" "" }
        else { Add-Result $g "Код роли = $($role.code)" "FAIL" "получено '$roleCode'" }

        $perms = @()
        if ($mj.permissions) { $perms = @($mj.permissions) }
        $missing = @($role.mustHave | Where-Object { $perms -notcontains $_ })
        $extra = @($role.mustNotHave | Where-Object { $perms -contains $_ })
        if ($missing.Count -eq 0) { Add-Result $g "Есть нужные права ($($role.mustHave.Count))" "PASS" "" }
        else { Add-Result $g "Есть нужные права" "FAIL" ("нет: " + ($missing -join ", ")) }
        if ($role.mustNotHave.Count -gt 0) {
            if ($extra.Count -eq 0) { Add-Result $g "Нет лишних прав ($($role.mustNotHave.Count))" "PASS" "" }
            else { Add-Result $g "Нет лишних прав" "FAIL" ("лишние: " + ($extra -join ", ")) }
        }

        if ($role.limits) {
            foreach ($p in $role.limits.PSObject.Properties) {
                $expected = $p.Value
                $actual = $null
                $has = $false
                if ($mj.limits -and ($mj.limits.PSObject.Properties.Name -contains $p.Name)) { $actual = $mj.limits.($p.Name); $has = $true }
                $ok = $has -and ((($null -eq $expected) -and ($null -eq $actual)) -or (($null -ne $expected) -and ($null -ne $actual) -and ([decimal]$actual -eq [decimal]$expected)))
                $expStr = if ($null -eq $expected) { "без лимита (null)" } else { "$expected" }
                $actStr = if (-not $has) { "нет поля" } elseif ($null -eq $actual) { "null" } else { "$actual" }
                if ($ok) { Add-Result $g "Лимит $($p.Name) = $expStr" "PASS" "" }
                else { Add-Result $g "Лимит $($p.Name) = $expStr" "FAIL" "получено $actStr" }
            }
        }

        # Доступ к эндпоинтам (404 = ещё не реализован на текущем этапе)
        foreach ($ep in $role.endpoints) {
            $path = $ep.path.Replace("{today}", $today).Replace("{monthStart}", $monthStart)
            $resp = Invoke-Http $ep.method "$ApiUrl$path" $null $firstToken
            $name = "$($ep.method) $($ep.path.Split('?')[0]) -> $($ep.expect)"
            if ($resp.Status -eq $ep.expect) { Add-Result $g $name "PASS" "" }
            elseif ($resp.Status -eq 404 -or $resp.Status -eq 405) { Add-Result $g $name "SKIP" "эндпоинт ещё не реализован" }
            else { Add-Result $g $name "FAIL" "получено HTTP $($resp.Status)" }
        }
    }
}

# ---------- Итог ----------
$pass = @($results | Where-Object { $_.Status -eq "PASS" }).Count
$fail = @($results | Where-Object { $_.Status -eq "FAIL" }).Count
$skip = @($results | Where-Object { $_.Status -eq "SKIP" -or $_.Status -eq "WARN" }).Count

Write-Host ""
$summary = "Итог: PASS $pass, FAIL $fail, SKIP/WARN $skip"
if ($fail -eq 0) { Write-Host $summary -ForegroundColor Green } else { Write-Host $summary -ForegroundColor Red }

$lines = @("Dental Admin - проверка $(Get-Date -Format 'dd.MM.yyyy HH:mm')", "API: $ApiUrl  WEB: $WebUrl", "")
$lines += ($results | ForEach-Object { "[{0}] {1} | {2} {3}" -f $_.Status, $_.Group, $_.Check, $(if ($_.Detail) { "- " + $_.Detail } else { "" }) })
$lines += ""
$lines += $summary
Set-Content -Path $reportPath -Value $lines -Encoding UTF8
Write-Host "Отчёт: $reportPath"

if ($fail -gt 0) { exit 1 } else { exit 0 }
