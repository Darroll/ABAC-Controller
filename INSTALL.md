# INSTALL

## Requirements

- .NET 10 SDK
- Docker (for container builds and Docker integration tests)
- Access to the `docker` group if you run the integration suite as configured

## Local build

```bash
~/.dotnet/dotnet restore AbacController.slnx
~/.dotnet/dotnet build AbacController.slnx --nologo -v q
```

## Local run

```bash
cd /home/darroll/projects/abac-controller
ABAC_Auth__EnableDevelopmentAuth=true \
ABAC_Database__ConnectionString="Data Source=abac-controller.db" \
~/.dotnet/dotnet run --project src/AbacController.Api/AbacController.Api.csproj
```

Ports:

- `8080` HTTP/REST/Blazor
- `8081` gRPC (HTTP/2)

## Docker image

```bash
sg docker -c "docker build -t abac-controller:v1.0 ."
sg docker -c "docker run --rm -p 8080:8080 -p 8081:8081 \
  -e ASPNETCORE_ENVIRONMENT=Development \
  -e ABAC_Auth__EnableDevelopmentAuth=true \
  -e ABAC_Database__ConnectionString='Data Source=/data/abac-controller.db' \
  -v $(pwd)/.data:/data \
  abac-controller:v1.0"
```

## Environment variables

Configuration is loaded from `appsettings.json` and environment variables prefixed with `ABAC_`.

Common settings:

- `ABAC_Database__Provider=sqlite`
- `ABAC_Database__ConnectionString=Data Source=/data/abac-controller.db`
- `ABAC_Auth__Authority=https://issuer.example.com`
- `ABAC_Auth__Audience=abac-controller`
- `ABAC_Auth__RequireHttpsMetadata=true`
- `ABAC_Auth__EnableDevelopmentAuth=true`
- `ABAC_Pdp__DecisionCacheEnabled=true`
- `ABAC_Pdp__DecisionCacheTtlSeconds=300`
- `ABAC_RateLimiting__PdpPermitLimit=1000`
- `ABAC_RateLimiting__WindowSeconds=1`

## Health checks

- `GET /health/live`
- `GET /health/ready`
- `GET /health/startup`
- `GET /metrics`

## Troubleshooting

### Authentication startup failure

If startup throws because auth is not configured, either:

1. set `ABAC_Auth__Authority` and related JWT settings, or
2. in Development only, set `ABAC_Auth__EnableDevelopmentAuth=true`

### SQLite permissions in Docker

Mount a writable host directory to `/data` and ensure the container user can write to it.

### Docker integration tests

The integration fixture builds and runs the real image and expects Docker access through:

```bash
sg docker -c "docker ..."
```
