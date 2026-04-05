# USAGE

## Typical operator flow

1. Start the service.
2. Import a SPIF for the tenant.
3. Create a policy set.
4. Create a policy.
5. Create and activate a policy version.
6. Evaluate requests through AuthZEN, XACML JSON, or gRPC.
7. Inspect audit, PIP health, metrics, and policy/version state.

---

## 1) Start the API

```bash
ABAC_Auth__EnableDevelopmentAuth=true \
ABAC_Database__ConnectionString="Data Source=abac-controller.db" \
~/.dotnet/dotnet run --project src/AbacController.Api/AbacController.Api.csproj
```

Optional tenant header used throughout the examples:

```bash
TENANT_HEADER='X-Tenant-Id: tenant-a'
```

---

## 2) Import a SPIF

```bash
curl -X POST http://localhost:8080/pap/api/spifs/import \
  -H 'Content-Type: application/json' \
  -H "$TENANT_HEADER" \
  -d @- <<'JSON'
{
  "xml": "<SPIF xmlns=\"urn:nato:stanag:4774:confidentialitymetadatalabel:1:0\" schemaVersion=\"3.0\"><securityPolicyId id=\"1.2.3.4\" name=\"TEST\"/><securityClassifications><securityClassification name=\"SECRET\" lacv=\"3\" hierarchy=\"3\"/></securityClassifications></SPIF>",
  "activate": true,
  "setAsDefault": true,
  "importedBy": "local-dev"
}
JSON
```

### Export a SPIF

```bash
curl -H "$TENANT_HEADER" \
  http://localhost:8080/pap/api/spifs/{spifId}/export
```

### List tenant SPIFs

```bash
curl -H "$TENANT_HEADER" \
  http://localhost:8080/pap/api/spifs
```

---

## 3) Create a policy set

```bash
curl -X PUT http://localhost:8080/pap/api/policy-sets/ps1 \
  -H 'Content-Type: application/json' \
  -H "$TENANT_HEADER" \
  -d '{
    "id": "ps1",
    "name": "Default Policy Set",
    "description": "Tenant A default policy set",
    "combiningAlgorithm": "deny-overrides",
    "isActive": true,
    "policies": []
  }'
```

Supported usage patterns include combining algorithms such as `deny-overrides` and `permit-overrides` depending on your policy model.

---

## 4) Create a policy

```bash
curl -X PUT http://localhost:8080/pap/api/policies/policy1 \
  -H 'Content-Type: application/json' \
  -H "$TENANT_HEADER" \
  -d '{
    "id": "policy1",
    "policySetId": "ps1",
    "name": "Permit engineering readers",
    "format": "native",
    "versions": []
  }'
```

---

## 5) Create and activate a policy version

```bash
curl -X POST http://localhost:8080/pap/api/policies/policy1/versions \
  -H 'Content-Type: application/json' \
  -H "$TENANT_HEADER" \
  -d '{
    "content": "{\"id\":\"rule1\",\"effect\":\"Permit\",\"conditions\":[{\"path\":\"subject.department\",\"equals\":\"engineering\",\"caseInsensitive\":true},{\"path\":\"action.name\",\"equals\":\"read\",\"caseInsensitive\":true},{\"path\":\"resource.type\",\"equals\":\"document\",\"caseInsensitive\":true}]}",
    "createdBy": "local-dev",
    "activate": true
  }'
```

### List versions

```bash
curl -H "$TENANT_HEADER" \
  http://localhost:8080/pap/api/policies/policy1/versions
```

### Diff two versions

```bash
curl -H "$TENANT_HEADER" \
  http://localhost:8080/pap/api/policies/policy1/versions/{leftVersionId}/diff/{rightVersionId}
```

### Roll back to an earlier version

```bash
curl -X POST http://localhost:8080/pap/api/policies/policy1/versions/{versionId}/rollback \
  -H 'Content-Type: application/json' \
  -H "$TENANT_HEADER" \
  -d '{
    "createdBy": "local-dev",
    "reason": "rollback after bad rollout"
  }'
```

---

## 6) Evaluate with AuthZEN

```bash
curl -X POST http://localhost:8080/access/v1/evaluation \
  -H 'Content-Type: application/json' \
  -H "$TENANT_HEADER" \
  -d '{
    "requestId": "req-1",
    "subject": {
      "type": "user",
      "id": "user-123",
      "properties": {
        "department": "engineering",
        "securityClearance": {
          "policyOid": "1.2.3.4",
          "classificationLacvs": [3],
          "categoryTagSets": []
        }
      }
    },
    "action": { "name": "read", "properties": {} },
    "resource": {
      "type": "document",
      "id": "doc-123",
      "properties": {
        "securityLabel": {
          "policyOid": "1.2.3.4",
          "classificationLacv": 3,
          "classificationName": "SECRET",
          "categoryTagSets": []
        }
      }
    }
  }'
```

Response shape:

```json
{
  "decision": true,
  "context": {
    "id": "...",
    "reason_admin": "Permitted by policy-set:ps1, policy:policy1@v1"
  }
}
```

---

## 7) Batch evaluation

```bash
curl -X POST http://localhost:8080/access/v1/evaluations \
  -H 'Content-Type: application/json' \
  -H "$TENANT_HEADER" \
  -d '{
    "evaluations": [
      {
        "requestId": "batch-1",
        "subject": { "type": "user", "id": "user-123", "properties": { "department": "engineering" } },
        "action": { "name": "read", "properties": {} },
        "resource": { "type": "document", "id": "doc-1", "properties": {} }
      },
      {
        "requestId": "batch-2",
        "subject": { "type": "user", "id": "user-999", "properties": { "department": "finance" } },
        "action": { "name": "read", "properties": {} },
        "resource": { "type": "document", "id": "doc-2", "properties": {} }
      }
    ]
  }'
```

---

## 8) Explain evaluation

```bash
curl -X POST http://localhost:8080/pdp/api/evaluate/explain \
  -H 'Content-Type: application/json' \
  -H "$TENANT_HEADER" \
  -d '{
    "requestId": "explain-1",
    "subject": { "id": "user-123", "attributes": { "department": "engineering" } },
    "action": { "name": "read" },
    "resource": { "id": "doc-123", "type": "document" }
  }'
```

Use this when you need decision traces rather than a simple permit/deny answer.

---

## 9) Simulation mode

Simulation evaluates without polluting the normal decision cache and without treating the run like a normal production evaluation path.

```bash
curl -X POST http://localhost:8080/pdp/api/evaluate/simulate \
  -H 'Content-Type: application/json' \
  -H "$TENANT_HEADER" \
  -d '{
    "requestId": "sim-1",
    "subject": { "type": "user", "id": "user-123", "properties": { "department": "engineering" } },
    "action": { "name": "delete", "properties": {} },
    "resource": { "type": "document", "id": "doc-123", "properties": {} }
  }'
```

Good for impact analysis before policy activation.

---

## 10) Async evaluation with webhook callback

```bash
curl -X POST http://localhost:8080/pdp/api/evaluate/async \
  -H 'Content-Type: application/json' \
  -H "$TENANT_HEADER" \
  -d '{
    "requestId": "async-1",
    "subject": { "type": "user", "id": "user-123", "properties": {} },
    "action": { "name": "read", "properties": {} },
    "resource": { "type": "document", "id": "doc-123", "properties": {} },
    "callbackUrl": "https://example.com/webhooks/abac",
    "callbackHeaders": {
      "Authorization": "Bearer callback-token"
    }
  }'
```

Check status later:

```bash
curl http://localhost:8080/pdp/api/evaluate/async/{evaluationId}/status
```

---

## 11) Evaluate using XACML JSON input

```bash
curl -X POST http://localhost:8080/access/v1/xacml-json \
  -H 'Content-Type: application/json' \
  -H "$TENANT_HEADER" \
  -d '{
    "Request": {
      "AccessSubject": { "Attribute": [{ "AttributeId": "subject.id", "Value": "user-123" }] },
      "Action": { "Attribute": [{ "AttributeId": "action.id", "Value": "read" }] },
      "Resource": { "Attribute": [{ "AttributeId": "resource.id", "Value": "doc-123" }] }
    }
  }'
```

---

## 12) Search AuthZEN entity history

```bash
curl -X POST http://localhost:8080/access/v1/subjects \
  -H 'Content-Type: application/json' \
  -H "$TENANT_HEADER" \
  -d '{ "query": "user-", "limit": 10 }'
```

Equivalent endpoints exist for:

- `/access/v1/resources`
- `/access/v1/actions`

---

## 13) Metadata binding

### Bind a label and payload

```bash
curl -X POST http://localhost:8080/pep/api/metadata/bind \
  -H 'Content-Type: application/json' \
  -H "$TENANT_HEADER" \
  -d '{
    "bindingId": "bind-1",
    "labelXml": "<securityLabel />",
    "payloadText": "hello",
    "mediaType": "text/plain"
  }'
```

### Unbind an envelope

```bash
curl -X POST http://localhost:8080/pep/api/metadata/unbind \
  -H 'Content-Type: application/json' \
  -H "$TENANT_HEADER" \
  -d '{ "envelopeXml": "..." }'
```

### List available metadata codecs

```bash
curl -H "$TENANT_HEADER" \
  http://localhost:8080/pep/api/metadata/codecs
```

---

## 14) PIP source administration

### List sources

```bash
curl -H "$TENANT_HEADER" \
  http://localhost:8080/pip/api/sources
```

### Upsert a static source

```bash
curl -X PUT http://localhost:8080/pip/api/sources/hr-static \
  -H 'Content-Type: application/json' \
  -H "$TENANT_HEADER" \
  -d '{
    "id": "hr-static",
    "name": "HR Static",
    "sourceType": "static",
    "configJson": "{\"subjects\":{\"user-123\":{\"department\":\"engineering\",\"securityClearance\":{\"policyOid\":\"1.2.3.4\",\"classificationLacvs\":[3]}}}}",
    "providesAttributes": "department,securityClearance",
    "priority": 10,
    "isRequired": false,
    "cacheEnabled": true,
    "cacheTtlSeconds": 300,
    "cacheMaxEntries": 1000
  }'
```

Static source config also accepts a wildcard/default form when you want the same attributes for any subject:

```json
{
  "attributes": {
    "department": "engineering"
  }
}
```

Supported source config keys by `sourceType`:

- `static`: `subjects` or `attributes`
- `file`: `filePath` or `path`
- `rest`: `urlTemplate` or `url`
- `oidc`: `userInfoEndpoint` plus `claimMapping`
- `ldap`: `host`, `baseDn`, `subjectIdAttribute` and optional `port`, `useSsl`, `username`, `password`

Concrete `configJson` examples:

#### Static source with per-subject values

```json
{
  "subjects": {
    "user-123": {
      "department": "engineering",
      "securityClearance": {
        "policyOid": "1.2.3.4",
        "classificationLacvs": [3]
      }
    },
    "user-999": {
      "department": "finance"
    }
  }
}
```

#### REST source

```json
{
  "urlTemplate": "https://attributes.example.com/users/{subjectId}"
}
```

Pair it with `providesAttributes` such as:

```text
department,managerId,costCenter
```

#### OIDC UserInfo source

```json
{
  "userInfoEndpoint": "https://issuer.example.com/connect/userinfo",
  "claimMapping": {
    "department": "department",
    "groups": "groups",
    "email": "email"
  }
}
```

The runtime expects the incoming evaluation context to already carry a bearer token when OIDC resolution is used.

#### LDAP source

```json
{
  "host": "ldap.example.com",
  "port": 636,
  "useSsl": true,
  "baseDn": "ou=people,dc=example,dc=com",
  "subjectIdAttribute": "uid",
  "username": "cn=svc-abac,ou=svc,dc=example,dc=com",
  "password": "replace-me"
}
```

Typical `providesAttributes` value:

```text
department,title,memberOf
```

### Validate persisted-source runtime resolution end-to-end

After upserting a source, prove the running controller is actually consuming the persisted record by evaluating a request that omits a policy-required subject attribute and lets the PIP source supply it.

Example shape:

1. Create a policy that permits only when `subject.department == engineering`.
2. Upsert a static source that provides `department` for `user-123`.
3. Send an AuthZEN request for `user-123` without `subject.properties.department`.
4. Expect a permit decision because runtime enrichment resolved the missing attribute from the persisted source.

This exact flow is captured in `docs/smoke-test-validation.md` and in the Docker-backed integration test `PersistedPipSourceTests`.

### Test a source

```bash
curl -X POST -H "$TENANT_HEADER" \
  http://localhost:8080/pip/api/sources/hr-static/test
```

The response reports live connectivity or config validity for that persisted source definition, for example:

```json
{
  "id": "hr-static",
  "sourceType": "static",
  "healthy": true,
  "message": "Static source always healthy"
}
```

### Run health checks

```bash
curl -H "$TENANT_HEADER" \
  http://localhost:8080/pip/api/health
```

### Read cached health

```bash
curl -H "$TENANT_HEADER" \
  http://localhost:8080/pip/api/health/cached
```

### Invalidate PIP cache for one subject

```bash
curl -X POST -H "$TENANT_HEADER" \
  http://localhost:8080/pip/api/cache/invalidate/user-123
```

### Invalidate all PIP cache entries

```bash
curl -X POST -H "$TENANT_HEADER" \
  http://localhost:8080/pip/api/cache/invalidate-all
```

---

## 15) Policy conflict checking

```bash
curl -X POST http://localhost:8080/pap/api/policies/check-conflicts \
  -H 'Content-Type: application/json' \
  -H "$TENANT_HEADER" \
  -d '{
    "content": "{\"id\":\"candidate\",\"effect\":\"Deny\",\"conditions\":[{\"path\":\"action.name\",\"equals\":\"delete\"}]}"
  }'
```

---

## 16) Audit and system views

### Tenant-scoped policy/admin audit

```bash
curl -H "$TENANT_HEADER" \
  'http://localhost:8080/pap/api/audit?page=1&pageSize=25'
```

### System-wide audit query surface

```bash
curl -H "$TENANT_HEADER" \
  'http://localhost:8080/system/api/audit?page=1&pageSize=25'
```

### Get one audit event

```bash
curl -H "$TENANT_HEADER" \
  http://localhost:8080/system/api/audit/{id}
```

### Runtime status/config

```bash
curl -H "$TENANT_HEADER" http://localhost:8080/system/api/info
curl -H "$TENANT_HEADER" http://localhost:8080/system/api/config
```

### Enforcement points

```bash
curl -H "$TENANT_HEADER" http://localhost:8080/system/api/enforcement-points
```

---

## 17) Metrics and health

```bash
curl http://localhost:8080/metrics
curl http://localhost:8080/health/live
curl http://localhost:8080/health/ready
curl http://localhost:8080/health/startup
```

---

## 18) API key authentication example

```bash
curl -X POST http://localhost:8080/access/v1/evaluation \
  -H 'Content-Type: application/json' \
  -H 'X-API-Key: super-secret-key' \
  -d '{
    "requestId": "apikey-1",
    "subject": { "type": "user", "id": "user-123", "properties": {} },
    "action": { "name": "read", "properties": {} },
    "resource": { "type": "document", "id": "doc-123", "properties": {} }
  }'
```

---

## 19) Rate limiting behavior

PDP-facing evaluation routes are rate-limited. When limits are exceeded, the API returns:

- `429 Too Many Requests`

Use this to validate configuration during ops testing by temporarily lowering the limits.
