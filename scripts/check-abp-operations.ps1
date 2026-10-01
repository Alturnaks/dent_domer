param([string]$BaseUrl='http://localhost:3102',[switch]$ReadOnly)
$ErrorActionPreference='Stop'
if(-not ([Uri]$BaseUrl).IsLoopback){throw 'Only local demo data is supported.'}
function Login([string]$name){$t=Invoke-RestMethod -Method Post -Uri "$BaseUrl/connect/token" -Headers @{__tenant='dental-plus'} -ContentType 'application/x-www-form-urlencoded' -Body "grant_type=password&client_id=Dental_App&username=$name@demo.kz&password=demo12345&scope=Dental";return @{Authorization="Bearer $($t.access_token)";__tenant='dental-plus'}}
function Api([string]$path,[string]$method='Get',$body=$null,$headers=$owner){$a=@{Uri="$BaseUrl/api/app/$path";Method=$method;Headers=$headers;TimeoutSec=120};if($null-ne $body){$a.ContentType='application/json';$a.Body=$body|ConvertTo-Json -Depth 15};Invoke-RestMethod @a}
function Check([bool]$ok,[string]$label){if(-not $ok){throw $label};Write-Host "PASS $label"}
function Denied([scriptblock]$call,[string]$label){try{&$call|Out-Null}catch{if([int]$_.Exception.Response.StatusCode-in 400,403,404,409){Check $true $label;return};throw};throw "Expected rejection: $label"}
$owner=Login 'owner';$doctor=Login 'doctor1'
$catalog=Api 'reports/catalog';Check ($catalog.Count-eq 25) 'all 25 reports in owner catalog'
$failures=@()
foreach($r in $catalog){try{$data=Api "reports?code=$($r.code)&from=2026-01-01&to=2026-12-31";Check ($data.columns.Count-gt 0) "report $($r.code) executes"}catch{$failures+="$($r.code): $($_.ErrorDetails.Message)";Write-Host "FAIL $($r.code)"}}
if($failures.Count){$failures|ForEach-Object{Write-Host $_};throw 'Report queries failed'}
$audit=Api 'audit?from=2026-01-01&to=2026-12-31';Check ($null-ne $audit.items) 'audit returns paged history'
Api 'audit/suspicious?from=2026-01-01&to=2026-12-31'|Out-Null
Denied {Api 'reports?code=revenue&from=2026-01-01&to=2026-12-31' -headers $doctor} 'doctor cannot read finance report'
Denied {Api 'audit?from=2026-01-01&to=2026-12-31' -headers $doctor} 'doctor cannot read audit'
Denied {Api ('reports?code=revenue&from=2026-01-01&to=2026-12-31&branchId='+[Guid]::NewGuid())} 'report rejects inaccessible branch'
Denied {Api "reports?code=revenue&from=2020-01-01&to=2026-12-31"} 'report rejects excessive date range'
$revenue=Api 'reports?code=revenue&from=2026-01-01&to=2026-12-31'
$expected=@'
SELECT COALESCE(sum(v."Total"),0) FROM "AppVisits" v JOIN "AbpTenants" t ON t."Id"=v."TenantId" WHERE t."Name"='dental-plus' AND NOT v."IsDeleted" AND v."Status"=1 AND v."ClosedAt">='2025-12-31 19:00' AND v."ClosedAt"<'2026-12-31 19:00';
'@ | docker exec -i dental-abp-postgres-1 psql -U abp -d dental_abp -At
$total=0L;foreach($row in $revenue.rows){$total+=[long][Math]::Round([decimal]$row[1]*100)}
Check ($total-eq [long]$expected) 'revenue total reconciles with closed visits in PostgreSQL'
foreach($format in 'xlsx','pdf','csv'){
    $file=Join-Path ([IO.Path]::GetTempPath()) "dental-report-check.$format"
    Invoke-WebRequest -Uri "$BaseUrl/api/app/reports/export" -Method Post -Headers $owner -ContentType 'application/json' -Body (@{code='revenue';from='2026-01-01';to='2026-12-31';format=$format}|ConvertTo-Json) -OutFile $file
    $bytes=[IO.File]::ReadAllBytes($file);Check ($bytes.Length-gt 100) "export $format nonempty"
    if($format-eq 'xlsx'){Check ($bytes[0]-eq 80-and $bytes[1]-eq 75) 'xlsx is a ZIP workbook'}
    if($format-eq 'pdf'){Check ([Text.Encoding]::ASCII.GetString($bytes,0,4)-eq '%PDF') 'pdf is a PDF document'}
}
if($ReadOnly){return}
$me=Api 'employee/current' -headers $doctor;$branch=$me.branchIds[0];$employee=$me.employeeId
$warehouses=Api 'purchasing/warehouses';$warehouse=$warehouses|Where-Object name -match 'Есиль'|Select-Object -First 1;if(-not $warehouse){$warehouse=$warehouses|Select-Object -First 1}
$supplier=(Api 'purchasing/suppliers')|Select-Object -First 1;$item=(Api 'purchasing/items')|Where-Object name -match 'Ватные валики'|Select-Object -First 1
if(-not $item){throw 'Demo consumable not found'}
$request=Api 'purchasing/request' 'Post' @{warehouseId=$warehouse.id;comment='Operations test';lines=@(@{itemId=$item.id;qty=4})}
$request=Api "purchasing/$($request.id)/submit" 'Post' @{concurrencyStamp=$request.concurrencyStamp}
Check ($request.status-eq 1) 'purchase request submits'
$request=Api "purchasing/$($request.id)/process" 'Post' @{concurrencyStamp=$request.concurrencyStamp;supplierId=$supplier.id;lines=@(@{itemId=$item.id;qty=2;unitPrice=100})}
Check ($request.status-eq 1-and $request.lines[0].processedQty-eq 2) 'partial request processing preserves remaining demand'
$order=(Api 'purchasing/orders')|Where-Object {$_.lines.Count-eq 1-and $_.lines[0].itemId-eq $item.id-and $_.lines[0].qty-eq 2-and $_.status-eq 0}|Select-Object -First 1
if(-not $order){throw 'Created purchase order not found'}
$order=Api "purchasing/$($order.id)/send" 'Post' @{concurrencyStamp=$order.concurrencyStamp;via='Local test'}
Check ($order.status-eq 2) 'owner sends order'
$receipt=Api "purchasing/$($order.id)/receive" 'Post' @{concurrencyStamp=$order.concurrencyStamp;invoiceNumber="CHECK-$([Guid]::NewGuid().ToString('N').Substring(0,8))";invoiceDate='2026-10-01';lines=@(@{itemId=$item.id;qty=1})}
$order=(Api 'purchasing/orders')|Where-Object id -eq $order.id
Check ($order.status-eq 3-and $order.lines[0].receivedQty-eq 1) 'partial receipt updates order'
Denied {Api "purchasing/$($order.id)/receive" 'Post' @{concurrencyStamp=$order.concurrencyStamp;invoiceNumber='EXCESS';invoiceDate='2026-10-01';lines=@(@{itemId=$item.id;qty=2})}} 'over-receipt rejected'
$invoice=(Api 'purchasing/invoices')|Where-Object {$_.amount-eq 100-and $_.status-eq 0}|Select-Object -First 1
Check ($null-ne $invoice) 'receipt creates supplier invoice'
$payment=@{concurrencyStamp=$invoice.concurrencyStamp;amount=50;reference='LOCAL-TEST';idempotencyKey=[Guid]::NewGuid().ToString()}
$invoice=Api "purchasing/$($invoice.id)/pay-invoice" 'Post' $payment
$retry=Api "purchasing/$($invoice.id)/pay-invoice" 'Post' $payment
Check ($invoice.paidAmount-eq 50-and $retry.paidAmount-eq 50) 'supplier payment retry applied once'
Denied {Api "purchasing/$($invoice.id)/pay-invoice" 'Post' @{concurrencyStamp=$invoice.concurrencyStamp;amount=51;reference='LOCAL-TEST';idempotencyKey=[Guid]::NewGuid().ToString()}} 'supplier overpayment rejected'
$returned=Api "purchasing/$($invoice.id)/return-invoice" 'Post' @{concurrencyStamp=$invoice.concurrencyStamp;reason='Local test half receipt return';lines=@(@{itemId=$item.id;qty=0.5})}
$invoice=(Api 'purchasing/invoices')|Where-Object id -eq $invoice.id
Check ($invoice.returnedAmount-eq 50) 'supplier credit uses receipt price rather than current stock valuation'
$returnDoc=Api "stock/$returned"
Api "stock/$returned/cancel" 'Post' @{concurrencyStamp=$returnDoc.concurrencyStamp;comment='Local test reverse return'}|Out-Null
$invoice=(Api 'purchasing/invoices')|Where-Object id -eq $invoice.id
Check ($invoice.returnedAmount-eq 0) 'storno return restores supplier debt'
Api "purchasing/$($request.id)/process" 'Post' @{concurrencyStamp=$request.concurrencyStamp;reject=$true}|Out-Null
$schemeDate=([DateTime]'2045-01-01').AddDays((Get-Random -Minimum 0 -Maximum 10000)).ToString('yyyy-MM-dd')
$scheme=Api 'payroll/save-scheme' 'Post' @{employeeId=$employee;type=2;percent=30;fixedAmount=10000;shiftRate=0;validFrom=$schemeDate}
Check ($scheme.fixedAmount-eq 10000) 'payroll scheme version saved'
$start=[DateTime]$schemeDate;$end=$start.AddDays(1).ToString('yyyy-MM-dd')
$period=Api 'payroll' 'Post' @{branchId=$branch;start=$schemeDate;end=$end}
$entry=$period.entries|Where-Object employeeId -eq $employee
Check ($entry.accrued-eq 10000) 'fixed payroll accrues without visits'
$period=Api "payroll/$($entry.id)/adjust" 'Post' @{concurrencyStamp=$entry.concurrencyStamp;bonus=1000;penalty=500;comment='Local test bonus'}
Check (($period.entries|Where-Object employeeId -eq $employee).total-eq 10500) 'bonus and penalty adjust total'
Denied {Api "payroll/$($period.period.id)" -headers $doctor} 'draft payroll hidden from doctor'
$period=Api "payroll/$($period.period.id)/approve" 'Post' @{concurrencyStamp=$period.period.concurrencyStamp}
Check ($period.period.status-eq 1) 'payroll approved'
$own=Api "payroll/$($period.period.id)" -headers $doctor
Check ($own.entries.Count-eq 1-and $own.entries[0].employeeId-eq $employee) 'doctor sees own approved entry only'
Denied {Api "payroll/$($period.period.id)/recalculate" 'Post' @{concurrencyStamp=$period.period.concurrencyStamp}} 'approved payroll cannot recalculate'
$period=Api "payroll/$($period.period.id)/mark-paid" 'Post' @{concurrencyStamp=$period.period.concurrencyStamp}
Check ($period.period.status-eq 2) 'payroll marked paid'
$subscription=Api 'report-subscription' 'Post' @{code='revenue';branchId=$branch;localTime='23:59:00';channel='internal'}
Check ($subscription.enabled) 'report subscription created'
Denied {Api "report-subscription/$($subscription.id)" 'Delete' -headers $doctor} 'another user cannot delete subscription'
Api "report-subscription/$($subscription.id)" 'Delete'|Out-Null
Write-Host 'Operations check finished. Test purchase receipts/invoices and payroll history remain in local demo data.'
