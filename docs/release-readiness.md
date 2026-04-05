# Release Readiness

## v1 status snapshot

Current repository state is release-ready for the scoped v1 surface.

Validated baseline:

- build: `~/.dotnet/dotnet build AbacController.slnx --nologo -v q`
- tests: `~/.dotnet/dotnet test AbacController.slnx --nologo -v q`
- latest observed result: **288 unit + 22 integration = 310 passing**
- container smoke flow: documented in `docs/smoke-test-validation.md`

## Notable release-facing behaviors

- persisted PIP source admin records now feed the runtime resolver and health path
- `POST /pip/api/sources/{id}/test` performs a real source health/config check for persisted definitions
- static PIP sources support both per-subject values and wildcard/default attributes
- signed SPIFs remain rejected by default unless a deployment supplies a real XML signature verifier
- STANAG 4778 support remains intentionally minimal and envelope-oriented

## Deployment references

For the practical operator path, see:

- `docs/deployment-guide.md` for the pre-deployment checklist, go-live checks, and Docker/Kubernetes examples
- `docs/smoke-test-validation.md` for the exact observed container smoke baseline
- `docs/releases-v1.md` for the drafted v1 release notes

## Deployment example

```bash
docker build -t abac-controller:v1 .

docker run -d --name abac-controller \
  -p 8080:8080 -p 8081:8081 \
  -e ASPNETCORE_ENVIRONMENT=Production \
  -e ABAC_Database__ConnectionString='Data Source=/data/abac-controller.db' \
  -e ABAC_Auth__Authority='https://issuer.example.com/' \
  -e ABAC_Auth__Audience='abac-controller' \
  -e ABAC_Auth__RequireHttpsMetadata=true \
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

- if cryptographic SPIF trust is required for production, replace the default rejecting XML signature verifier with a deployment-specific implementation
- if Kubernetes is the target platform, convert the example manifests in `docs/deployment-guide.md` into environment-specific manifests or a chart before rollout
