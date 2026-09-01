# syntax=docker/dockerfile:1

# ---- build -------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

# Restore first, against project files only, so a source-only change reuses the
# cached restore layer instead of pulling every package again.
COPY global.json Directory.Build.props AdminForge.sln ./
COPY src/AdminForge.Core/AdminForge.Core.csproj   src/AdminForge.Core/
COPY src/AdminForge.Tools/AdminForge.Tools.csproj src/AdminForge.Tools/
COPY src/AdminForge.Web/AdminForge.Web.csproj     src/AdminForge.Web/
COPY tests/AdminForge.Tests/AdminForge.Tests.csproj tests/AdminForge.Tests/
RUN dotnet restore AdminForge.sln

COPY . .
RUN dotnet publish src/AdminForge.Web/AdminForge.Web.csproj \
        --configuration Release \
        --no-restore \
        --output /app

# ---- runtime -----------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app

# AdminForge needs no write access to its own files and holds no state, so it runs
# as an unprivileged user. The image is also happy read-only:
#   docker run --read-only --tmpfs /tmp ...
RUN addgroup -S adminforge && adduser -S -G adminforge adminforge

COPY --from=build --chown=root:root /app ./

ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_EnableDiagnostics=0

EXPOSE 8080
USER adminforge

HEALTHCHECK --interval=30s --timeout=3s --start-period=10s --retries=3 \
    CMD wget --quiet --spider http://127.0.0.1:8080/healthz || exit 1

ENTRYPOINT ["dotnet", "AdminForge.Web.dll"]
