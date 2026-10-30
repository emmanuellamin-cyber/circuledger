# syntax=docker/dockerfile:1

# ---------- build ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first (cached layer) using only the project files.
COPY Directory.Build.props ./
COPY src/CircuLedger.Domain/CircuLedger.Domain.csproj src/CircuLedger.Domain/
COPY src/CircuLedger.Application/CircuLedger.Application.csproj src/CircuLedger.Application/
COPY src/CircuLedger.Infrastructure/CircuLedger.Infrastructure.csproj src/CircuLedger.Infrastructure/
COPY src/CircuLedger.Api/CircuLedger.Api.csproj src/CircuLedger.Api/
RUN dotnet restore src/CircuLedger.Api/CircuLedger.Api.csproj

COPY src/ src/
RUN dotnet publish src/CircuLedger.Api/CircuLedger.Api.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

# ---------- runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

# libfontconfig1: QuestPDF font discovery. curl: container health check.
RUN apt-get update \
    && apt-get install -y --no-install-recommends libfontconfig1 curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app/publish .

# Uploads + certificate signing key live here; mount a named volume so they survive container rebuilds.
RUN mkdir -p /app/App_Data/uploads /app/App_Data/keys && chown -R app:app /app/App_Data
USER app

# Render (and most PaaS hosts) inject PORT at runtime; fall back to 8080 for docker compose.
ENV PORT=8080 \
    DOTNET_RUNNING_IN_CONTAINER=true
EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=40s --retries=3 \
    CMD curl -fsS http://localhost:${PORT}/health || exit 1

ENTRYPOINT ["sh", "-c", "ASPNETCORE_HTTP_PORTS=${PORT} exec dotnet CircuLedger.Api.dll"]
