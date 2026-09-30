# Однократно: создаёт решение ABP (abp.io, MVC, PostgreSQL) в папке abp\ и скачивает NuGet-пакеты для Claude.
# Работает через Docker Desktop (образ dotnet/sdk:10.0), ничего ставить не нужно.
$ErrorActionPreference = "Continue"
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }
$root = Split-Path $PSScriptRoot -Parent
$run = Join-Path $root ".run"
$pk = Join-Path $run "abp-packages"
New-Item -ItemType Directory -Force -Path $pk | Out-Null
Start-Transcript -Path (Join-Path $run "abp-bootstrap.log") -Force | Out-Null
try {
    docker info *> $null
    if ($LASTEXITCODE -ne 0) { throw "Docker Desktop не запущен. Запустите его и повторите." }
    Write-Host "==> Создаю решение ABP и качаю пакеты (5-15 минут)" -ForegroundColor Cyan
    docker run --rm -v "${root}:/src" mcr.microsoft.com/dotnet/sdk:10.0 bash /src/scripts/abp-bootstrap.sh 2>&1 | ForEach-Object { "$_" } | Tee-Object -FilePath (Join-Path $run "abp-bootstrap-docker.log")
    if ($LASTEXITCODE -ne 0) { throw "Шаг в Docker завершился с ошибкой (код $LASTEXITCODE). Лог: .run\abp-bootstrap-docker.log" }

    Write-Host "==> Упаковываю пакеты (частями до 350 МБ)" -ForegroundColor Cyan
    Get-ChildItem $run -Filter "abp-packages-part*.zip" | Remove-Item -Force
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $files = Get-ChildItem $pk -Recurse -Filter "*.nupkg" | Sort-Object FullName
    $part = 1; $size = 0; $archive = $null
    foreach ($f in $files) {
        if ($null -eq $archive -or ($size + $f.Length) -gt 350MB) {
            if ($archive) { $archive.Dispose() }
            $archive = [System.IO.Compression.ZipFile]::Open((Join-Path $run ("abp-packages-part{0}.zip" -f $part)), [System.IO.Compression.ZipArchiveMode]::Create)
            $part++; $size = 0
        }
        $rel = $f.FullName.Substring($pk.Length + 1).Replace('\', '/')
        [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $f.FullName, $rel, [System.IO.Compression.CompressionLevel]::NoCompression)
        $size += $f.Length
    }
    if ($archive) { $archive.Dispose() }
    Write-Host ""
    Write-Host "ГОТОВО: $($files.Count) пакетов. Напишите Claude «готово»." -ForegroundColor Green
} catch {
    Write-Host ""
    Write-Host "ОШИБКА: $($_.Exception.Message)" -ForegroundColor Red
}
Stop-Transcript | Out-Null
