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
- `GET /pap/grpc/policy-sets/{id}`
- `GET /pap/grpc/policies/{policy_id}/versions`
- `GET /pap/grpc/spifs`
- `GET /pip/grpc/sources`
- `PUT /pip/grpc/sources/{source.id}`
- `DELETE /pip/grpc/sources/{id}`
- `POST /pip/grpc/resolve`
- `GET /pep/grpc/enforcement-points`
- `PUT /pep/grpc/enforcement-points/{enforcement_point.id}`
- `DELETE /pep/grpc/enforcement-points/{id}`
- `POST /pep/grpc/labels/decode`
- `POST /pep/grpc/labels/encode`
- `POST /pep/grpc/labels/validate`
- `POST /pep/grpc/metadata/bind`
- `POST /pep/grpc/metadata/unbind`
- `GET /pep/grpc/metadata/codecs`
- `GET /system/grpc/status`
- `GET /system/grpc/spifs/registered`
- `POST /system/grpc/audit/query`

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
- controller-based REST routes and JSON-transcoded protobuf routes both remain active
- audit persistence is asynchronous but durable once flushed by the background writer
