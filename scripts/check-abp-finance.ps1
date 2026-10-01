param([string]$BaseUrl = 'http://localhost:3102')
$ErrorActionPreference = 'Stop'
if (-not ([Uri]$BaseUrl).IsLoopback) { throw 'Only a local demo server is supported.' }
function Login([string]$name) { $token=Invoke-RestMethod -Method Post -Uri "$BaseUrl/connect/token" -Headers @{__tenant='dental-plus'} -ContentType 'application/x-www-form-urlencoded' -Body "grant_type=password&client_id=Dental_App&username=$name@demo.kz&password=demo12345&scope=Dental"; return @{Authorization="Bearer $($token.access_token)";__tenant='dental-plus'} }
function Api([string]$path, [string]$method='Get', $body=$null, $headers=$owner) { $args=@{Uri="$BaseUrl/api/app/$path";Method=$method;Headers=$headers;TimeoutSec=60};if($null -ne $body){$args.ContentType='application/json';$args.Body=$body|ConvertTo-Json -Depth 12};Invoke-RestMethod @args }
function Check([bool]$ok,[string]$label){if(-not $ok){throw $label};Write-Host "PASS $label"}
function Denied([scriptblock]$call,[string]$label){try{&$call|Out-Null}catch{if([int]$_.Exception.Response.StatusCode -in 400,403,409){Check $true $label;return};throw};throw "Expected rejection: $label"}
$owner=Login 'owner';$doctor=Login 'doctor1';$other=Login 'doctor2';$admin=Login 'admin1';$visit=$null;$shift=$null;$paid=@();$patient=$null
try {
    $me=Api 'employee/current' -headers $doctor;$branch=$me.branchIds[0];$doctorId=$me.employeeId
    $patient=Api 'patient' 'Post' @{lastName='FinanceCheck';firstName=([Guid]::NewGuid().ToString('N').Substring(0,10));notes='Isolated local finance check'}
    $register=Api 'cash/register' 'Post' @{branchId=$branch;name="Проверка кассы $([DateTime]::Now.ToString('HHmmss'))"}
    $shift=Api 'cash/open-shift' 'Post' @{cashRegisterId=$register.id;openingBalance=10000}
    Denied {Api 'cash/open-shift' 'Post' @{cashRegisterId=$register.id;openingBalance=0}} 'second shift cannot open on same register'
    $service=(Api "visit/services/$branch" -headers $doctor)|Select-Object -First 1
    $day=([DateTime]'2040-10-01').AddDays((Get-Random -Minimum 0 -Maximum 10000)).ToString('yyyy-MM-dd')
    $appointment=Api 'appointment' 'Post' @{branchId=$branch;patientId=$patient.id;doctorId=$doctorId;startsAt="${day}T09:00:00+05:00";endsAt="${day}T09:30:00+05:00";force=$true;services=@(@{serviceId=$service.id;qty=1})}
    Api "appointment/$($appointment.id)/change-status" 'Post' @{concurrencyStamp=$appointment.concurrencyStamp;status=2}|Out-Null
    $visit=Api "visit/by-appointment/$($appointment.id)"
    Check ($visit.id -and $visit.items.Count -eq 1) 'arrival opens visit and copies service with current price'
    Check ((Api "visit/by-appointment/$($appointment.id)" -headers $doctor).id -eq $visit.id) 'doctor reads own visit'
    Denied {Api "visit/$($visit.id)" -headers $other} 'other doctor cannot read visit'
    $visit=Api "visit/$($visit.id)" 'Put' @{concurrencyStamp=$visit.concurrencyStamp;items=@(@{serviceId=$service.id;qty=1;discountPct=0})} $doctor
    Check ($visit.status -eq 0 -and $visit.items.Count -eq 1) 'doctor can edit own open visit with complete permission'
    $edit=@{concurrencyStamp=$visit.concurrencyStamp;items=@(@{serviceId=$service.id;qty=2;discountPct=10})}
    $visit=Api "visit/$($visit.id)" 'Put' $edit
    Check ($visit.total -eq [long][Math]::Round($service.price*2*.9,0,[MidpointRounding]::AwayFromZero)) 'visit quantity and discount calculate exact total'
    Denied {Api "visit/$($visit.id)" 'Put' $edit} 'stale edit rejected'
    $visit=Api "visit/$($visit.id)" 'Put' @{concurrencyStamp=$visit.concurrencyStamp;items=@(@{serviceId=$service.id;qty=2;discountPct=10})} $admin
    Check ($visit.approvalState -eq 1) 'discount over admin limit waits for approval'
    Denied {Api "visit/$($visit.id)/close" 'Post' @{concurrencyStamp=$visit.concurrencyStamp}} 'visit cannot close with pending discount'
    $request=(Api 'approval?status=0&type=Discount&maxResultCount=100').items|Where-Object entityId -eq $visit.id|Select-Object -First 1
    Denied {Api "approval/$($request.id)/approve" 'Post' @{comment='Self approval denied'} $admin} 'admin cannot approve own excessive discount'
    Api "approval/$($request.id)/approve" 'Post' @{comment='Finance check owner approval'}|Out-Null
    $visit=Api "visit/$($visit.id)"
    Check ($visit.approvalState -eq 0) 'owner approval unlocks visit'
    $payBody=@{shiftId=$shift.id;patientId=$patient.id;visitId=$visit.id;parts=@(@{method=0;amount=100});idempotencyKey=[Guid]::NewGuid().ToString();comment='Finance check'}
    $payment=Api 'cash/pay' 'Post' $payBody;$paid+=@($payment)
    $again=Api 'cash/pay' 'Post' $payBody
    Check ($again[0].id -eq $payment[0].id) 'repeated payment returns same record'
    $visit=Api "visit/$($visit.id)"
    Check ($visit.paidTotal -eq 100 -and $visit.patientBalance -eq 100) 'payment updates visit and patient once'
    $advance=Api 'cash/pay' 'Post' @{shiftId=$shift.id;patientId=$patient.id;parts=@(@{method=0;amount=100});idempotencyKey=[Guid]::NewGuid().ToString();comment='Finance check advance'};$paid+=@($advance)
    $balancePayment=Api 'cash/pay' 'Post' @{shiftId=$shift.id;patientId=$patient.id;visitId=$visit.id;parts=@(@{method=5;amount=100});idempotencyKey=[Guid]::NewGuid().ToString();comment='Finance check allocation'};$paid+=@($balancePayment)
    $visit=Api "visit/$($visit.id)"
    Check ($visit.paidTotal -eq 200 -and $visit.patientBalance -eq 200) 'advance allocation does not create new money'
    Denied {Api 'cash/pay' 'Post' @{shiftId=$shift.id;patientId=$patient.id;visitId=$visit.id;parts=@(@{method=5;amount=1});idempotencyKey=[Guid]::NewGuid().ToString()}} 'already allocated advance cannot be spent twice'
    $material=(Api 'visit/materials')|Where-Object name -Match 'Ватные валики'|Select-Object -First 1
    if(-not $material){throw 'Demo consumable not found'}
    $visit=Api "visit/$($visit.id)/set-materials" 'Post' @{concurrencyStamp=$visit.concurrencyStamp;materials=@(@{itemId=$material.id;quantity=1})}
    $visit=Api "visit/$($visit.id)/close" 'Post' @{concurrencyStamp=$visit.concurrencyStamp}
    Check ($visit.status -eq 1 -and $visit.patientBalance -eq 200-$visit.total) 'closing visit bills patient and closes appointment'
    Check ($visit.materials.Count -eq 1 -and $visit.materials[0].cost -gt 0) 'closing consumes material and stores actual stock cost'
    Check ((Api "appointment/$($appointment.id)").status -eq 4) 'closed visit marks appointment completed'
    $visit=Api "visit/$($visit.id)/correct" 'Post' @{concurrencyStamp=$visit.concurrencyStamp;items=@(@{serviceId=$service.id;qty=1;unitPrice=2*$service.price;discountPct=0});materials=@(@{itemId=$material.id;quantity=2});reason='Finance check correction'}
    Check ($visit.total -eq 2*$service.price -and $visit.patientBalance -eq 200-$visit.total -and $visit.materials[0].quantity -eq 2 -and $visit.materials[0].cost -gt 0) 'closed correction updates debt and replaces material consumption'
    $operation=Api 'cash/operation' 'Post' @{shiftId=$shift.id;type=1;amount=500;comment='Finance check deposit'}
    $category=(Api 'cash/expense-categories')|Select-Object -First 1
    Api 'cash/expense' 'Post' @{branchId=$branch;categoryId=$category.id;shiftId=$shift.id;amount=200;comment='Finance check expense'}|Out-Null
    $current=(Api "cash/shifts/$branch")|Where-Object id -eq $shift.id
    Check ($current.expectedCash -eq 10500) 'expected cash includes payment deposit and expense without counting balance allocation'
    $refund=Api "cash/$($payment[0].id)/refund" 'Post' @{shiftId=$shift.id;amount=100;idempotencyKey=[Guid]::NewGuid().ToString();reason='Finance check refund'}
    $paid=@($advance)+@($balancePayment)
    Check ($refund.type -eq 1 -and $refund.state -eq 0) 'owner refund applied'
    Denied {Api "cash/$($payment[0].id)/refund" 'Post' @{shiftId=$shift.id;amount=1;idempotencyKey=[Guid]::NewGuid().ToString();reason='Excess refund'}} 'cannot refund beyond original payment'
    foreach($p in $paid){Api "cash/$($p.id)/refund" 'Post' @{shiftId=$shift.id;amount=$p.amount;idempotencyKey=[Guid]::NewGuid().ToString();reason='Finance check cleanup'}|Out-Null};$paid=@()
    $visit=Api "visit/$($visit.id)"
    $visit=Api "visit/$($visit.id)/cancel" 'Post' @{concurrencyStamp=$visit.concurrencyStamp;reason='Finance check cleanup'}
    Check ($visit.patientBalance -eq 0) 'cancelled closed visit restores patient balance'
    Check ((Api "appointment/$($appointment.id)").status -eq 5) 'cancelled visit releases calendar slot'
    $current=(Api "cash/shifts/$branch")|Where-Object id -eq $shift.id
    $shift=Api "cash/$($shift.id)/close-shift" 'Post' @{concurrencyStamp=$current.concurrencyStamp;actual=$current.expectedCash;comment='Finance check cleanup'}
    Check ($shift.status -eq 1 -and $shift.difference -eq 0) 'shift closes with matching actual cash'
    Denied {Api 'cash/pay' 'Post' @{shiftId=$shift.id;patientId=$patient.id;parts=@(@{method=0;amount=1});idempotencyKey=[Guid]::NewGuid().ToString()}} 'closed shift rejects payments'
} finally {
    foreach($p in $paid){try{Api "cash/$($p.id)/refund" 'Post' @{shiftId=$shift.id;amount=$p.amount;idempotencyKey=[Guid]::NewGuid().ToString();reason='Finance check cleanup'}|Out-Null}catch{Write-Warning 'Payment cleanup failed'}}
    if($visit -and $visit.status -ne 2){try{$visit=Api "visit/$($visit.id)";Api "visit/$($visit.id)/cancel" 'Post' @{concurrencyStamp=$visit.concurrencyStamp;reason='Finance check cleanup'}|Out-Null}catch{Write-Warning 'Visit cleanup failed'}}
    if($shift -and $shift.status -eq 0){try{$current=(Api "cash/shifts/$branch")|Where-Object id -eq $shift.id;Api "cash/$($shift.id)/close-shift" 'Post' @{concurrencyStamp=$current.concurrencyStamp;actual=$current.expectedCash;comment='Finance check cleanup'}|Out-Null}catch{Write-Warning 'Shift cleanup failed'}}
}
