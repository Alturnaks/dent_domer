param([string]$BaseUrl='http://localhost:3102', [string]$FixturePdf=(Join-Path $PSScriptRoot '../.run/stock-file-check/fractional.pdf'))
$ErrorActionPreference='Stop'
if(-not ([Uri]$BaseUrl).IsLoopback){throw 'Only local demo data is supported'}
function Login([string]$name){$t=Invoke-RestMethod -Method Post "$BaseUrl/connect/token" -Headers @{__tenant='dental-plus'} -ContentType 'application/x-www-form-urlencoded' -Body "grant_type=password&client_id=Dental_App&username=$name@demo.kz&password=demo12345&scope=Dental";return @{Authorization="Bearer $($t.access_token)";__tenant='dental-plus'}}
function Api([string]$path,[string]$method='Get',$body=$null,$headers=$owner){$a=@{Uri="$BaseUrl/api/app/$path";Method=$method;Headers=$headers};if($null-ne $body){$a.ContentType='application/json';$a.Body=$body|ConvertTo-Json -Depth 10};Invoke-RestMethod @a}
function Check([bool]$ok,[string]$label){if(-not $ok){throw $label};$script:checks++;Write-Host "PASS $label"}
function Denied([scriptblock]$call,[string]$label){try{&$call|Out-Null}catch{if([int]$_.Exception.Response.StatusCode-in 400,401,403,404){Check $true $label;return};throw};throw "Expected rejection: $label"}
$script:checks=0;$owner=Login 'owner';$doctor=Login 'doctor1';$created=@()
$directory=Join-Path $PSScriptRoot '../.run/storage-check';New-Item -ItemType Directory -Path $directory -Force|Out-Null
$patient=(Api 'patient?maxResultCount=1').items[0].id
if(-not $patient){throw 'Demo patient required'}
$pdf=Join-Path $directory 'file.pdf';Copy-Item -LiteralPath $FixturePdf -Destination $pdf -Force
$length=(Get-Item $pdf).Length
function Ticket([string]$name='scan.pdf',[string]$mime='application/pdf',[long]$size=$length){
    $t=Api 'patient-file/upload' 'Post' @{patientId=$patient;fileName=$name;contentType=$mime;size=$size}
    $script:created+=@($t.id);return $t
}
function Upload($ticket,[string]$path=$pdf){$fields=[ordered]@{};foreach($p in $ticket.upload.fields.PSObject.Properties){$fields[$p.Name]=$p.Value};$fields.file=Get-Item $path;Invoke-WebRequest $ticket.upload.url -Method Post -Form $fields|Out-Null}
try{
    $cors=Invoke-WebRequest 'http://localhost:59002/dental-files' -Method Options -Headers @{Origin=$BaseUrl;'Access-Control-Request-Method'='POST'}
    Check ($cors.Headers['Access-Control-Allow-Origin'] -contains $BaseUrl) 'CORS allows the configured app origin'
    $t=Ticket
    Check (@(Api "patient-file?patientId=$patient").id -notcontains $t.id) 'Pending upload hidden'
    Upload $t
    Api "patient-file/$($t.id)/complete" 'Post'|Out-Null
    $files=@(Api "patient-file?patientId=$patient")
    Check ($files.id-contains $t.id) 'Valid PDF listed after verification'
    $link=Api "patient-file/$($t.id)/download"
    $download=Join-Path $directory 'download.pdf';Invoke-WebRequest $link -OutFile $download
    Check ((Get-FileHash $pdf).Hash-eq (Get-FileHash $download).Hash) 'Signed download checksum'
    $unsigned=([Uri]$link).GetLeftPart([UriPartial]::Path)
    Denied {Invoke-WebRequest $unsigned} 'Bucket is private'
    Denied {Api 'patient-file/upload' 'Post' @{patientId=$patient;fileName='bad.svg';contentType='image/svg+xml';size=10}} 'SVG rejected'
    Denied {Api 'patient-file/upload' 'Post' @{patientId=$patient;fileName='bad.pdf';contentType='image/png';size=10}} 'Extension/MIME mismatch rejected'
    Denied {Api 'patient-file/upload' 'Post' @{patientId=$patient;fileName='large.pdf';contentType='application/pdf';size=20971521}} '20 MB limit enforced'
    Denied {Api 'patient-file/upload' 'Post' @{patientId=$patient;fileName='doctor.pdf';contentType='application/pdf';size=10} $doctor} 'View-only doctor cannot upload'
    $bad=Join-Path $directory 'bad.pdf';[IO.File]::WriteAllText($bad,'<script>not a PDF</script>')
    $fake=Ticket 'bad.pdf' 'application/pdf' (Get-Item $bad).Length;Upload $fake $bad
    Denied {Api "patient-file/$($fake.id)/complete" 'Post'} 'Fake MIME rejected by signature'
    Denied {Api "patient-file/$($fake.id)/download"} 'Unverified file cannot download'
    $sized=Ticket 'size.pdf' 'application/pdf' 1
    Denied {Upload $sized} 'S3 policy rejects wrong size before storing'
    $tampered=Ticket
    $tampered.upload.fields.key='files/other-clinic/overwrite'
    Denied {Upload $tampered} 'S3 signature rejects a changed object key'
    # Authenticated tenant comes from token claims; a conflicting header cannot switch it.
    $foreign=@{Authorization=$owner.Authorization;__tenant='dental-light'}
    $unchanged=Api "patient-file/$($t.id)/download" 'Get' $null $foreign
    Check (([Uri]$unchanged).AbsolutePath-eq ([Uri]$link).AbsolutePath) 'Tenant header cannot switch authenticated tenant'
    Api "patient-file/$($t.id)" 'Delete'|Out-Null
    Denied {Api "patient-file/$($t.id)/download"} 'Removed file no longer accessible'
}finally{foreach($id in $created){try{Api "patient-file/$id" 'Delete'|Out-Null}catch{}}}
Write-Host "$script:checks storage checks passed"
