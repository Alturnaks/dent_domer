param([string]$BaseUrl='http://localhost:3102')
$ErrorActionPreference='Stop'
if(-not ([Uri]$BaseUrl).IsLoopback){throw 'Only a local demo server is supported.'}
function Login([string]$name){$t=Invoke-RestMethod -Method Post "$BaseUrl/connect/token" -Headers @{__tenant='dental-plus'} -ContentType 'application/x-www-form-urlencoded' -Body "grant_type=password&client_id=Dental_App&username=$name@demo.kz&password=demo12345&scope=Dental";return @{Authorization="Bearer $($t.access_token)";__tenant='dental-plus'}}
function Api([string]$path,[string]$method='Get',$body=$null,$headers=$owner){$a=@{Uri="$BaseUrl/api/app/$path";Method=$method;Headers=$headers;TimeoutSec=120};if($null-ne $body){$a.ContentType='application/json';$a.Body=$body|ConvertTo-Json -Depth 15};Invoke-RestMethod @a}
function Check([bool]$ok,[string]$label){if(-not $ok){throw $label};$script:checks++;Write-Host "PASS $label"}
$script:checks=0
$owner=Login 'owner';$doctor=Login 'doctor1';$baseQuery='from=2026-01-01&to=2026-12-31';$failed=@();$tables=@{};$visits=@();$payments=@();$shift=$null
try {
# Isolated nonzero financial facts: no real patient's account is modified.
$me=Api 'employee/current' -headers $doctor;$branch=$me.branchIds[0];$employee=$me.employeeId
$secondDoctor=(Api 'employee/current' -headers (Login 'doctor2')).employeeId
$patient=Api 'patient' 'Post' @{lastName='ReportFixture';firstName=([Guid]::NewGuid().ToString('N').Substring(0,10));notes='Isolated report reconciliation'}
$register=Api 'cash/register' 'Post' @{branchId=$branch;name=('Report check '+[Guid]::NewGuid().ToString('N').Substring(0,8))}
$shift=Api 'cash/open-shift' 'Post' @{cashRegisterId=$register.id;openingBalance=0}
$service=(Api "visit/services/$branch")|Select-Object -First 1;$material=(Api 'visit/materials')|Where-Object name -match 'Ватные валики'|Select-Object -First 1
foreach($qty in 1,2){
    $day=([DateTime]'2040-01-01').AddDays((Get-Random -Minimum 0 -Maximum 20000)).ToString('yyyy-MM-dd')
    $a=Api 'appointment' 'Post' @{branchId=$branch;patientId=$patient.id;doctorId=$employee;startsAt="${day}T09:00:00+05:00";endsAt="${day}T09:30:00+05:00";force=$true;services=@(@{serviceId=$service.id;qty=$qty})}
    Api "appointment/$($a.id)/change-status" 'Post' @{concurrencyStamp=$a.concurrencyStamp;status=2}|Out-Null
    $v=Api "visit/by-appointment/$($a.id)";$visits+=@($v)
    if($qty-eq 2){$v=Api "visit/$($v.id)" 'Put' @{concurrencyStamp=$v.concurrencyStamp;items=@(@{serviceId=$service.id;qty=$qty;discountPct=0;doctorId=$secondDoctor})}}
    if($material){$v=Api "visit/$($v.id)/set-materials" 'Post' @{concurrencyStamp=$v.concurrencyStamp;materials=@(@{itemId=$material.id;quantity=$qty})}}
    $v=Api "visit/$($v.id)/close" 'Post' @{concurrencyStamp=$v.concurrencyStamp}
    $p=Api 'cash/pay' 'Post' @{shiftId=$shift.id;patientId=$patient.id;visitId=$v.id;parts=@(@{method=($qty-1);amount=$v.total});idempotencyKey=[Guid]::NewGuid().ToString();comment='Report fixture'};$payments+=@($p)
}
$catalog=Api 'reports/catalog'
foreach($r in $catalog){foreach($group in $r.groupings){try{$table=Api "reports?code=$($r.code)&groupBy=$group&$baseQuery";Check ($table.columns.Count-gt 0-and $table.rows.Count-eq $table.links.Count) "$($r.code)/$group executes";$tables["$($r.code)/$group"]=$table}catch{$failed+="$($r.code)/$group $($_.ErrorDetails.Message)";Write-Host "FAIL $($r.code)/$group"}}}
if($failed.Count){$failed|ForEach-Object{Write-Host $_};throw 'Report variants failed'}
function SumColumn($t,[string]$name){$index=[Array]::IndexOf(@($t.columns|ForEach-Object name),$name);if($index-lt 0){throw "Column not found: $name"};$sum=0D;foreach($row in $t.rows){$sum+=[decimal]$row[$index]};return $sum}
$expected=SumColumn $tables['revenue/default'] 'Выручка, ₸';$received=SumColumn $tables['revenue/default'] 'Поступления, ₸'
Check ($expected-gt 0-and $received-gt 0) 'reconciliation uses nonzero revenue and payments'
foreach($g in 'day','doctor','service','category','branch'){Check ([Math]::Abs((SumColumn $tables["revenue/$g"] 'Выручка, ₸')-$expected)-le 0.01) "revenue/$g reconciles revenue";Check ([Math]::Abs((SumColumn $tables["revenue/$g"] 'Поступления, ₸')-$received)-le 0.10) "revenue/$g reconciles receipts"}
$secondPerformance=Api "reports?code=doctor_performance&doctorId=$secondDoctor&$baseQuery"
Check ((SumColumn $secondPerformance 'Выручка, ₸')-ge ([decimal]$service.price*2/100)) 'service doctor receives revenue even when visit header names another doctor'
$scopedDay=Api "reports?code=revenue&doctorId=$secondDoctor&groupBy=day&$baseQuery"
$scopedDoctor=Api "reports?code=revenue&doctorId=$secondDoctor&groupBy=doctor&$baseQuery"
Check ([Math]::Abs((SumColumn $scopedDay 'Поступления, ₸')-(SumColumn $scopedDoctor 'Поступления, ₸'))-le 0.01) 'doctor-scoped receipts reconcile across grouping dimensions'
foreach($g in 'day','doctor','service','category','branch'){$t=$tables["revenue/$g"];if($t.rows.Count){$key=[Uri]::EscapeDataString($t.drillKeys[0]);$detail=Api "reports?code=revenue&groupBy=detail&detailGroup=$g&detailKey=$key&$baseQuery";Check ($detail.rows.Count-eq $detail.links.Count) "revenue/$g drilldown metadata aligns"}}
$stock=$tables['stock_movement/default'];$indexes=@{};for($n=0;$n-lt $stock.columns.Count;$n++){$indexes[$stock.columns[$n].name]=$n}
foreach($row in $stock.rows){$balance=[decimal]$row[$indexes['На начало']]+[decimal]$row[$indexes['Приходы, нетто']]-[decimal]$row[$indexes['Визиты']]-[decimal]$row[$indexes['Списания']]+[decimal]$row[$indexes['Перемещения приход']]-[decimal]$row[$indexes['Перемещения расход']]-[decimal]$row[$indexes['Возвраты поставщику']]+[decimal]$row[$indexes['Инвентаризации']];if([Math]::Abs($balance-[decimal]$row[$indexes['На конец']])-gt 0.00001){throw 'Stock turnover does not balance'}}
Check $true 'stock opening plus categorized movements equals closing for every item and warehouse'
$facts=@'
WITH tenant AS (SELECT "Id" FROM "AbpTenants" WHERE "Name"='dental-plus'),
visits AS (SELECT * FROM "AppVisits" WHERE "TenantId"=(SELECT "Id" FROM tenant) AND NOT "IsDeleted" AND "Status"=1 AND "ClosedAt">='2025-12-31 19:00' AND "ClosedAt"<'2026-12-31 19:00'),
periods AS (SELECT * FROM "AppPayrollPeriods" WHERE "TenantId"=(SELECT "Id" FROM tenant) AND NOT "IsDeleted" AND "Status"<>0 AND "PeriodStart"<'2027-01-01' AND "PeriodEnd">='2026-01-01')
SELECT json_build_object('materialCost',coalesce((SELECT sum(m."Cost") FROM "AppVisitMaterials" m JOIN visits v ON v."Id"=m."VisitId" WHERE NOT m."IsDeleted" AND m."TenantId"=(SELECT "Id" FROM tenant)),0)/100.0,
'salary',coalesce((SELECT sum(e."Total"::numeric*(least(p."PeriodEnd"+1,'2027-01-01'::date)-greatest(p."PeriodStart",'2026-01-01'::date))/(p."PeriodEnd"-p."PeriodStart"+1)) FROM "AppPayrollEntries" e JOIN periods p ON p."Id"=e."PeriodId" WHERE e."TenantId"=(SELECT "Id" FROM tenant) AND NOT e."IsDeleted"),0)/100);
'@ | docker exec -i dental-abp-postgres-1 psql -U abp -d dental_abp -At
if($LASTEXITCODE-ne 0){throw 'PostgreSQL reference failed'};$facts=$facts|ConvertFrom-Json
Check ([Math]::Abs((SumColumn $tables['service_margin/default'] 'Материалы, ₸')-[decimal]$facts.materialCost)-le 0.10) 'service margin includes all actual visit materials'
Check ([Math]::Abs((SumColumn $tables['consumption/default'] 'Стоимость, ₸')-[decimal]$facts.materialCost)-le 0.01) 'consumption reconciles actual material costs'
Check ([Math]::Abs((SumColumn $tables['pnl/default'] 'Зарплата, ₸')-[decimal]$facts.salary)-le 0.10) 'P&L allocates approved salary to selected calendar days'
$period=(Api "payroll/periods/$branch")|Where-Object {$_.status-ne 0-and $_.total-gt 0}|Select-Object -First 1
if($period){$daySalary=Api "reports?code=pnl&branchId=$branch&from=$($period.start)&to=$($period.start)";$days=([DateTime]$period.end-[DateTime]$period.start).Days+1;Check ([Math]::Abs((SumColumn $daySalary 'Зарплата, ₸')-[decimal]$period.total/100/$days)-le 0.01) 'one-day P&L includes only one day of approved salary'}
$me=Api 'employee/current' -headers $doctor;$own=Api "reports?code=doctor_performance&$baseQuery" -headers $doctor
Check (@($own.drillKeys|Where-Object {$_-ne $me.employeeId}).Count-eq 0) 'doctor performance exposes only current doctor'
$ownCatalog=Api 'reports/catalog' -headers $doctor;Check ($ownCatalog.Count-eq 1-and $ownCatalog[0].code-eq 'doctor_performance') 'doctor catalog contains only own performance'
try{Api "reports?code=doctor_performance&doctorId=$([Guid]::NewGuid())&$baseQuery" -headers $doctor|Out-Null;throw 'Doctor accessed other doctor'}catch{if([int]$_.Exception.Response.StatusCode-ne 403){throw};Check $true 'doctor cannot request another employee performance'}
$audit=Api "audit?entityType=Visit&changeType=1&$baseQuery";Check (@($audit.items|Where-Object {$_.entityType-notlike '*.Visit'-or $_.action-ne 'Изменено'}).Count-eq 0) 'audit filters entity and change type'
try{Api "reports?code=revenue&groupBy=arbitrary&$baseQuery"|Out-Null;throw 'Invalid grouping accepted'}catch{if([int]$_.Exception.Response.StatusCode-notin 400,403){throw};Check $true 'invalid grouping rejected'}
try{Api "reports?code=revenue&groupBy=detail&$baseQuery" -headers $doctor|Out-Null;throw 'Doctor accessed finance'}catch{if([int]$_.Exception.Response.StatusCode-ne 403){throw};Check $true 'doctor cannot access financial detail'}
$subs=@();try{
    foreach($channel in 'internal','email'){$s=Api 'report-subscription' 'Post' @{code='revenue';frequency='weekly';weekday=3;groupBy='category';localTime='23:59:00';channel=$channel};$subs+=$s;Check ($s.frequency-eq 'weekly'-and $s.weekday-eq 3-and $s.groupBy-eq 'category') "weekly $channel subscription persists schedule"}
}finally{foreach($s in $subs){Api "report-subscription/$($s.id)" 'Delete'|Out-Null}}
Write-Host "$script:checks report checks passed"
} finally {
    foreach($p in $payments){try{Api "cash/$($p.id)/refund" 'Post' @{shiftId=$shift.id;amount=$p.amount;idempotencyKey=[Guid]::NewGuid().ToString();reason='Report fixture cleanup'}|Out-Null}catch{Write-Warning "Refund cleanup failed: $($_.ErrorDetails.Message)"}}
    foreach($v in $visits){try{$current=Api "visit/$($v.id)";Api "visit/$($v.id)/cancel" 'Post' @{concurrencyStamp=$current.concurrencyStamp;reason='Report fixture cleanup'}|Out-Null}catch{Write-Warning "Visit cleanup failed: $($_.ErrorDetails.Message)"}}
    if($shift){try{$current=(Api "cash/shifts/$branch")|Where-Object id -eq $shift.id;Api "cash/$($shift.id)/close-shift" 'Post' @{concurrencyStamp=$current.concurrencyStamp;actual=$current.expectedCash;comment='Report fixture cleanup'}|Out-Null}catch{Write-Warning "Shift cleanup failed: $($_.ErrorDetails.Message)"}}
}
