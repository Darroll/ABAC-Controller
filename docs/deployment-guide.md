# Deployment Guide

This guide is the operator-focused deployment reference for the current ABAC Controller runtime.

It only documents behavior that is implemented in the repository today.

## Runtime facts to anchor on

- single ASP.NET Core process
- REST, Swagger, Blazor UI, metrics, and health on port `8080`
- gRPC on port `8081`
- SQLite is the active built-in persistence baseline
- auth must be configured with JWT bearer auth, API keys, or development auth in Development only
- signed SPIFs are rejected by default unless a real XML signature verifier is supplied by the deployment

Reference docs:

- `INSTALL.md`
- `API.md`
- `docs/runtime-baseline.md`
- `docs/release-readiness.md`
- `docs/smoke-test-validation.md`

## Pre-deployment checklist

Before a production deployment, confirm all of the following:

### Runtime and image

- build succeeds: `~/.dotnet/dotnet build AbacController.slnx --nologo -v q`
- tests succeed: `~/.dotnet/dotnet test AbacController.slnx --nologo -v q`
- image is built from the repository `Dockerfile`
- persistent storage is planned for the SQLite database file
- the container can write to `/data`

### Authentication and authorization

- development auth is disabled for production
- at least one production auth mode is configured:
  - JWT bearer auth, and/or
  - API key auth
- caller scopes are mapped for the endpoints you intend to expose
- reverse proxy or ingress TLS termination is in place if the service is exposed beyond a trusted network boundary

### Network and routing

- port `8080` is reachable for REST, health, metrics, Swagger, and the admin UI
- port `8081` is reachable only if you intend to use gRPC directly
- health endpoints are wired into the deployment platform:
  - `/health/live`
  - `/health/ready`
  - `/health/startup`
- `/metrics` is reachable by your scraper

### Data and policy state

- database path is persistent and backed up appropriately
- tenant strategy is decided (`X-Tenant-Id` header and/or `tenant_id` JWT claim)
- initial SPIF import process is defined for each tenant that needs label-aware evaluation
- initial policy set / policy / version activation plan is defined
- if PIP sources are used, source connectivity is valid from the runtime network namespace

### Operational limits and observability

- PDP rate limits are set deliberately instead of relying on defaults
- JSON logs are collected from the container stdout/stderr stream
- health checks and metrics are scraped by the target platform
- audit retention and database backup expectations are documented by the operator

### Explicit known boundaries

- SQLite is the current built-in runtime baseline; PostgreSQL is not the active host wiring baseline in this repo state
- STANAG 4778 support is minimal and envelope-oriented, not a complete interoperability claim
- signed SPIF trust validation is not enabled out of the box
- file-backed PIP sources require referenced files to exist inside the running container
- LDAP credentials in persisted PIP source config should be treated as sensitive deployment data

## Go-live checks

Run these checks immediately after deployment.

1. Confirm the process is healthy:
   - `GET /health/live`
   - `GET /health/ready`
   - `GET /health/startup`
2. Confirm observability is up:
   - `GET /metrics`
   - inspect JSON logs for startup/config errors
3. Confirm the runtime identity/config surface is reachable:
   - `GET /system/api/info`
4. Confirm your auth mode works with a real secured request.
5. Import or verify a tenant SPIF if label-aware evaluation is expected.
6. Run one labeled evaluation request end-to-end.
7. If PIP sources are configured:
   - `GET /pip/api/health`
   - `POST /pip/api/sources/{id}/test`
   - one real evaluation that depends on a PIP-supplied attribute
8. Confirm audit records are queryable:
   - `GET /system/api/audit?page=1&pageSize=5`

## Docker example

Build:

```bash
docker build -t abac-controller:v1 .
```

Run with JWT bearer auth:

```bash
docker run -d --name abac-controller \
  -p 8080:8080 \
  -p 8081:8081 \
  -e ASPNETCORE_ENVIRONMENT=Production \
  -e ABAC_Database__ConnectionString='Data Source=/data/abac-controller.db' \
  -e ABAC_Auth__Authority='https://issuer.example.com/' \
  -e ABAC_Auth__Audience='abac-controller' \
  -e ABAC_Auth__RequireHttpsMetadata=true \
  -v $(pwd)/data:/data \
  abac-controller:v1
```

Run with API key auth only:

```bash
docker run -d --name abac-controller \
  -p 8080:8080 \
  -p 8081:8081 \
  -e ASPNETCORE_ENVIRONMENT=Production \
  -e ABAC_Database__ConnectionString='Data Source=/data/abac-controller.db' \
  -e ABAC_Auth__ApiKeys__0__Key='replace-with-real-secret' \
  -e ABAC_Auth__ApiKeys__0__ClientId='svc-policy-gateway' \
  -e ABAC_Auth__ApiKeys__0__Description='Production gateway' \
  -e ABAC_Auth__ApiKeys__0__Scopes__0='abac:evaluate' \
  -e ABAC_Auth__ApiKeys__0__Scopes__1='abac:evaluate:explain' \
  -v $(pwd)/data:/data \
  abac-controller:v1
```

Notes:

- the container runs as a non-root `abac` user
- the image declares `/data` as a volume and exposes ports `8080` and `8081`
- the image health check targets `http://localhost:8080/health/live`

## Kubernetes example

The repository does not ship a production-hardened chart or manifest set.

The example below is intentionally minimal and should be adapted for your cluster, storage class, secret handling, ingress, and network policy model.

### Config and secret examples

```yaml
apiVersion: v1
kind: Secret
metadata:
  name: abac-controller-auth
stringData:
  ABAC_Auth__Authority: https://issuer.example.com/
  ABAC_Auth__Audience: abac-controller
  ABAC_Auth__RequireHttpsMetadata: "true"
---
apiVersion: v1
kind: ConfigMap
metadata:
  name: abac-controller-config
data:
  ASPNETCORE_ENVIRONMENT: Production
  ABAC_Database__ConnectionString: Data Source=/data/abac-controller.db
  ABAC_RateLimiting__PdpPermitLimit: "1000"
  ABAC_RateLimiting__WindowSeconds: "1"
```

### Deployment and service example

```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: abac-controller
spec:
  replicas: 1
  selector:
    matchLabels:
      app: abac-controller
  template:
    metadata:
      labels:
        app: abac-controller
    spec:
      containers:
        - name: abac-controller
          image: abac-controller:v1
          imagePullPolicy: IfNotPresent
          ports:
            - name: http
              containerPort: 8080
            - name: grpc
              containerPort: 8081
          envFrom:
            - configMapRef:
                name: abac-controller-config
            - secretRef:
                name: abac-controller-auth
          volumeMounts:
            - name: data
              mountPath: /data
          readinessProbe:
            httpGet:
              path: /health/ready
              port: http
          livenessProbe:
            httpGet:
              path: /health/live
              port: http
          startupProbe:
            httpGet:
              path: /health/startup
              port: http
            failureThreshold: 30
            periodSeconds: 5
      volumes:
        - name: data
          persistentVolumeClaim:
            claimName: abac-controller-data
---
apiVersion: v1
kind: Service
metadata:
  name: abac-controller
spec:
  selector:
    app: abac-controller
  ports:
    - name: http
      port: 8080
      targetPort: http
    - name: grpc
      port: 8081
      targetPort: grpc
```

Operator notes for Kubernetes:

- use a persistent volume for `/data` if you keep the SQLite baseline
- if you need HA or multi-writer semantics, validate whether SQLite matches your operational model before using this example unchanged
- put auth settings and API keys in Secrets, not ConfigMaps
- add an ingress or gateway separately for TLS termination and exposure policy
- add a `ServiceMonitor` or equivalent only if your platform already uses Prometheus Operator or a compatible scraper

## Release-oriented validation path

For a pragmatic v1 validation pass, use this order:

1. deploy the container
2. verify health and metrics
3. load one tenant SPIF
4. create one policy set and one policy version
5. run one labeled AuthZEN evaluation
6. validate PIP health and one PIP-enriched evaluation if applicable
7. confirm audit query access

The exact container-oriented smoke sequence and observed baseline live in `docs/smoke-test-validation.md`.
