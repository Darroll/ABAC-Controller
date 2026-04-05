# ABAC Controller

A standalone .NET 10 Attribute-Based Access Control (ABAC) controller that combines policy authoring, policy evaluation, attribute resolution, labeling helpers, audit capture, and operator-facing admin APIs in a single deployable service.

The current runtime ships as one ASP.NET Core application exposing:

- AuthZEN-style REST evaluation APIs
- gRPC services with JSON transcoding
- PAP/PIP/PEP/System admin controllers
- an embedded Blazor admin UI
- Prometheus metrics and health endpoints
- SQLite-backed persistence by default

## What it does

ABAC Controller implements the core control-plane/runtime pieces of an ABAC stack:

- **PDP**: evaluates authorization requests, including explain mode, batch mode, simulation, and async evaluation
- **PAP**: imports SPIFs, validates SPIF XML, stores policy sets and policies, manages policy versions, diffs, and rollbacks
- **PIP**: resolves subject attributes from pluggable sources with cache + TTL behavior
- **PEP helpers**: handles STANAG 4774 label encode/decode/validation and STANAG 4778-style metadata binding
- **Audit**: records evaluation and admin events through a buffered durable write path
- **System/admin**: exposes runtime status, enforcement point registration, tenant-aware data access, and operational config

## Major features

### Policy evaluation

- AuthZEN 1.0-style single-request evaluation
- batch evaluation for multiple requests in one call
- async evaluation with webhook callback delivery
- explain mode with evaluation trace output
- simulation mode that bypasses normal cache/audit side effects
- XACML JSON request mapping for evaluation input
- XACML-style 4-valued decisions (`Permit`, `Deny`, `NotApplicable`, `Indeterminate`)
- decision caching with configurable TTL and bypass support
- rate limiting on PDP-facing evaluation routes

### Multi-tenant support

- tenant isolation wired through request pipeline and persistence queries
- tenant resolved from `X-Tenant-Id` header or `tenant_id` JWT claim
- tenant-aware SPIF access, policy admin flows, and audit filtering
- system/default tenant supported when no tenant ID is present

### Policy administration

- policy set CRUD
- policy CRUD
- policy version creation and activation
- version diffing between arbitrary versions
- rollback by cloning an earlier version into a newly active version
- combining algorithm support at policy-set level
- policy conflict checking for candidate rules

### SPIF and labeling

- SPIF XML parsing with namespace normalization
- schema and semantic validation for supported SPIF variants
- SPIF import/export through admin APIs
- tenant-aware SPIF registration
- STANAG 4774 XML label codec
- STANAG 4778-style envelope bind/unbind helpers
- label validation and marking generation helpers

### Attribute resolution and PIP

- pluggable PIP source model
- built-in file, static, REST, LDAP, and OIDC-backed sources
- source priority ordering
- attribute cache management and manual invalidation
- health checks for individual sources and cached source-health snapshots
- provenance on resolved attributes including cache/source metadata

### Security and auth

- JWT bearer auth for production
- API key auth for service-to-service callers
- optional development auth mode for local-only work
- scope-based authorization for admin and evaluation endpoints
- HMAC request signing helper for PEP-to-PDP style calls
- correlation IDs and structured JSON logging outside development

### Observability

- Prometheus metrics at `/metrics`
- liveness, readiness, and startup health endpoints
- structured logging with JSON console formatting in non-development environments
- evaluation counters, latency metrics, and batch metrics

## Standards posture

Implemented or intentionally scoped support includes:

- **NIST SP 800-162** alignment for ABAC concepts and architecture
- **AuthZEN 1.0**-style evaluation and discovery endpoints
- **XACML JSON profile mapping** for request ingestion and decision output shape
- **xmlspif.org v3.0** as the primary SPIF parsing target, with compatibility handling for older variants
- **STANAG 4774** XML label encode/decode
- **STANAG 4778-style** metadata envelope binding/unbinding
- **gRPC + protobuf + JSON transcoding** for first-class service contracts

Important boundary: STANAG 4778 support is intentionally minimal and envelope-oriented. It is not a full claim of complete 4778 interoperability.

## Runtime baseline

- **Target framework**: .NET 10 (`net10.0`)
- **Default database**: SQLite via EF Core
- **Ports**:
  - `8080` HTTP/REST/Blazor/Swagger/metrics/health
  - `8081` gRPC over HTTP/2
- **Host shape**: single ASP.NET Core process
- **Default persistence entities**:
  - policy sets
  - policies
  - policy versions
  - SPIF registrations
  - PIP source definitions
  - enforcement points
  - audit events
  - configuration entries

## Project structure

```text
AbacController/
├── protos/                          # protobuf contracts for gRPC + REST transcoding
├── src/
│   ├── AbacController.Core/         # domain types, interfaces, constants
│   ├── AbacController.Pdp/          # ACDF evaluation, decision cache, policy execution
│   ├── AbacController.Pap/          # SPIF parser, registries, policy admin services
│   ├── AbacController.Pep/          # label codecs, validators, metadata binding
│   ├── AbacController.Pip/          # attribute resolution, source connectors, cache
│   ├── AbacController.Data/         # EF Core DbContext, entities, repositories
│   ├── AbacController.Audit/        # buffered audit ingestion + batch persistence
│   ├── AbacController.Api/          # host wiring, controllers, gRPC, auth, metrics
│   └── AbacController.Blazor/       # embedded admin UI
├── tests/
│   ├── AbacController.Tests.Unit/
│   └── AbacController.Tests.Integration/
└── docs/
    ├── runtime-baseline.md
    ├── standards-gaps.md
    ├── dependency-baseline.md
    ├── sbom.spdx.json
    └── emailclassification-migration-guide.md
```

## Documentation map

- `INSTALL.md` — install, config, auth, Docker, env vars
- `USAGE.md` — end-to-end examples for common flows
- `ARCHITECTURE.md` — runtime internals and request pipeline
- `API.md` — HTTP/gRPC endpoint inventory and auth model
- `docs/runtime-baseline.md` — current runtime truth source
- `docs/standards-gaps.md` — standards coverage and explicit limits
- `docs/emailclassification-migration-guide.md` — migration notes from the EmailClassification baseline

## Quick start

### Build

```bash
~/.dotnet/dotnet build AbacController.slnx --nologo -v q
```

### Run locally

```bash
ABAC_Auth__EnableDevelopmentAuth=true \
ABAC_Database__ConnectionString="Data Source=abac-controller.db" \
~/.dotnet/dotnet run --project src/AbacController.Api/AbacController.Api.csproj
```

### Test

```bash
~/.dotnet/dotnet test AbacController.slnx --nologo -v q
```

## Current test baseline

- **272 unit tests**
- **17 integration tests**
- **289 total tests**

## Highlights in the HTTP surface

Representative endpoints:

- `POST /access/v1/evaluation`
- `POST /access/v1/evaluations`
- `POST /pdp/api/evaluate/explain`
- `POST /pdp/api/evaluate/simulate`
- `POST /pdp/api/evaluate/async`
- `POST /pdp/api/evaluate/xacml-json`
- `POST /pap/api/spifs/import`
- `GET /pap/api/spifs/{id}/export`
- `POST /pap/api/policies/{id}/versions`
- `POST /pap/api/policies/{id}/versions/{versionId}/rollback`
- `GET /metrics`
- `GET /health/live`

See `API.md` for the full surface.

## Notes for operators

- The service fails fast on invalid configuration.
- Signed SPIFs are rejected by default unless a real XML signature verifier is provided.
- Audit persistence is buffered but durable once flushed by the background writer.
- Swagger is enabled at `/swagger`.
- API key auth and JWT auth can coexist.
- Rate limiting is configurable globally and by client/resource partition.

## License

Proprietary — © archTIS Limited. All rights reserved.
