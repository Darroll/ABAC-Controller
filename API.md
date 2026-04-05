# API

## Base listeners

- REST / Swagger / metrics / health / Blazor: `http://localhost:8080`
- gRPC: `http://localhost:8081`

Swagger UI:

- `GET /swagger`

## Authentication

The API supports:

- **JWT bearer auth**
- **API key auth** via `X-API-Key`
- **development auth** in Development only

JWT and API key auth can coexist.

### Example API key header

```http
X-API-Key: super-secret-key
```

## Tenant context

Tenant is resolved from:

1. `X-Tenant-Id` header
2. `tenant_id` JWT claim
3. default/system tenant when neither exists

Example:

```http
X-Tenant-Id: tenant-a
```

## Authorization policies

Representative policies:

- `Evaluate`
- `EvaluateExplain`
- `PolicyRead`
- `PolicyWrite`
- `PolicyAdmin`
- `PipRead`
- `PipAdmin`
- `PepRead`
- `PepAdmin`
- `PepLabel`
- `AuditRead`
- `SysRead`
- `SysAdmin`

Underlying scopes are ABAC-specific values such as `abac:evaluate` and `abac:policy:write`.

---

## HTTP endpoints

### AuthZEN and evaluation endpoints

- `GET /.well-known/authzen-configuration`
- `POST /access/v1/evaluation`
- `POST /access/v1/evaluations`
- `POST /access/v1/subjects`
- `POST /access/v1/resources`
- `POST /access/v1/actions`

### Extended PDP endpoints

- `POST /pdp/api/evaluate/explain`
- `POST /pdp/api/evaluate/simulate`
- `POST /pdp/api/evaluate/async`
- `GET /pdp/api/evaluate/async/{evaluationId}/status`
- `POST /pdp/api/evaluate/xacml-json`

### PAP endpoints

- `GET /pap/api/policy-sets`
- `GET /pap/api/policy-sets/{id}`
- `PUT /pap/api/policy-sets/{id}`
- `DELETE /pap/api/policy-sets/{id}`
- `GET /pap/api/policies/{id}`
- `PUT /pap/api/policies/{id}`
- `DELETE /pap/api/policies/{id}`
- `GET /pap/api/policies/{id}/versions`
- `POST /pap/api/policies/{id}/versions`
- `POST /pap/api/policies/{id}/versions/{versionId}/activate`
- `GET /pap/api/policies/{id}/versions/{leftVersionId}/diff/{rightVersionId}`
- `POST /pap/api/policies/{id}/versions/{versionId}/rollback`
- `GET /pap/api/spifs`
- `POST /pap/api/spifs/import`
- `DELETE /pap/api/spifs/{id}`
- `GET /pap/api/spifs/{id}/export`
- `POST /pap/api/policies/check-conflicts`
- `GET /pap/api/audit`

### PIP endpoints

- `GET /pip/api/sources`
- `GET /pip/api/sources/{id}`
- `PUT /pip/api/sources/{id}`
- `DELETE /pip/api/sources/{id}`
- `POST /pip/api/sources/{id}/test`
- `GET /pip/api/health`
- `GET /pip/api/health/cached`
- `POST /pip/api/cache/invalidate/{subjectId}`
- `POST /pip/api/cache/invalidate-all`

### PEP metadata endpoints

- `POST /pep/api/metadata/bind`
- `POST /pep/api/metadata/unbind`
- `GET /pep/api/metadata/codecs`

### System endpoints

- `GET /system/api/info`
- `GET /system/api/config`
- `GET /system/api/enforcement-points`
- `GET /system/api/audit`
- `GET /system/api/audit/{id}`

### Health / observability endpoints

- `GET /health/live`
- `GET /health/ready`
- `GET /health/startup`
- `GET /metrics`

---

## gRPC services

- `PdpApi`
- `PapApi`
- `PipApi`
- `PepApi`
- `SystemApi`

The gRPC services are also exposed through JSON transcoding.

### Representative transcoded routes

#### PDP

- `POST /pdp/grpc/evaluate`
- `POST /pdp/grpc/evaluate/batch`
- `POST /pdp/grpc/evaluate/explain`

#### PAP

- `GET /pap/grpc/policy-sets`
- `GET /pap/grpc/policy-sets/{id}`
- `GET /pap/grpc/policies/{policy_id}/versions`
- `GET /pap/grpc/spifs`

#### PIP

- `GET /pip/grpc/sources`
- `PUT /pip/grpc/sources/{source.id}`
- `DELETE /pip/grpc/sources/{id}`
- `POST /pip/grpc/resolve`

#### PEP

- `GET /pep/grpc/enforcement-points`
- `PUT /pep/grpc/enforcement-points/{enforcement_point.id}`
- `DELETE /pep/grpc/enforcement-points/{id}`
- `POST /pep/grpc/labels/decode`
- `POST /pep/grpc/labels/encode`
- `POST /pep/grpc/labels/validate`
- `POST /pep/grpc/metadata/bind`
- `POST /pep/grpc/metadata/unbind`
- `GET /pep/grpc/metadata/codecs`

#### System

- `GET /system/grpc/status`
- `GET /system/grpc/spifs/registered`
- `POST /system/grpc/audit/query`

See `protos/` for exact contracts.

---

## Feature notes by endpoint family

### Batch evaluation

`POST /access/v1/evaluations`

- evaluates multiple requests in one payload
- returns one response entry per request
- records batch metrics

### Simulation

`POST /pdp/api/evaluate/simulate`

- bypasses normal cache side effects
- intended for what-if analysis
- returns detailed decision information without acting like a normal production hit

### Async evaluation

`POST /pdp/api/evaluate/async`

Request requirements:

- must include `callbackUrl`
- optional `callbackHeaders`

Status lookup:

- `GET /pdp/api/evaluate/async/{evaluationId}/status`

### XACML JSON evaluation

`POST /pdp/api/evaluate/xacml-json`

- expects a payload of the form `{ "Request": { ... } }`
- maps XACML JSON profile input into internal evaluation requests
- returns ABAC/XACML-style decision output

### SPIF import/export

- import: `POST /pap/api/spifs/import`
- export: `GET /pap/api/spifs/{id}/export`

Import path includes:

- XML parsing
- schema validation
- semantic validation
- tenant registration
- optional activation/defaulting

### Policy versioning

- create version: `POST /pap/api/policies/{id}/versions`
- activate version: `POST /pap/api/policies/{id}/versions/{versionId}/activate`
- diff versions: `GET /pap/api/policies/{id}/versions/{left}/diff/{right}`
- rollback: `POST /pap/api/policies/{id}/versions/{versionId}/rollback`

### PIP cache control

- invalidate one subject: `POST /pip/api/cache/invalidate/{subjectId}`
- invalidate all: `POST /pip/api/cache/invalidate-all`

### Metrics

`GET /metrics`

- Prometheus text exposition format
- covers evaluation counts, latency, and runtime signals

---

## Common headers

### Response headers from evaluation paths

Typical evaluation responses may include:

- `X-ABAC-Decision-Id`
- `X-ABAC-Evaluation-Time`
- `Cache-Control: no-store`

### Multi-tenant header

```http
X-Tenant-Id: tenant-a
```

### API key header

```http
X-API-Key: super-secret-key
```

### HMAC-related headers

For HMAC-signed service-to-service calls, the helper uses headers such as:

- `X-ABAC-Signature`
- `X-ABAC-Timestamp`
- `X-ABAC-Nonce`

---

## Example AuthZEN request

```json
{
  "requestId": "req-1",
  "subject": {
    "type": "user",
    "id": "user-123",
    "properties": {
      "department": "engineering"
    }
  },
  "action": {
    "name": "read",
    "properties": {}
  },
  "resource": {
    "type": "document",
    "id": "doc-123",
    "properties": {}
  }
}
```

## Example AuthZEN response

```json
{
  "decision": true,
  "context": {
    "id": "4ff2...",
    "reason_admin": "Permitted by policy-set:ps1, policy:policy1@v1"
  }
}
```

## Example async callback payload

```json
{
  "evaluationId": "async-1",
  "decision": true,
  "context": {
    "id": "decision-id",
    "decision": "Permit",
    "evaluationTime": "3ms"
  }
}
```

---

## Error behavior

Common patterns:

- `400 Bad Request` — malformed JSON, invalid XACML JSON wrapper, SPIF validation failures, missing required callback URL
- `401 Unauthorized` — missing or invalid auth
- `403 Forbidden` — authenticated but missing required scope
- `404 Not Found` — missing policy, version, SPIF, audit record, or PIP source
- `429 Too Many Requests` — rate-limited PDP evaluation traffic

## Observability summary

- Prometheus metrics at `/metrics`
- Swagger UI at `/swagger`
- health endpoints at `/health/live`, `/health/ready`, `/health/startup`
- JSON structured logs outside Development
