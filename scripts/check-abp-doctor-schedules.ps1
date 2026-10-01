# API smoke checks against the local demo tenant. Creates a weekly demo template;
# exceptions and blocks created by the check are soft-deleted afterwards.
param([string]$BaseUrl = 'http://localhost:3102')
$ErrorActionPreference = 'Stop'
if (-not ([Uri]$BaseUrl).IsLoopback) { throw 'This check only supports a local demo server.' }
$BaseUrl = $BaseUrl.TrimEnd('/')
$report = [System.Collections.Generic.List[string]]::new()
$runDir = Join-Path (Split-Path $PSScriptRoot -Parent) '.run'
New-Item -ItemType Directory -Path $runDir -Force | Out-Null
function Pass([string]$message) { $report.Add("PASS $message"); Write-Host "PASS $message" }
function Check([bool]$condition, [string]$message) { if (-not $condition) { throw $message }; Pass $message }
function Login([string]$name) {
    $token = Invoke-RestMethod -Method Post -Uri "$BaseUrl/connect/token" -Headers @{ __tenant = 'dental-plus' } `
        -ContentType 'application/x-www-form-urlencoded' `
        -Body "grant_type=password&client_id=Dental_App&username=$name@demo.kz&password=demo12345&scope=Dental" -TimeoutSec 30
    return @{ Authorization = "Bearer $($token.access_token)"; __tenant = 'dental-plus' }
}
function Api([string]$path, $headers, [string]$method = 'Get', $body = $null) {
    $request = @{ Uri = "$BaseUrl/api/app/$path"; Headers = $headers; Method = $method; TimeoutSec = 30 }
    if ($null -ne $body) { $request.ContentType = 'application/json'; $request.Body = $body | ConvertTo-Json -Depth 10 }
    return Invoke-RestMethod @request
}
function ExpectStatus([int]$expected, [scriptblock]$action, [string]$message) {
    try { & $action | Out-Null } catch {
        if ($_.Exception.Response -and [int]$_.Exception.Response.StatusCode -eq $expected) { Pass $message; return }
        throw
    }
    throw "$message (expected HTTP $expected)"
}
$exceptionIds = [System.Collections.Generic.List[string]]::new()
$blockId = $null
$owner = $null
try {
    foreach ($name in @('owner','senior1','senior2','admin1','admin2','admin3','doctor1','doctor2','doctor3','doctor4','doctor5','doctor6')) {
        Login $name | Out-Null
        Pass "login $name"
    }
    $owner = Login 'owner'; $doctor = Login 'doctor1'; $otherDoctor = Login 'doctor2'
    $profile = Api 'employee/current' $doctor
    $branch = $profile.branchIds[0]; $doctorId = $profile.employeeId
    $chairs = Api "branch/chairs/$branch" $owner
    # Use the next Monday, in the clinic timezone (the demo clinic uses UTC+05).
    $date = [DateTime]::UtcNow.AddHours(5).Date
    do { $date = $date.AddDays(1) } while ($date.DayOfWeek -ne [DayOfWeek]::Monday)
    $day = $date.ToString('yyyy-MM-dd')
    $template = @{ branchId = $branch; doctorId = $doctorId; validFrom = $day; days = @(
        @{ weekday = 1; startTime = '09:00:00'; endTime = '12:00:00'; chairId = $chairs.items[0].id },
        @{ weekday = 1; startTime = '13:00:00'; endTime = '18:00:00'; chairId = $chairs.items[0].id }) }
    $saved = Api 'doctor-schedule/replace-week' $owner 'Post' $template
    Check ($saved.items.Count -eq 2) 'owner saves a split weekly shift'
    $workingPath = "doctor-schedule/working-intervals?branchId=$branch&doctorId=$doctorId&date=$day"
    $working = Api $workingPath $doctor
    Check ($working.items.Count -eq 2) 'doctor reads own working intervals'
    Check (([DateTimeOffset]$working.items[0].startsAt).UtcDateTime.Hour -eq 4) '09:00 clinic time is 04:00 UTC'
    ExpectStatus 403 { Api $workingPath $otherDoctor } 'doctor cannot read another doctor'
    ExpectStatus 403 { Api 'doctor-schedule/replace-week' $doctor 'Post' $template } 'doctor cannot edit templates'
    $overlap = @{ branchId = $branch; doctorId = $doctorId; validFrom = $day; days = @(
        @{ weekday = 1; startTime = '09:00:00'; endTime = '12:00:00' },
        @{ weekday = 1; startTime = '10:00:00'; endTime = '11:00:00' }) }
    ExpectStatus 409 { Api 'doctor-schedule/replace-week' $owner 'Post' $overlap } 'overlapping shifts return 409'
    $exception = Api 'doctor-schedule/exception' $owner 'Post' @{
        doctorId = $doctorId; branchId = $branch; dateFrom = $day; dateTo = $day; type = 2
        startTime = '09:30:00'; endTime = '10:00:00'; comment = 'API smoke check' }
    $exceptionIds.Add($exception.id)
    Check ((Api $workingPath $owner).items.Count -eq 3) 'partial absence splits the shift'
    $start = ([DateTimeOffset]::Parse("${day}T10:00:00+05:00")).ToUniversalTime().ToString('o')
    $end = ([DateTimeOffset]::Parse("${day}T11:00:00+05:00")).ToUniversalTime().ToString('o')
    $block = Api 'doctor-schedule/block' $owner 'Post' @{ branchId = $branch; doctorId = $doctorId; startsAt = $start; endsAt = $end; reason = 'API smoke check' }
    $blockId = $block.id
    $remaining = (Api $workingPath $owner).items
    Check (-not ($remaining | Where-Object { ([DateTimeOffset]$_.startsAt) -lt ([DateTimeOffset]$end) -and ([DateTimeOffset]$_.endsAt) -gt ([DateTimeOffset]$start) })) 'time block removes working time'
    $absence = Api 'doctor-schedule/exception' $owner 'Post' @{ doctorId = $doctorId; branchId = $branch; dateFrom = $day; dateTo = $day; type = 0 }
    $exceptionIds.Add($absence.id)
    Check ((Api $workingPath $doctor).items.Count -eq 0) 'full-day vacation removes all working intervals'
} catch {
    $report.Add("FAIL $($_.Exception.Message)")
    throw
} finally {
    if ($owner) {
        foreach ($id in $exceptionIds) {
            try { Api "doctor-schedule/$id/exception" $owner 'Delete' | Out-Null } catch { $report.Add("FAIL cleanup exception $id") }
        }
        if ($blockId) {
            try { Api "doctor-schedule/$blockId/block" $owner 'Delete' | Out-Null } catch { $report.Add("FAIL cleanup block $blockId") }
        }
    }
    $report | Set-Content -LiteralPath (Join-Path $runDir 'abp-doctor-schedules-check.txt') -Encoding utf8
}
if ($report | Where-Object { $_ -like 'FAIL*' }) { exit 1 }
