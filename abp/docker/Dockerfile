# Сборка ABP-приложения Dental. Контекст сборки — папка abp/.
# Цели: web (сайт MVC, порт 80) и migrator (DbMigrator: миграции + seed, одноразовый).

# ---- клиентские библиотеки (замена `abp install-libs`, нужен Node 24) ----
FROM node:24-bookworm-slim AS libs
WORKDIR /web
COPY Dental/src/Dental.Web/package.json Dental/src/Dental.Web/abp.resourcemapping.js ./
COPY docker/install-libs.js /tools/install-libs.js
RUN yarn install --non-interactive --ignore-engines --network-timeout 600000 \
 && node /tools/install-libs.js /web

# ---- сборка .NET ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Dental/ ./
COPY --from=libs /web/wwwroot/libs ./src/Dental.Web/wwwroot/libs
RUN dotnet restore src/Dental.Web/Dental.Web.csproj \
 && dotnet restore src/Dental.DbMigrator/Dental.DbMigrator.csproj
RUN dotnet publish src/Dental.Web/Dental.Web.csproj -c Release --no-restore -o /out/web \
 && dotnet publish src/Dental.DbMigrator/Dental.DbMigrator.csproj -c Release --no-restore -o /out/migrator

# ---- DbMigrator ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS migrator
WORKDIR /app
COPY --from=build /out/migrator ./
ENTRYPOINT ["dotnet", "Dental.DbMigrator.dll"]

# ---- Web ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS web
WORKDIR /app
COPY --from=build /out/web ./
ENV ASPNETCORE_URLS=http://+:80
EXPOSE 80
ENTRYPOINT ["dotnet", "Dental.Web.dll"]
