$ErrorActionPreference='Stop'
$directory=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../.run/backup-check'))
New-Item -ItemType Directory -Path $directory -Force|Out-Null
$previous=$env:MC_HOST_backup
$restoreDatabase='dental_storage_restore_'+[Guid]::NewGuid().ToString('N').Substring(0,8)
$temporary='/tmp/'+$restoreDatabase+'.dump'
try{
    $access=if($env:ABP_MINIO_USER){$env:ABP_MINIO_USER}else{'dental-local'}
    $secret=if($env:ABP_MINIO_PASSWORD){$env:ABP_MINIO_PASSWORD}else{'dental-local-development'}
    $env:MC_HOST_backup='http://'+[Uri]::EscapeDataString($access)+':'+[Uri]::EscapeDataString($secret)+'@minio:9000'
    function Mc([string[]]$arguments){
        $result=& docker run --rm --network dental-abp_default -e MC_HOST_backup --mount "type=bind,source=$directory,target=/backups" dental-mc:local @arguments
        if($LASTEXITCODE-ne 0){throw 'S3 operation failed'};return $result
    }
    $prefix='backup/dental-backups/postgres/storage_checks/'
    [IO.File]::WriteAllText((Join-Path $directory 'sentinel.txt'),'unrelated object retained')
    Mc @('cp','--quiet','/backups/sentinel.txt',($prefix+'sentinel.txt'))|Out-Null
    for($i=0;$i-lt 3;$i++){
        & (Join-Path $PSScriptRoot 'backup-abp.ps1') -S3 -Keep 2 -Directory $directory -S3Prefix 'postgres/storage_checks/'|Out-Null
    }
    $objects=@(Mc @('ls','--json',$prefix)|ForEach-Object{$_|ConvertFrom-Json})
    $archives=@($objects|Where-Object{$_.key-match '^dental_abp-\d{8}-\d{6}-[a-f0-9]{6}\.dump$'})
    if($archives.Count-ne 2){throw 'S3 rotation did not retain 2 copies'}
    if(-not ($objects.key-contains 'sentinel.txt')){throw 'S3 rotation removed unrelated object'}
    if(@(Get-ChildItem -LiteralPath $directory -Filter 'dental_abp-*.dump').Count-ne 2){throw 'Local rotation failed'}
    Write-Host 'PASS local/S3 rotation retains 2 copies and unrelated object'
    $latest=$archives|Sort-Object lastModified -Descending|Select-Object -First 1
    Mc @('cp','--quiet',($prefix+$latest.key),'/backups/restored.dump')|Out-Null
    & docker cp (Join-Path $directory 'restored.dump') "dental-abp-postgres-1:$temporary"
    if($LASTEXITCODE-ne 0){throw 'Copying restore archive failed'}
    & docker exec dental-abp-postgres-1 createdb -U abp $restoreDatabase
    if($LASTEXITCODE-ne 0){throw 'Creating temporary restore DB failed'}
    & docker exec dental-abp-postgres-1 pg_restore -U abp -d $restoreDatabase --no-owner --exit-on-error $temporary
    if($LASTEXITCODE-ne 0){throw 'Restoring S3 archive failed'}
    $query='SELECT (SELECT count(*) FROM "AppPatients"), (SELECT count(*) FROM "AppPatientFiles"), (SELECT count(*) FROM "__EFMigrationsHistory")'
    $original=& docker exec dental-abp-postgres-1 psql -U abp -d dental_abp -At -c $query
    $restored=& docker exec dental-abp-postgres-1 psql -U abp -d $restoreDatabase -At -c $query
    if($LASTEXITCODE-ne 0-or $original-ne $restored){throw 'Restored database counts differ'}
    Write-Host "PASS PostgreSQL restored from S3: patients/files/migrations $restored"
}finally{
    if($restoreDatabase -match '^dental_storage_restore_[a-f0-9]{8}$'){
        & docker exec dental-abp-postgres-1 dropdb -U abp --if-exists $restoreDatabase|Out-Null
        & docker exec dental-abp-postgres-1 rm -f -- $temporary|Out-Null
    }
    $env:MC_HOST_backup=$previous
}
