param(
    [string]$Container='dental-abp-postgres-1',
    [string]$Database='dental_abp',
    [string]$User='abp',
    [string]$Directory=(Join-Path $PSScriptRoot '..\.run\backups'),
    [ValidateRange(1,365)][int]$Keep=10,
    [switch]$S3,
    [string]$S3Endpoint='http://minio:9000',
    [string]$S3Bucket='dental-backups',
    [string]$S3Prefix='postgres/dental_abp/',
    [string]$Network='dental-abp_default'
)
$ErrorActionPreference='Stop'
$backupDirectory=[IO.Path]::GetFullPath($Directory)
New-Item -ItemType Directory -Path $backupDirectory -Force|Out-Null
$name='dental_abp-'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'-'+[Guid]::NewGuid().ToString('N').Substring(0,6)+'.dump'
$destination=Join-Path $backupDirectory $name
$temporary='/tmp/'+$name
try {
    & docker exec $Container pg_dump -U $User -d $Database -Fc -f $temporary
    if($LASTEXITCODE-ne 0){throw 'pg_dump failed'}
    & docker cp "${Container}:$temporary" $destination
    if($LASTEXITCODE-ne 0-or -not (Test-Path -LiteralPath $destination)){throw 'Copying backup failed'}
    & docker exec $Container pg_restore --list $temporary|Out-Null
    if($LASTEXITCODE-ne 0){throw 'Backup archive verification failed'}
    if($S3){
        & (Join-Path $PSScriptRoot 'backup-abp-s3.ps1') -File $destination -Keep $Keep -Endpoint $S3Endpoint -Bucket $S3Bucket -Prefix $S3Prefix -Network $Network
    }
    $prefix=$backupDirectory.TrimEnd([IO.Path]::DirectorySeparatorChar)+[IO.Path]::DirectorySeparatorChar
    Get-ChildItem -LiteralPath $backupDirectory -Filter 'dental_abp-*.dump' -File|Sort-Object LastWriteTime -Descending|Select-Object -Skip $Keep|ForEach-Object {
        $resolved=[IO.Path]::GetFullPath($_.FullName)
        if(-not $resolved.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)){throw 'Backup rotation escaped its directory'}
        Remove-Item -LiteralPath $resolved
    }
    Write-Output $destination
} finally { & docker exec $Container rm -f -- $temporary|Out-Null }
