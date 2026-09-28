# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props Dental.sln ./
COPY src/Dental.Domain/Dental.Domain.csproj src/Dental.Domain/
COPY src/Dental.Application/Dental.Application.csproj src/Dental.Application/
COPY src/Dental.Infrastructure/Dental.Infrastructure.csproj src/Dental.Infrastructure/
COPY src/Dental.Seed/Dental.Seed.csproj src/Dental.Seed/
COPY src/Dental.Api/Dental.Api.csproj src/Dental.Api/
RUN dotnet restore src/Dental.Api/Dental.Api.csproj
COPY src/ src/
RUN dotnet publish src/Dental.Api/Dental.Api.csproj -c Release -o /app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    TZ=UTC
RUN apt-get update && apt-get install -y --no-install-recommends libfontconfig1 curl && rm -rf /var/lib/apt/lists/*
COPY --from=build /app .
EXPOSE 8080
HEALTHCHECK --interval=10s --timeout=5s --start-period=20s --retries=10 CMD curl -fsS http://localhost:8080/health || exit 1
ENTRYPOINT ["dotnet", "Dental.Api.dll"]
