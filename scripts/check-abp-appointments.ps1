# Local demo API checks. Test appointments remain in audit history as cancelled records.
param([string]$BaseUrl = 'http://localhost:3102')
$ErrorActionPreference = 'Stop'
if (-not ([Uri]$BaseUrl).IsLoopback) { throw 'Only a local demo server is supported.' }
$BaseUrl = $BaseUrl.TrimEnd('/')
$report = [System.Collections.Generic.List[string]]::new()
$created = [System.Collections.Generic.List[string]]::new()
function Check([bool]$value, [string]$message) { if (-not $value) { throw $message }; $report.Add("PASS $message"); Write-Host "PASS $message" }
function Login([string]$name) {
    $result = Invoke-RestMethod -Method Post -Uri "$BaseUrl/connect/token" -Headers @{__tenant='dental-plus'} -ContentType 'application/x-www-form-urlencoded' -Body "grant_type=password&client_id=Dental_App&username=$name@demo.kz&password=demo12345&scope=Dental"
    return @{Authorization="Bearer $($result.access_token)";__tenant='dental-plus'}
}
function Api([string]$path, $headers, [string]$method='Get', $body=$null) {
    $args = @{Uri="$BaseUrl/api/app/$path";Headers=$headers;Method=$method;TimeoutSec=30}
    if ($null -ne $body) { $args.ContentType='application/json';$args.Body=$body | ConvertTo-Json -Depth 10 }
    Invoke-RestMethod @args
}
function Expect([int]$status, [scriptblock]$action, [string]$message) {
    try { & $action | Out-Null } catch { if ([int]$_.Exception.Response.StatusCode -eq $status) { Check $true $message; return }; throw }
    throw "Expected HTTP $status : $message"
}
$owner=$null
try {
    $owner=Login 'owner';$doctor=Login 'doctor1';$other=Login 'doctor2';$admin=Login 'admin1'
    $me=Api 'employee/current' $doctor;$branch=$me.branchIds[0];$doctorId=$me.employeeId
    $day='2030-10-07';$weekday=[int]([DateTime]::Parse($day).DayOfWeek)
    $template=@{branchId=$branch;doctorId=$doctorId;validFrom=$day;days=@(@{weekday=$weekday;startTime='09:00:00';endTime='18:00:00'})}
    Api 'doctor-schedule/replace-week' $owner 'Post' $template | Out-Null
    $patient=(Api 'patient?maxResultCount=1' $owner).items[0].id
    $chair=(Api "branch/chairs/$branch" $owner).items[0].id
    $service=(Api 'catalog/services' $owner).items | Where-Object isActive | Select-Object -First 1
    $appointmentInput=@{branchId=$branch;patientId=$patient;doctorId=$doctorId;chairId=$chair;startsAt="${day}T09:00:00+05:00";endsAt="${day}T10:00:00+05:00";services=@(@{serviceId=$service.id;qty=1});comment='Appointment API smoke check'}
    Expect 403 { Api 'appointment' $doctor 'Post' $appointmentInput } 'doctor cannot create appointments'
    $appointment=Api 'appointment' $owner 'Post' $appointmentInput;$created.Add($appointment.id)
    Check (([DateTimeOffset]$appointment.startsAt).UtcDateTime.Hour -eq 4) 'clinic-local start stored as UTC'
    Check ($appointment.services.Count -eq 1 -and $appointment.services[0].serviceId -eq $service.id) 'appointment service snapshot returned'
    Expect 409 { Api 'appointment' $owner 'Post' $appointmentInput } 'overlapping appointment rejected'
    Expect 403 { Api "appointment/$($appointment.id)" $other } 'doctor cannot read another doctor appointment'
    Check ((Api "appointment/$($appointment.id)" $doctor).id -eq $appointment.id) 'doctor reads own appointment'
    $calendar=Api "appointment/calendar?branchId=$branch&from=$day&to=$day" $doctor
    Check ($calendar.appointments.id -contains $appointment.id -and $calendar.doctors.Count -eq 1 -and $calendar.doctors[0].id -eq $doctorId) 'own calendar hides other doctors'
    $slots=Api "appointment/available-slots?branchId=$branch&doctorId=$doctorId&chairId=$chair&date=$day&durationMinutes=30" $owner
    Check (-not ($slots.items | Where-Object { ([DateTimeOffset]$_.startsAt) -lt ([DateTimeOffset]$appointmentInput.endsAt) -and ([DateTimeOffset]$_.endsAt) -gt ([DateTimeOffset]$appointmentInput.startsAt) })) 'free slots exclude occupied chair'
    $move=@{concurrencyStamp=$appointment.concurrencyStamp;doctorId=$doctorId;chairId=$chair;startsAt="${day}T10:00:00+05:00";endsAt="${day}T11:00:00+05:00"}
    $moved=Api "appointment/$($appointment.id)/move" $owner 'Post' $move
    Check (([DateTimeOffset]$moved.startsAt).UtcDateTime.Hour -eq 5 -and $moved.concurrencyStamp -ne $appointment.concurrencyStamp) 'reschedule updates time and concurrency stamp'
    Expect 409 { Api "appointment/$($appointment.id)/move" $owner 'Post' $move } 'stale reschedule returns concurrency conflict'
    Expect 400 { Api "appointment/$($appointment.id)/change-status" $owner 'Post' @{concurrencyStamp=$moved.concurrencyStamp;status=5} } 'cancellation needs a reason'
    Expect 409 { Api "appointment/$($appointment.id)/change-status" $owner 'Post' @{concurrencyStamp=$moved.concurrencyStamp;status=4} } 'invalid status transition rejected'
    Expect 409 { Api 'doctor-schedule/exception' $owner 'Post' @{doctorId=$doctorId;branchId=$branch;dateFrom=$day;dateTo=$day;type=0} } 'vacation affecting future appointment rejected'
    $working=Api "doctor-schedule/working-intervals?branchId=$branch&doctorId=$doctorId&date=$day" $owner
    Check ($working.items.Count -eq 1) 'rejected vacation rolled back'
    Expect 409 { Api 'doctor-schedule/block' $owner 'Post' @{branchId=$branch;doctorId=$doctorId;startsAt=$move.startsAt;endsAt=$move.endsAt} } 'blocking future appointment rejected'
    $outside=$appointmentInput.Clone();$outside.startsAt="${day}T08:00:00+05:00";$outside.endsAt="${day}T09:00:00+05:00"
    Expect 422 { Api 'appointment' $owner 'Post' $outside } 'appointment outside shift rejected'
    $outside.force=$true
    Expect 403 { Api 'appointment' $admin 'Post' $outside } 'ordinary administrator cannot force outside schedule'
    $forced=Api 'appointment' $owner 'Post' $outside;$created.Add($forced.id)
    Check $forced.forcedOutsideSchedule 'owner can force and forced flag is recorded'
    $waiting=Api 'appointment/waitlist' $owner 'Post' @{branchId=$branch;patientId=$patient;doctorId=$doctorId;preferredFrom=$day;preferredTo=$day;comment='Smoke check'}
    Check ((Api "appointment/waitlist/$branch" $owner).items.id -contains $waiting.id) 'waitlist entry appears'
    $cancelReason=(Api 'cancel-reason?type=0' $owner).items[0].id
    $cancelled=Api "appointment/$($appointment.id)/change-status" $owner 'Post' @{concurrencyStamp=$moved.concurrencyStamp;status=5;reasonId=$cancelReason;comment='Smoke cleanup'}
    Check ($cancelled.status -eq 5) 'appointment cancelled'
    $rebook=Api 'appointment' $owner 'Post' (@{branchId=$branch;patientId=$patient;doctorId=$doctorId;chairId=$chair;startsAt=$move.startsAt;endsAt=$move.endsAt})
    $created.Add($rebook.id);Check ($rebook.id -ne $appointment.id) 'cancellation frees the slot'
    $secondDoctor=(Api "employee/lookup?branchId=$branch&position=3" $owner).items | Where-Object { $_.id -ne $doctorId } | Select-Object -First 1
    $secondTemplate=@{branchId=$branch;doctorId=$secondDoctor.id;validFrom=$day;days=@(@{weekday=$weekday;startTime='09:00:00';endTime='18:00:00'})}
    Api 'doctor-schedule/replace-week' $owner 'Post' $secondTemplate | Out-Null
    $parallelUrl="$BaseUrl/api/app/appointment"
    $parallelBodies=@(
        (@{branchId=$branch;patientId=$patient;doctorId=$doctorId;chairId=$chair;startsAt="${day}T13:00:00+05:00";endsAt="${day}T14:00:00+05:00"}|ConvertTo-Json),
        (@{branchId=$branch;patientId=$patient;doctorId=$secondDoctor.id;chairId=$chair;startsAt="${day}T13:00:00+05:00";endsAt="${day}T14:00:00+05:00"}|ConvertTo-Json))
    $parallelResults=$parallelBodies | ForEach-Object -Parallel {
        $result=Invoke-WebRequest -Method Post -Uri $using:parallelUrl -Headers $using:owner -Body $_ -ContentType 'application/json' -SkipHttpErrorCheck
        [PSCustomObject]@{Status=[int]$result.StatusCode;Body=($result.Content|ConvertFrom-Json)}
    } -ThrottleLimit 2
    foreach ($success in ($parallelResults|Where-Object {$_.Status -ge 200 -and $_.Status -lt 300})) {$created.Add($success.Body.id)}
    Check (($parallelResults|Where-Object {$_.Status -ge 200 -and $_.Status -lt 300}).Count -eq 1 -and ($parallelResults|Where-Object Status -eq 409).Count -eq 1) 'parallel bookings by different doctors in one chair: one success, one conflict'
    Api "appointment/$($waiting.id)/change-waitlist-status" $owner 'Post' @{concurrencyStamp=$waiting.concurrencyStamp;status=3} | Out-Null
    $history=Api "appointment/patient-appointments/$patient" $owner
    Check ($history.items.id -contains $appointment.id) 'patient history includes cancelled appointments'
} finally {
    if ($owner) {
        $reason=(Api 'cancel-reason?type=0' $owner).items[0].id
        foreach ($id in $created) { try { $row=Api "appointment/$id" $owner;if($row.status -in 0,1){Api "appointment/$id/change-status" $owner 'Post' @{concurrencyStamp=$row.concurrencyStamp;status=5;reasonId=$reason;comment='Smoke cleanup'} | Out-Null} } catch { Write-Warning "Cleanup failed for appointment $id" } }
    }
    $report | Set-Content (Join-Path $PSScriptRoot '../.run/abp-appointments-check.txt') -Encoding utf8
}
