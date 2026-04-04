# ARCHITECTURE

## Runtime shape

The current runtime is a single .NET 10 process with these layers:

- **API host**: ASP.NET Core REST controllers, gRPC services, health checks, metrics, Blazor UI
- **PDP**: request enrichment, SPIF resolution, ACDF checks, native policy evaluation, decision cache, audit write
- **PAP**: SPIF parsing/registration, policy set storage, policy version activation
- **PIP**: multi-source attribute resolution with source priority and memory cache
- **PEP-oriented services**: STANAG 4774 label codec and STANAG 4778-style metadata binding
- **Data**: EF Core with SQLite backing stores for policies, SPIFs, audit records, configuration entities
- **Audit**: bounded in-memory channel with background batch persistence

## Evaluation pipeline

1. Receive request via AuthZEN REST or gRPC.
2. Normalize request into `EvaluationRequest`.
3. Resolve security label from resource properties.
4. Resolve governing SPIF using:
   - label policy OID
   - resource property override
   - request override
   - clearance policy OID
   - default SPIF
5. Load active policy sets.
6. Check decision cache when enabled and not bypassed.
7. Enrich request through PIP resolution.
8. Extract security clearance.
9. Run ACDF checks against label, clearance, and SPIF.
10. Run native policy evaluation.
11. Cache non-indeterminate results.
12. Emit append-only audit event.

## Persistence model

SQLite is the default runtime store.

Primary entity groups:

- `PolicySets`, `Policies`, `PolicyVersions`
- `Spifs`
- `AuditEvents`
- `PipSources`
- `EnforcementPoints`
- `ConfigurationEntries`

## Audit design

Audit writes are intentionally decoupled from request handling:

- request path writes into a bounded `Channel<AuditEvent>`
- channel is configured for explicit backpressure, not silent drop
- background service flushes batches to SQLite
- query surface reads through `IAuditReader`

## Security model

Authentication modes:

- **JWT bearer** for production
- **Development auth handler** for local development only when explicitly enabled

Authorization is scope-based. Policies map to required OAuth scopes such as:

- `abac.evaluate`
- `abac.policy.read`
- `abac.policy.write`
- `abac.audit.read`
- `abac.sys.admin`

## gRPC and REST split

- Port `8081`: gRPC over HTTP/2
- Port `8080`: REST controllers, health checks, metrics, static assets, Blazor UI

The gRPC contracts are defined in `protos/`. JSON transcoding is enabled for gRPC services, while several convenience REST controllers also exist for AuthZEN and admin workflows.

## Key design constraints

- immutable SPIF indexes for lock-free reads
- in-memory decision and PIP caches
- append-only audit model
- SQLite-first local runtime
- XML signature verification defaults to reject signed SPIFs unless a real verifier is supplied

## Known boundaries

This repository provides a strong runnable baseline, not a full multi-node production control plane. For current standards and implementation gaps, see:

- `docs/runtime-baseline.md`
- `docs/standards-gaps.md`
- `docs/compliance/`
