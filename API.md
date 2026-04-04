# API

## HTTP endpoints

### AuthZEN-style endpoints

- `GET /.well-known/authzen-configuration`
- `POST /access/v1/evaluation`
- `POST /access/v1/evaluations`
- `POST /access/v1/subjects`
- `POST /access/v1/resources`
- `POST /access/v1/actions`

### PAP endpoints

- `GET /api/v1/pap/policy-sets`
- `GET /api/v1/pap/policy-sets/{id}`
- `PUT /api/v1/pap/policy-sets/{id}`
- `DELETE /api/v1/pap/policy-sets/{id}`
- `GET /api/v1/pap/policies/{id}`
- `PUT /api/v1/pap/policies/{id}`
- `DELETE /api/v1/pap/policies/{id}`
- `GET /api/v1/pap/policies/{id}/versions`
- `POST /api/v1/pap/policies/{id}/versions`
- `POST /api/v1/pap/policies/{id}/versions/{versionId}/activate`
- `GET /api/v1/pap/spifs`
- `POST /api/v1/pap/spifs/import`

### PIP endpoints

- `GET /api/v1/pip/sources`
- `GET /api/v1/pip/sources/{id}`
- `PUT /api/v1/pip/sources/{id}`
- `DELETE /api/v1/pip/sources/{id}`
- `POST /api/v1/pip/sources/{id}/test`

### PEP metadata endpoints

- `POST /api/v1/pep/metadata/bind`
- `POST /api/v1/pep/metadata/unbind`
- `GET /api/v1/pep/metadata/codecs`

### System endpoints

- `GET /api/v1/system/info`
- `GET /api/v1/system/config`
- `GET /api/v1/system/enforcement-points`

### Health and metrics

- `GET /health/live`
- `GET /health/ready`
- `GET /health/startup`
- `GET /metrics`

## gRPC services

- `PdpApi`
- `PapApi`
- `PipApi`
- `PepApi`
- `SystemApi`

See `protos/` for source contracts.

## Authentication and authorization

The API uses bearer auth or the explicit development auth handler.

Representative authorization policies:

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

Headers:

- `X-ABAC-Decision-Id`
- `X-ABAC-Evaluation-Time`
- `Cache-Control: no-store`

## Error behavior

Common patterns:

- `400 Bad Request` for malformed payloads or SPIF parse errors
- `401/403` for missing auth or insufficient scope
- `404 Not Found` for missing policies, versions, or PIP sources
- `429 Too Many Requests` for rate-limited PDP evaluation traffic

## Notes

- gRPC runs on a dedicated HTTP/2 listener on port `8081`
- REST runs on port `8080`
- audit persistence is asynchronous but durable once flushed by the background writer
