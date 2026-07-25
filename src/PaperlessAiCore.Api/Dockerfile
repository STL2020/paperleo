# =============================================================================
# Paperless-AI Core – Multi-Stage-Build (Linux, plattformunabhängig)
# Build:  docker build -f src/PaperlessAiCore.Api/Dockerfile -t paperless-ai-core .
# Run:    docker run -p 8080:8080 -v paperlessai_data:/app/data paperless-ai-core
# =============================================================================

# ---- Build-Stage -----------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Python ist eine harte Voraussetzung des Blazor-WASM-Native-Builds
# (Emscripten/WasmApp.Native.targets ruft beim Publish "python" auf).
RUN apt-get update \
    && apt-get install -y --no-install-recommends python3 python-is-python3 \
    && rm -rf /var/lib/apt/lists/*

RUN dotnet workload install wasm-tools

# Erst nur Projektdateien kopieren => NuGet-Restore wird gecacht
COPY src/PaperlessAiCore.Core/PaperlessAiCore.Core.csproj       src/PaperlessAiCore.Core/
COPY src/PaperlessAiCore.Shared/PaperlessAiCore.Shared.csproj   src/PaperlessAiCore.Shared/
COPY src/PaperlessAiCore.Web/PaperlessAiCore.Web.csproj         src/PaperlessAiCore.Web/
COPY src/PaperlessAiCore.Api/PaperlessAiCore.Api.csproj         src/PaperlessAiCore.Api/
RUN dotnet restore src/PaperlessAiCore.Api/PaperlessAiCore.Api.csproj

COPY src/ src/
RUN dotnet publish src/PaperlessAiCore.Api/PaperlessAiCore.Api.csproj -c Release -o /app/publish

# ---- Runtime-Stage ---------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

RUN mkdir -p /app/data && chown -R app:app /app
USER app

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
ENV ConfigPath=/app/data/settings.env
ENV ActivityLogPath=/app/data/activity.jsonl
EXPOSE 8080

ENTRYPOINT ["dotnet", "PaperlessAiCore.Api.dll"]
