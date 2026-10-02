param(
    [Parameter(Mandatory)][string]$File,
    [ValidateRange(1,365)][int]$Keep=10,
    [string]$Endpoint='http://minio:9000',
    [string]$Bucket='dental-backups',
    [string]$Prefix='postgres/dental_abp/',
    [string]$Network='dental-abp_default'
)
$ErrorActionPreference='Stop'
$resolvedFile=(Resolve-Path -LiteralPath $File).Path
$name=[IO.Path]::GetFileName($resolvedFile)
if($name -notmatch '^dental_abp-\d{8}-\d{6}-[a-f0-9]{6}\.dump$'){throw 'Unexpected backup filename'}
if($Bucket -notmatch '^[a-z0-9][a-z0-9.-]{1,61}[a-z0-9]$' -or $Prefix -notmatch '^[a-z0-9_-]+(?:/[a-z0-9_-]+)*/$'){throw 'Invalid backup bucket/prefix'}
$uri=[Uri]$Endpoint
if($uri.Scheme -notin @('http','https') -or $uri.UserInfo -or $uri.AbsolutePath -ne '/' -or $uri.Query){throw 'Invalid S3 endpoint'}
$access=if($env:ABP_MINIO_USER){$env:ABP_MINIO_USER}else{'dental-local'}
$secret=if($env:ABP_MINIO_PASSWORD){$env:ABP_MINIO_PASSWORD}else{'dental-local-development'}
$previous=$env:MC_HOST_backup
$temporary=Join-Path ([IO.Path]::GetDirectoryName($resolvedFile)) ('verify-'+[Guid]::NewGuid().ToString('N')+'.dump')
try {
    $env:MC_HOST_backup=$uri.Scheme+'://'+[Uri]::EscapeDataString($access)+':'+[Uri]::EscapeDataString($secret)+'@'+$uri.Authority
    function Invoke-Mc([string[]]$Arguments){
        $output=& docker run --rm --network $Network -e MC_HOST_backup --mount "type=bind,source=$([IO.Path]::GetDirectoryName($resolvedFile)),target=/backups" dental-mc:local @Arguments
        if($LASTEXITCODE-ne 0){throw 'S3 backup operation failed; local archive retained'}
        return $output
    }
    $remote='backup/'+$Bucket+'/'+$Prefix
    Invoke-Mc @('cp','--quiet',('/backups/'+$name),($remote+$name))|Out-Null
    # Verify a complete round trip before rotation, including checksum rather than size alone.
    Invoke-Mc @('cp','--quiet',($remote+$name),('/backups/'+[IO.Path]::GetFileName($temporary)))|Out-Null
    if((Get-FileHash -LiteralPath $resolvedFile -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $temporary -Algorithm SHA256).Hash){throw 'S3 backup checksum mismatch'}
    $entries=@(Invoke-Mc @('ls','--json',$remote)|ForEach-Object {$_|ConvertFrom-Json}|Where-Object { $_.type -eq 'file' -and $_.key -match '^dental_abp-\d{8}-\d{6}-[a-f0-9]{6}\.dump$' })
    $entries|Sort-Object lastModified -Descending|Select-Object -Skip $Keep|ForEach-Object {
        Invoke-Mc @('rm','--quiet',($remote+$_.key))|Out-Null
    }
    Write-Output ('S3 verified: '+$Bucket+'/'+$Prefix+$name)
} finally {
    $env:MC_HOST_backup=$previous
    if(Test-Path -LiteralPath $temporary){Remove-Item -LiteralPath $temporary}
}
