param([string]$BaseUrl='http://localhost:3102')
$ErrorActionPreference='Stop'
if(-not ([Uri]$BaseUrl).IsLoopback){throw 'Only local demo data is supported.'}
function Login([string]$name){$t=Invoke-RestMethod -Method Post "$BaseUrl/connect/token" -Headers @{__tenant='dental-plus'} -ContentType 'application/x-www-form-urlencoded' -Body "grant_type=password&client_id=Dental_App&username=$name@demo.kz&password=demo12345&scope=Dental";return @{Authorization="Bearer $($t.access_token)";__tenant='dental-plus'}}
function Api([string]$path,[string]$method='Get',$body=$null,$headers=$owner){$a=@{Uri="$BaseUrl/api/app/$path";Method=$method;Headers=$headers;TimeoutSec=120};if($null-ne $body){$a.ContentType='application/json';$a.Body=$body|ConvertTo-Json -Depth 15};Invoke-RestMethod @a}
function Check([bool]$ok,[string]$label){if(-not $ok){throw $label};$script:checks++;Write-Host "PASS $label"}
function Denied([scriptblock]$call,[string]$label){try{&$call|Out-Null}catch{if([int]$_.Exception.Response.StatusCode-in 400,403,404){Check $true $label;return};throw};throw "Expected rejection: $label"}
$script:checks=0;$owner=Login 'owner';$doctor=Login 'doctor1';$admin=Login 'admin1';$docs=@()
$dir=Join-Path $PSScriptRoot '../.run/stock-file-check';New-Item -ItemType Directory -Path $dir -Force|Out-Null
$template=Join-Path $dir 'template.xlsx'
Invoke-WebRequest "$BaseUrl/api/app/stock-file/receipt-template" -Headers $owner -OutFile $template
Check ((Get-Item $template).Length-gt 1000) 'XLSX template downloads'
# Fill only sheet 1, preserving the real template's catalog and instruction sheets.
function Fixture([string]$name,[object[]]$rows){
    $path=Join-Path $dir ($name+'.xlsx');Copy-Item -LiteralPath $template -Destination $path -Force
    $zip=[IO.Compression.ZipFile]::Open($path,[IO.Compression.ZipArchiveMode]::Update)
    try{
        $entry=$zip.GetEntry('xl/worksheets/sheet1.xml');$reader=[IO.StreamReader]::new($entry.Open());$xml=[xml]$reader.ReadToEnd();$reader.Dispose()
        $ns='http://schemas.openxmlformats.org/spreadsheetml/2006/main';$sheetData=$xml.GetElementsByTagName('sheetData',$ns)[0];$number=2
        foreach($values in $rows){$row=$xml.CreateElement('row',$ns);$row.SetAttribute('r',[string]$number)
            for($n=0;$n-lt $values.Count;$n++){$cell=$xml.CreateElement('c',$ns);$cell.SetAttribute('r',([string][char](65+$n))+$number);$value=$values[$n]
                if($value-is [string]-and $value.StartsWith('FORMULA:')){$f=$xml.CreateElement('f',$ns);$f.InnerText=$value.Substring(8);$null=$cell.AppendChild($f)}
                elseif($value-is [string]-or $null-eq $value){$cell.SetAttribute('t','inlineStr');$text=$xml.CreateElement('t',$ns);$text.InnerText=[string]$value;$inline=$xml.CreateElement('is',$ns);$null=$inline.AppendChild($text);$null=$cell.AppendChild($inline)}
                else{$v=$xml.CreateElement('v',$ns);$v.InnerText=[Convert]::ToString($value,[Globalization.CultureInfo]::InvariantCulture);$null=$cell.AppendChild($v)}
                $null=$row.AppendChild($cell)
            };$null=$sheetData.AppendChild($row);$number++
        }
        $entry.Delete();$writer=[IO.StreamWriter]::new($zip.CreateEntry('xl/worksheets/sheet1.xml').Open(),[Text.UTF8Encoding]::new($false));$writer.Write($xml.OuterXml);$writer.Dispose()
    }finally{$zip.Dispose()};return $path
}
function Preview([string]$path,$headers=$owner){Invoke-RestMethod "$BaseUrl/api/app/stock-file/preview-receipt-import" -Method Post -Headers $headers -Form @{file=Get-Item -LiteralPath $path} -TimeoutSec 120}
function Pdf($id,$headers=$owner){$path=Join-Path $dir ($id.ToString()+'.pdf');Invoke-WebRequest "$BaseUrl/api/app/stock-file/$id/export-pdf" -Method Post -Headers $headers -OutFile $path;return $path}
try{
    $item=(Api 'inventory-catalog/items?maxResultCount=1000').items|Where-Object {$_.isActive-and $_.units.Count-gt 0-and -not $_.trackSerials}|Select-Object -First 1
    if(-not $item){throw 'Demo item with package unit is required'};$unit=$item.units[0]
    $valid=Fixture 'valid' @(,@($item.sku,2,$unit.unitName,1250.50,('IMPORT-CHECK-'+[Guid]::NewGuid().ToString('N').Substring(0,8)),'','2030-12-31'))
    $result=Preview $valid;Check ($result.errors.Count-eq 0-and $result.lines.Count-eq 1) 'receipt rows resolve existing SKU and unit'
    Check ($result.lines[0].qty-eq 2-and $result.lines[0].unitId-eq $unit.id-and $result.lines[0].unitCost-eq 125050) 'import preserves input unit and exact tenge price in tiyn'
    Check ($result.lines[0].expiresAt-eq '2030-12-31'-and $result.lines[0].batchNumber-like 'IMPORT-CHECK-*') 'batch and expiry preserved'
    $unknown=Preview (Fixture 'unknown' @(,@(('MISSING-SKU-'+[Guid]::NewGuid()),1,'',100,'','','')));Check ($unknown.errors[0].row-eq 2-and $unknown.lines.Count-eq 0) 'unknown SKU produces row error'
    $wrongUnit=Preview (Fixture 'wrong-unit' @(,@($item.sku,1,'MISSING-UNIT',100,'B','','2030-12-31')));Check ($wrongUnit.errors.Count-eq 1) 'unit outside item catalog rejected'
    $formula=Preview (Fixture 'formula' @(,@($item.sku,'FORMULA:1+1','',100,'B','','2030-12-31')));Check ($formula.errors[0].message-like '*Формулы*') 'formulas rejected without calculation'
    $negative=Preview (Fixture 'negative' @(,@($item.sku,-1,'',100,'B','','2030-12-31')));Check ($negative.errors.Count-eq 1) 'negative quantity rejected'
    $empty=Preview $template;Check ($empty.errors.Count-eq 1-and $empty.lines.Count-eq 0) 'empty template cannot import'
    Denied {Preview $valid $doctor} 'doctor cannot import warehouse receipts'
    $before=(Api 'stock?maxResultCount=1').totalCount;Preview $valid|Out-Null;Check ((Api 'stock?maxResultCount=1').totalCount-eq $before) 'preview does not create stock documents'
    $warehouses=(Api 'inventory-catalog/warehouses').items;$warehouse=$warehouses|Where-Object branchId|Select-Object -First 1;$supplier=(Api 'inventory-catalog/supplier-lookup').items[0]
    $receipt=Api 'stock' 'Post' @{type=0;warehouseToId=$warehouse.id;supplierId=$supplier.id;invoiceNumber='IMPORT-CHECK';invoiceDate='2026-10-02';comment='Isolated XLSX receipt check';lines=$result.lines};$docs+=@($receipt)
    Check ($receipt.status-eq 0-and $receipt.lines[0].qty-eq 2*$unit.factorToBase) 'saving imported receipt converts package quantity to base unit'
    Check ($receipt.totalCost-eq 250100) 'draft receipt total equals input package price times quantity'
    $draftPdf=Pdf $receipt.id;Check ([Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes($draftPdf),0,4)-eq '%PDF') 'draft receipt PDF renders'
    $receipt=Api "stock/$($receipt.id)" 'Post' @{concurrencyStamp=$receipt.concurrencyStamp};Check ($receipt.status-eq 2) 'imported receipt posts through normal stock workflow'
    $postedPdf=Pdf $receipt.id;Check ((Get-Item $postedPdf).Length-gt 1000) 'posted receipt PDF renders'
    Denied {Pdf $receipt.id $doctor} 'doctor cannot print warehouse documents'
    $ownBranches=(Api 'employee/current' -headers $admin).branchIds;$foreign=$warehouses|Where-Object {$_.branchId-and $_.branchId-notin $ownBranches}|Select-Object -First 1
    if($foreign){$restricted=Api 'stock' 'Post' @{type=0;warehouseToId=$foreign.id;supplierId=$supplier.id;lines=$result.lines};$docs+=@($restricted);Denied {Pdf $restricted.id $admin} 'PDF enforces other branch restriction'}
    $inventory=Api 'stock' 'Post' @{type=3;warehouseFromId=$warehouse.id;comment='Isolated inventory print check'};$docs+=@($inventory)
    $inventoryPdf=Pdf $inventory.id;Check ((Get-Item $inventoryPdf).Length-gt 1000) 'inventory PDF renders expected actual and difference columns'
    $fractional=Preview (Fixture 'fractional' @(,@($item.sku,0.0001,$unit.unitName,1250.50,'FRACTION-CHECK','','2030-12-31')))
    Check ($fractional.errors.Count-eq 0-and $fractional.lines.Count-eq 1) 'four-decimal input quantity remains exact'
    $fractionDoc=Api 'stock' 'Post' @{type=0;warehouseToId=$warehouse.id;supplierId=$supplier.id;comment='Fractional precision print check';lines=$fractional.lines};$docs+=@($fractionDoc)
    $fractionPath=Pdf $fractionDoc.id;Copy-Item -LiteralPath $fractionPath -Destination (Join-Path $dir 'fractional.pdf') -Force
    Check ($fractionDoc.lines[0].qty-eq [decimal]0.0001*$unit.factorToBase) 'fractional receipt base quantity remains exact'
    @{quantity=$fractionDoc.lines[0].qty.ToString('0.####',[Globalization.CultureInfo]::GetCultureInfo('ru-RU'));unitCost=([decimal]$fractionDoc.lines[0].unitCost/100).ToString('0.######',[Globalization.CultureInfo]::GetCultureInfo('ru-RU'))}|ConvertTo-Json|Set-Content (Join-Path $dir 'expected-precision.json')
    $types=(Api 'stock?maxResultCount=1000').items|Group-Object type
    foreach($type in $types){$path=Pdf $type.Group[0].id;Check ((Get-Item $path).Length-gt 1000) "PDF document type $($type.Name) renders"}
    Write-Host "$script:checks stock file checks passed"
}finally{
    foreach($doc in $docs){try{$current=Api "stock/$($doc.id)";Api "stock/$($doc.id)/cancel" 'Post' @{concurrencyStamp=$current.concurrencyStamp;comment='Stock import fixture cleanup'}|Out-Null}catch{Write-Warning "Cleanup failed: $($_.ErrorDetails.Message)"}}
}
