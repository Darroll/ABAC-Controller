FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY AbacController.slnx ./
COPY src/ ./src/
COPY tests/ ./tests/
COPY protos/ ./protos/

RUN dotnet restore AbacController.slnx
RUN dotnet publish src/AbacController.Api/AbacController.Api.csproj \
    -c Release \
    -o /app/publish \
    /p:PublishReadyToRun=true

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# curl is required by HEALTHCHECK below; the aspnet runtime image ships
# neither curl nor wget, so a wget-based probe can never succeed.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && useradd --no-create-home --shell /bin/false abac \
    && mkdir -p /data \
    && chown -R abac:abac /app /data

COPY --from=build /app/publish .

EXPOSE 8080
EXPOSE 8081
VOLUME ["/data"]
USER abac

HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD curl --fail --silent --show-error http://localhost:8080/health/live || exit 1

ENTRYPOINT ["dotnet", "AbacController.Api.dll"]
