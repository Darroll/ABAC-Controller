# Release Readiness

## v1 status snapshot

Current repository state is release-ready for the scoped v1 surface.

Validated baseline:

- build: `~/.dotnet/dotnet build AbacController.slnx --nologo -v q`
- tests: `~/.dotnet/dotnet test AbacController.slnx --nologo -v q`
- latest observed result: **288 unit + 21 integration = 309 passing**
- container smoke flow: documented in `docs/smoke-test-validation.md`

## Notable release-facing behaviors

- persisted PIP source admin records now feed the runtime resolver and health path
- `POST /pip/api/sources/{id}/test` performs a real source health/config check for persisted definitions
- static PIP sources support both per-subject values and wildcard/default attributes
- signed SPIFs remain rejected by default unless a deployment supplies a real XML signature verifier
- STANAG 4778 support remains intentionally minimal and envelope-oriented

## Deployment example

```bash
docker build -t abac-controller:v1 .

docker run -d --name abac-controller \
  -p 8080:8080 -p 8081:8081 \
  -e ASPNETCORE_ENVIRONMENT=Production \
  -e ABAC_Database__ConnectionString='Data Source=/data/abac-controller.db' \
  -e ABAC_Auth__Authority='https://issuer.example.com/' \
  -e ABAC_Auth__Audience='abac-controller' \
  -v $(pwd)/data:/data \
  abac-controller:v1
```

## Immediate post-deploy checks

1. `GET /health/live`
2. `GET /health/ready`
3. `GET /metrics`
4. `GET /system/api/info`
5. import a tenant SPIF and run one labeled evaluation
6. if PIP sources are configured, call:
   - `GET /pip/api/health`
   - `POST /pip/api/sources/{id}/test`

## Remaining non-blocking follow-ups

- add another container smoke run capturing the new persisted PIP runtime behavior end-to-end
- broaden docs/examples for non-static PIP source config payloads if operators will use REST/OIDC/LDAP immediately
- if cryptographic SPIF trust is required for production, replace the default rejecting XML signature verifier with a deployment-specific implementation
