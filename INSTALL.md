# INSTALL

## Requirements

- .NET 10 SDK
- Docker (required for container builds and Docker-backed integration tests)
- permission to run Docker commands (`docker` group or equivalent)

## Clone and restore

```bash
cd /home/darroll/projects
git clone <repo-url> abac-controller
cd abac-controller
~/.dotnet/dotnet restore AbacController.slnx
```

## Build

```bash
~/.dotnet/dotnet build AbacController.slnx --nologo -v q
```

## Run locally

Minimal local dev run using development auth:

```bash
cd /home/darroll/projects/abac-controller
ABAC_Auth__EnableDevelopmentAuth=true \
ABAC_Database__ConnectionString="Data Source=abac-controller.db" \
~/.dotnet/dotnet run --project src/AbacController.Api/AbacController.Api.csproj
```

### Default ports

- `8080` — REST controllers, Swagger, Blazor UI, metrics, health
- `8081` — gRPC over HTTP/2

## Authentication modes

ABAC Controller requires one of these auth modes:

1. **JWT bearer auth**
2. **API key auth**
3. **development auth** in Development only

If none are configured, startup fails fast.

### JWT bearer auth

Set at minimum:

```bash
export ABAC_Auth__Authority="https://issuer.example.com"
export ABAC_Auth__Audience="abac-controller"
export ABAC_Auth__RequireHttpsMetadata=true
```

### API key auth

API keys are configured as indexed configuration entries.

Example:

```bash
export ABAC_Auth__ApiKeys__0__Key="super-secret-key"
export ABAC_Auth__ApiKeys__0__ClientId="svc-policy-gateway"
export ABAC_Auth__ApiKeys__0__Description="Internal gateway"
export ABAC_Auth__ApiKeys__0__Scopes__0="abac:evaluate"
export ABAC_Auth__ApiKeys__0__Scopes__1="abac:evaluate:explain"
```

Clients then send:

```http
X-API-Key: super-secret-key
```

### Development auth

For local-only work:

```bash
export ABAC_Auth__EnableDevelopmentAuth=true
```

Do not use this in production.

## Core configuration

Configuration comes from `appsettings.json` plus environment variables prefixed with `ABAC_`.

### Database

```bash
export ABAC_Database__Provider=sqlite
export ABAC_Database__ConnectionString="Data Source=abac-controller.db"
```

Notes:

- the current host wiring uses SQLite in the active runtime
- the provider value is still validated/configured for future compatibility, but SQLite is the live baseline

### PDP / cache

```bash
export ABAC_Pdp__DecisionCacheEnabled=true
export ABAC_Pdp__DecisionCacheTtlSeconds=300
```

### Rate limiting

Global PDP limiter:

```bash
export ABAC_RateLimiting__PdpPermitLimit=1000
export ABAC_RateLimiting__WindowSeconds=1
```

Optional per-client limiter:

```bash
export ABAC_RateLimiting__PerClientPermitLimit=200
export ABAC_RateLimiting__PerClientWindowSeconds=1
```

Optional per-resource limiter:

```bash
export ABAC_RateLimiting__PerResourcePermitLimit=100
export ABAC_RateLimiting__PerResourceWindowSeconds=1
```

## Multi-tenant operation

Tenant context is resolved in this order:

1. `X-Tenant-Id` request header
2. `tenant_id` JWT claim
3. no tenant (`null`) => default/system tenant

Examples:

```http
X-Tenant-Id: tenant-a
```

This affects tenant-scoped SPIF reads/writes, policy admin flows, and audit filtering.

## Logging and observability

### Structured logging

Outside development, the host enables JSON console logging automatically.

### Health endpoints

- `GET /health/live`
- `GET /health/ready`
- `GET /health/startup`

### Prometheus metrics

- `GET /metrics`

## Swagger

Swagger UI is available at:

- `GET /swagger`

## Docker build and run

### Build image

```bash
sg docker -c "docker build -t abac-controller:v1.0 ."
```

### Run image

```bash
sg docker -c "docker run --rm -p 8080:8080 -p 8081:8081 \
  -e ASPNETCORE_ENVIRONMENT=Development \
  -e ABAC_Auth__EnableDevelopmentAuth=true \
  -e ABAC_Database__ConnectionString='Data Source=/data/abac-controller.db' \
  -v $(pwd)/.data:/data \
  abac-controller:v1.0"
```

## Integration tests

The integration suite builds and runs the real container image.

Run:

```bash
~/.dotnet/dotnet test AbacController.slnx --nologo -v q
```

The Docker-backed tests expect Docker access through commands equivalent to:

```bash
sg docker -c "docker build -t abac-controller:v1.0 ."
sg docker -c "docker run ..."
```

## Common install-time issues

### Startup fails with authentication configuration error

Cause: no JWT authority, no API key config, and development auth disabled.

Fix one of:

- configure `ABAC_Auth__Authority`
- configure `ABAC_Auth__ApiKeys__...`
- set `ABAC_Auth__EnableDevelopmentAuth=true` in Development

### SQLite file permissions in Docker

Make sure the mounted directory is writable by the container process.

### Metrics endpoint returns nothing useful

Generate some traffic first; evaluation and batch counters are request-driven.

### Signed SPIF import fails

By default, signed SPIFs are rejected unless a real XML signature verifier is supplied.

## Recommended production checklist

- configure JWT auth and/or API key auth
- disable development auth
- set a persistent database path
- set rate limits explicitly
- scrape `/metrics`
- collect JSON logs
- front with TLS / reverse proxy as appropriate for your environment
