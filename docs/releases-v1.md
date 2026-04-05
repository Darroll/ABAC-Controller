# ABAC Controller v1 Release Notes

This release note summarizes the implemented v1 baseline in the repository.

It does not claim production hardening beyond what is documented and validated in this repo.

## Summary

ABAC Controller v1 packages policy administration, policy evaluation, attribute resolution, labeling helpers, audit capture, system administration APIs, gRPC services, and an embedded Blazor admin UI into a single deployable ASP.NET Core service.

## Included in v1

### Policy decision and evaluation

- AuthZEN-style evaluation endpoints
- batch evaluation
- explain mode
- simulation mode
- async evaluation with callback delivery
- XACML JSON request mapping
- decision caching with configurable TTL
- PDP-facing rate limiting

### Policy administration

- policy set CRUD
- policy CRUD
- policy version creation and activation
- version diffing
- rollback by cloning a previous version into a new active version
- candidate policy conflict checking
- SPIF import, list, delete, and export

### Attribute resolution and PIP

- persisted PIP source definitions used by the live runtime resolver
- static, file, REST, LDAP, and OIDC-backed source models
- source health testing
- cached and live PIP health views
- cache invalidation endpoints

### Labeling and metadata helpers

- STANAG 4774 XML label encode/decode/validation support
- minimal STANAG 4778-style metadata bind/unbind helpers

### Operations

- health endpoints
- Prometheus metrics endpoint
- structured JSON logging outside Development
- system info and audit query endpoints
- embedded Blazor admin UI
- Dockerfile for container packaging

## Important operator notes

- SQLite is the active built-in persistence baseline for the current host wiring.
- Signed SPIFs are rejected by default unless a deployment provides a concrete XML signature verifier.
- STANAG 4778 support is intentionally minimal and envelope-oriented.
- Production auth must be configured with JWT bearer auth and/or API keys. Development auth is for Development only.

## Validation baseline

Repository validation baseline:

- build: `~/.dotnet/dotnet build AbacController.slnx --nologo -v q`
- test: `~/.dotnet/dotnet test AbacController.slnx --nologo -v q`
- observed automated baseline: 310 passing tests
- documented container smoke flow: `docs/smoke-test-validation.md`

## Upgrade / adoption notes

For first deployment or v1 adoption:

1. review `INSTALL.md`
2. review `docs/deployment-guide.md`
3. validate auth settings and persistent storage
4. import a SPIF before expecting label-aware permit flows
5. run the go-live checks from `docs/deployment-guide.md`

## Known boundaries carried into v1

- no out-of-the-box full cryptographic trust-chain validation for signed SPIFs
- no claim of complete STANAG 4778 interoperability
- no production-ready Helm chart or hardened Kubernetes bundle shipped in-repo
- SQLite-backed default deployment requires operator judgment for backup, concurrency, and HA expectations
