# Однократно скачивает все NuGet-пакеты проекта в .run\nuget-packages и упаковывает в .run\nuget-packages.zip.
# Нужен для сборки проекта в окружении Claude, где nuget.org недоступен.
# Работает через локальный .NET SDK, а если его нет - через Docker (образ dotnet/sdk).
$ErrorActionPreference = "Continue"  # нативные команды пишут прогресс в stderr; ошибки проверяем по $LASTEXITCODE
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }
$root = Split-Path $PSScriptRoot -Parent
$run = Join-Path $root ".run"
$pk = Join-Path $run "nuget-packages"
$zip = Join-Path $run "nuget-packages.zip"
$log = Join-Path $run "fetch-packages.log"
New-Item -ItemType Directory -Force -Path $pk | Out-Null
Start-Transcript -Path $log -Force | Out-Null
function Has([string]$c) { [bool](Get-Command $c -ErrorAction SilentlyContinue) }
try {
    $useLocal = $false
    if (Has "dotnet") {
        $sdks = (dotnet --list-sdks) -join "`n"
        if ($sdks -match "(?m)^10\.") { $useLocal = $true }
    }
    if ($useLocal) {
        Write-Host "==> dotnet restore (локальный .NET 10 SDK)" -ForegroundColor Cyan
        dotnet restore (Join-Path $root "backend\Dental.sln") --packages $pk
        if ($LASTEXITCODE -ne 0) { throw "dotnet restore завершился с ошибкой" }
    } elseif (Has "docker") {
        docker info *> $null
        if ($LASTEXITCODE -ne 0) { throw "Docker Desktop не запущен. Запустите его и повторите." }
        Write-Host "==> dotnet restore в Docker (mcr.microsoft.com/dotnet/sdk:10.0)" -ForegroundColor Cyan
        docker run --rm -v "${root}:/src" -w /src mcr.microsoft.com/dotnet/sdk:10.0 dotnet restore backend/Dental.sln --packages /src/.run/nuget-packages
        if ($LASTEXITCODE -ne 0) { throw "dotnet restore в Docker завершился с ошибкой" }
    } else {
        throw "Не найден ни .NET 10 SDK, ни Docker. Установите Docker Desktop (он всё равно нужен для запуска сайта)."
    }

    Write-Host "==> скачиваю инструмент dotnet-ef" -ForegroundColor Cyan
    $idx = Invoke-RestMethod "https://api.nuget.org/v3-flatcontainer/dotnet-ef/index.json" -UseBasicParsing -ErrorAction Stop
    $ver = @($idx.versions | Where-Object { $_ -match '^10\.\d+\.\d+$' })[-1]
    $dir = Join-Path $pk "dotnet-ef\$ver"
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    Invoke-WebRequest "https://api.nuget.org/v3-flatcontainer/dotnet-ef/$ver/dotnet-ef.$ver.nupkg" -OutFile (Join-Path $dir "dotnet-ef.$ver.nupkg") -UseBasicParsing -ErrorAction Stop
    Write-Host "   dotnet-ef $ver"

    Write-Host "==> упаковываю .nupkg (частями до 350 МБ)" -ForegroundColor Cyan
    Get-ChildItem $run -Filter "nuget-packages*.zip" | Remove-Item -Force
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $files = Get-ChildItem $pk -Recurse -Filter "*.nupkg" | Sort-Object FullName
    $limit = 350MB
    $part = 1; $size = 0; $archive = $null
    foreach ($f in $files) {
        if ($null -eq $archive -or ($size + $f.Length) -gt $limit) {
            if ($archive) { $archive.Dispose() }
            $zipPath = Join-Path $run ("nuget-packages-part{0}.zip" -f $part)
            $archive = [System.IO.Compression.ZipFile]::Open($zipPath, [System.IO.Compression.ZipArchiveMode]::Create)
            $part++; $size = 0
        }
        $rel = $f.FullName.Substring($pk.Length + 1).Replace('\', '/')
        [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $f.FullName, $rel, [System.IO.Compression.CompressionLevel]::NoCompression)
        $size += $f.Length
    }
    if ($archive) { $archive.Dispose() }
    $parts = Get-ChildItem $run -Filter "nuget-packages-part*.zip"
    $mb = [math]::Round((($parts | Measure-Object Length -Sum).Sum) / 1MB, 1)
    Write-Host ""
    Write-Host "ГОТОВО: $($parts.Count) архив(а), $mb МБ, $($files.Count) пакетов. Напишите Claude, что пакеты скачаны." -ForegroundColor Green
} catch { }
$root = Split-Path $PSScriptRoot -Parent
$run = Join-Path $root ".run"
$pk = Join-Path $run "nuget-packages"
$zip = Join-Path $run "nuget-packages.zip"
$log = Join-Path $run "fetch-packages.log"
New-Item -ItemType Directory -Force -Path $pk | Out-Null
Start-Transcript -Path $log -Force | Out-Null
function Has([string]$c) { [bool](Get-Command $c -ErrorAction SilentlyContinue) }
try {
    $useLocal = $false
    if (Has "dotnet") {
        $sdks = (dotnet --list-sdks) -join "`n"
        if ($sdks -match "(?m)^10\.") { $useLocal = $true }
    }
    if ($useLocal) {
        Write-Host "==> dotnet restore (локальный .NET 10 SDK)" -ForegroundColor Cyan
        dotnet restore (Join-Path $root "backend\Dental.sln") --packages $pk
        if ($LASTEXITCODE -ne 0) { throw "dotnet restore завершился с ошибкой" }
    } elseif (Has "docker") {
        docker info *> $null
        if ($LASTEXITCODE -ne 0) { throw "Docker Desktop не запущен. Запустите его и повторите." }
        Write-Host "==> dotnet restore в Docker (mcr.microsoft.com/dotnet/sdk:10.0)" -ForegroundColor Cyan
        docker run --rm -v "${root}:/src" -w /src mcr.microsoft.com/dotnet/sdk:10.0 dotnet restore backend/Dental.sln --packages /src/.run/nuget-packages
        if ($LASTEXITCODE -ne 0) { throw "dotnet restore в Docker завершился с ошибкой" }
    } else {
        throw "Не найден ни .NET 10 SDK, ни Docker. Установите Docker Desktop (он всё равно нужен для запуска сайта)."
    }

    Write-Host "==> скачиваю инструмент dotnet-ef" -ForegroundColor Cyan
    $idx = Invoke-RestMethod "https://api.nuget.org/v3-flatcontainer/dotnet-ef/index.json" -UseBasicParsing -ErrorAction Stop
    $ver = @($idx.versions | Where-Object { $_ -match '^10\.\d+\.\d+$' })[-1]
    $dir = Join-Path $pk "dotnet-ef\$ver"
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    Invoke-WebRequest "https://api.nuget.org/v3-flatcontainer/dotnet-ef/$ver/dotnet-ef.$ver.nupkg" -OutFile (Join-Path $dir "dotnet-ef.$ver.nupkg") -UseBasicParsing -ErrorAction Stop
    Write-Host "   dotnet-ef $ver"

    Write-Host "==> упаковываю в $zip" -ForegroundColor Cyan
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::CreateFromDirectory($pk, $zip, [System.IO.Compression.CompressionLevel]::Optimal, $false)
    $mb = [math]::Round((Get-Item $zip).Length / 1MB, 1)
    Write-Host ""
    Write-Host "ГОТОВО: $zip ($mb МБ). Напишите Claude, что пакеты скачаны." -ForegroundColor Green
} catch {
    Write-Host ""
    Write-Host "ОШИБКА: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "Лог: $log - пришлите его Claude."
} finally {
    Stop-Transcript | Out-Null
}
