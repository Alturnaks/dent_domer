#!/usr/bin/env bash
# Выполняется ВНУТРИ контейнера mcr.microsoft.com/dotnet/sdk:10.0 (запускает scripts/abp-bootstrap.ps1).
# Создаёт решение ABP (MVC, EF Core, PostgreSQL) в /src/abp и скачивает все NuGet-пакеты в /src/.run/abp-packages.
set -x
export DEBIAN_FRONTEND=noninteractive PATH="$PATH:/root/.dotnet/tools" DOTNET_CLI_TELEMETRY_OPTOUT=1
apt-get update -qq && apt-get install -y -qq nodejs npm zip >/dev/null
npm i -g yarn >/dev/null 2>&1
dotnet tool install -g Volo.Abp.Studio.Cli || dotnet tool update -g Volo.Abp.Studio.Cli
abp --version
rm -rf /src/abp && mkdir -p /src/abp && cd /src/abp
abp new Dental -t app -u mvc -d ef -dbms PostgreSQL -csf || { echo "ABP_NEW_FAILED"; exit 2; }
cd /src/abp/Dental
abp install-libs || echo "INSTALL_LIBS_FAILED (не критично)"
dotnet restore --packages /src/.run/abp-packages || { echo "RESTORE_FAILED"; exit 3; }
# Пакеты, которые использует бизнес-логика клиники (отчёты, экспорт, фоновые задачи).
mkdir -p /tmp/extra && cd /tmp/extra && dotnet new classlib -f net10.0 -n Extra -o . >/dev/null
for p in Dapper ClosedXML QuestPDF Volo.Abp.BackgroundJobs.HangfireJobs Hangfire.PostgreSql Volo.Abp.Caching.StackExchangeRedis Volo.Abp.EntityFrameworkCore.PostgreSql xunit.v3 Shouldly NSubstitute Microsoft.NET.Test.Sdk; do dotnet add package $p --no-restore >/dev/null 2>&1 || true; done
dotnet restore --packages /src/.run/abp-packages || echo "EXTRA_RESTORE_WARN"
find /src/abp -name node_modules -prune -o -type d \( -name bin -o -name obj \) -print -exec rm -rf {} + >/dev/null 2>&1
echo "ABP_BOOTSTRAP_OK"
