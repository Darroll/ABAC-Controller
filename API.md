# API

## HTTP endpoints

### AuthZEN-style endpoints

- `GET /.well-known/authzen-configuration`
- `POST /access/v1/evaluation`
- `POST /access/v1/evaluations`
- `POST /access/v1/subjects`
- `POST /access/v1/resources`
- `POST /access/v1/actions`

### PDP extended endpoints

- `POST /pdp/api/evaluate/explain`
- `POST /pdp/api/evaluate/simulate`
- `POST /pdp/api/evaluate/async`
- `GET /pdp/api/evaluate/async/{evaluationId}/status`

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
- `GET /pap/api/spifs`
- `POST /pap/api/spifs/import`

### PIP endpoints

- `GET /pip/api/sources`
- `GET /pip/api/sources/{id}`
- `PUT /pip/api/sources/{id}`
- `DELETE /pip/api/sources/{id}`
- `POST /pip/api/sources/{id}/test`

### PEP metadata endpoints

- `POST /pep/api/metadata/bind`
- `POST /pep/api/metadata/unbind`
- `GET /pep/api/metadata/codecs`

### System endpoints

- `GET /system/api/info`
- `GET /system/api/config`
- `GET /system/api/enforcement-points`

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

Representative HTTP transcoding routes:

- `POST /pdp/grpc/evaluate`
- `POST /pdp/grpc/evaluate/batch`
- `POST /pdp/grpc/evaluate/explain`
- `GET /pap/grpc/policy-sets`
- `GET /pip/grpc/sources`
- `GET /pep/grpc/enforcement-points`
- `GET /system/grpc/status`

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
