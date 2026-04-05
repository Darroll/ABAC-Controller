# ARCHITECTURE

## Runtime shape

ABAC Controller currently runs as a single .NET 10 ASP.NET Core process composed of these layers:

- **API host** — controllers, gRPC services, Swagger, metrics, health checks, Blazor UI
- **PDP** — request normalization, tenant-aware SPIF selection, ACDF enforcement, native policy evaluation, decision caching
- **PAP** — SPIF parsing/validation/registration, policy CRUD, versioning, rollback, conflict analysis
- **PIP** — multi-source attribute resolution with source priority, TTL-aware caching, and health monitoring
- **PEP helpers** — label codec registry, STANAG 4774 XML codec, metadata bind/unbind, marking generation, label validation
- **Audit** — bounded in-memory ingestion plus background batch persistence
- **Data** — EF Core + SQLite persistence for the active runtime baseline

## Request flow

### HTTP and gRPC listeners

- **8080** — REST, Swagger, metrics, health, static assets, Blazor UI
- **8081** — gRPC over HTTP/2

### Middleware order

1. Correlation ID middleware
2. routing
3. authentication
4. tenant resolution middleware
5. authorization
6. antiforgery
7. endpoint execution

This ordering matters because tenant context is available to downstream handlers, repositories, and registries during policy/SPIF resolution.

## Tenant model

The system is multi-tenant at the request and persistence layers.

Tenant resolution order:

1. `X-Tenant-Id` request header
2. `tenant_id` JWT claim
3. `null` => default/system tenant

Tenant-scoped behavior includes:

- SPIF selection and registration
- PAP admin operations for SPIF entities
- tenant-aware audit filtering
- repository reads/writes where tenant is part of the entity model

`HttpTenantContext` is populated per request and injected behind `ITenantContext`.

## Evaluation pipeline

The PDP path is roughly:

1. receive request through AuthZEN, extended REST, or gRPC/transcoded gRPC
2. normalize into internal `EvaluationRequest`
3. derive resource key for rate limiting when applicable
4. resolve label / clearance data from request payload
5. determine tenant context
6. resolve governing SPIF using policy OID, request overrides, tenant registry, or default SPIF
7. load active policy sets for the current tenant/request context
8. check decision cache unless bypassed
9. enrich subject/resource attributes through the PIP resolver
10. run ACDF checks against label, clearance, and SPIF constraints
11. run native policy evaluation using the active policy set / policy version
12. combine results according to policy-set combining algorithm
13. produce decision, obligations/advice, status, and optional trace
14. write audit event
15. cache result when cacheable

## Batch evaluation

Batch evaluation accepts multiple requests in one call.

Current behavior:

- requests are evaluated individually
- shared endpoint-level batching reduces transport overhead
- metrics record both batch counts and per-decision outcomes
- each inner evaluation still generates its own decision result

## Explain and simulation modes

### Explain mode

Explain mode returns the normal decision plus trace details:

- matched policy / version
- ordered trace steps
- reason text per step
- status information

### Simulation mode

Simulation is intentionally isolated from the normal production decision path:

- bypasses decision cache population
- avoids polluting normal evaluation metrics
- useful for pre-change analysis and policy what-if checks

## Async evaluation

Async evaluation is implemented as an in-process background queue.

Components:

- `AsyncEvaluationQueue` stores queued and completed work
- `AsyncEvaluationService` consumes jobs in the background
- callback delivery uses a named `HttpClient`

Flow:

1. request is accepted with callback metadata
2. job is queued
3. background service evaluates it using a scoped `IPdpEngine`
4. result is retained for status polling
5. webhook callback is attempted

This is intentionally simple and single-node. It is not yet a distributed job system.

## Policy administration design

### Policy sets

Policy sets group policies and define a combining algorithm.

### Policies and versions

Policies are versioned entities with:

- monotonically increasing version numbers
- activation of one version at a time
- diff support between arbitrary versions
- rollback implemented as creation of a new version from earlier content

This preserves auditability instead of mutating old versions in place.

### Conflict detection

Candidate policy content can be checked for logical conflicts before activation. This is intended to catch obviously contradictory rules earlier in the authoring flow.

## SPIF handling

SPIF support is centered in the PAP layer.

Capabilities:

- XML parsing with typo/namespace normalization
- schema validation
- semantic validation
- immutable in-memory indexes for fast evaluation reads
- tenant-aware registration
- import/export through admin APIs

Important implementation detail:

- signed SPIFs are rejected by default because the default XML signature verifier is a safe reject-by-default implementation unless a real verifier is provided

## PIP design

The PIP layer uses a 3-stage resolution strategy:

1. request context / inline attributes
2. cache
3. external PIP sources

### Source model

Built-in source types include:

- static
- file
- REST
- LDAP
- OIDC

Each source advertises:

- `SourceId`
- `SourceType`
- priority
- provided attributes
- connectivity test support

### Caching

Resolved attributes carry source metadata and cache TTL. The cache can be invalidated:

- per subject
- globally

### Health monitoring

`PipHealthMonitor` periodically or on-demand tests source connectivity and stores last-known health snapshots for operators.

## Decision caching

The PDP decision cache is in-memory and TTL-based.

Characteristics:

- keyed from normalized evaluation inputs
- bypassable per request
- invalidatable when policy state changes
- intended to reduce repeat evaluation cost for hot authorization paths

Simulation mode avoids polluting this cache.

## Security model

### Authentication

The host supports:

- JWT bearer auth
- static API key auth
- development auth in Development only

Startup fails if no allowed auth mode is configured.

### Authorization

Authorization is scope-based. Policies are attached to controller and service methods for capabilities such as:

- evaluate
- explain
- policy read/write/admin
- PIP read/admin
- PEP read/admin/label
- audit read
- system read/admin

### HMAC support

An HMAC-SHA256 request-signing helper exists for service-to-service scenarios, especially PEP-to-PDP style traffic. It uses timestamp + nonce + body composition.

## Rate limiting

The host configures fixed-window rate limiting for PDP evaluation traffic.

Supported limiter shapes:

- global PDP limiter
- optional per-client limiter
- optional per-resource limiter

Partitioning inputs include:

- client identity (`client_id`, `sub`, or remote IP fallback)
- resource key via `X-ABAC-Resource-Key`

Endpoints reject with `429` when limits are exceeded.

## Audit architecture

Audit writes are intentionally decoupled from request handling.

Flow:

1. request path creates `AuditEvent`
2. event is written into a bounded `Channel`
3. background hosted service flushes batches to EF Core / SQLite
4. query APIs read through `IAuditReader`

Properties:

- bounded memory behavior
- explicit backpressure instead of silent loss
- durable persistence once flushed
- suitable for both evaluation audit and PAP admin audit trails

## Persistence model

The active runtime baseline uses SQLite with EF Core.

Primary entities:

- `PolicySetEntity`
- `PolicyEntity`
- `PolicyVersionEntity`
- `SpifEntity`
- `PipSourceEntity`
- `EnforcementPointEntity`
- `AuditEventEntity`
- `ConfigurationEntryEntity`

## Observability

### Metrics

`ApiMetrics` exports Prometheus text format at `/metrics`.

Metrics include counters and timings for:

- evaluations
- batch evaluations
- latency
- runtime health shape

### Logging

Outside Development, the host emits structured JSON logs with UTC timestamps and scopes enabled.

### Health

Health endpoints:

- `/health/live`
- `/health/ready`
- `/health/startup`

## Protocol surface

The service intentionally supports multiple ways to access the same platform:

- controller-based REST for operator-friendly/admin-centric flows
- AuthZEN-style REST for policy decision traffic
- gRPC for typed service-to-service use
- JSON transcoding over protobuf-defined gRPC contracts

## Known architectural boundaries

Current deliberate boundaries:

- single-process deployment model
- in-memory caches only
- in-process async job queue rather than distributed work queue
- minimal STANAG 4778 support
- safe reject-by-default signed-SPIF behavior without custom verifier injection

For standards-specific limits, see `docs/standards-gaps.md`.
