# USAGE

## Quick start flow

1. Start the API with development auth enabled.
2. Import a SPIF.
3. Create a policy set.
4. Create a policy.
5. Create and activate a policy version.
6. Evaluate access through AuthZEN or gRPC.

## Import a SPIF

```bash
curl -X POST http://localhost:8080/pap/api/spifs/import \
  -H 'Content-Type: application/json' \
  -d @- <<'JSON'
{
  "xml": "<SPIF xmlns=\"urn:nato:stanag:4774:confidentialitymetadatalabel:1:0\" schemaVersion=\"3.0\"><securityPolicyId id=\"1.2.3.4\" name=\"TEST\"/><securityClassifications><securityClassification name=\"SECRET\" lacv=\"3\" hierarchy=\"3\"/></securityClassifications></SPIF>",
  "activate": true,
  "setAsDefault": true,
  "importedBy": "local-dev"
}
JSON
```

## Create a policy set

```bash
curl -X PUT http://localhost:8080/pap/api/policy-sets/ps1 \
  -H 'Content-Type: application/json' \
  -d '{
    "id": "ps1",
    "name": "Default Policy Set",
    "description": "Default policy set",
    "combiningAlgorithm": "deny-overrides",
    "isActive": true,
    "policies": []
  }'
```

## Create a policy

```bash
curl -X PUT http://localhost:8080/pap/api/policies/policy1 \
  -H 'Content-Type: application/json' \
  -d '{
    "id": "policy1",
    "policySetId": "ps1",
    "name": "Permit engineering readers",
    "format": "native",
    "versions": []
  }'
```

## Create a policy version

```bash
curl -X POST http://localhost:8080/pap/api/policies/policy1/versions \
  -H 'Content-Type: application/json' \
  -d '{
    "content": "{\"id\":\"rule1\",\"effect\":\"Permit\",\"conditions\":[{\"path\":\"subject.department\",\"equals\":\"engineering\",\"caseInsensitive\":true},{\"path\":\"action.name\",\"equals\":\"read\",\"caseInsensitive\":true},{\"path\":\"resource.type\",\"equals\":\"document\",\"caseInsensitive\":true}]}",
    "createdBy": "local-dev",
    "activate": true
  }'
```

## Evaluate with AuthZEN

```bash
curl -X POST http://localhost:8080/access/v1/evaluation \
  -H 'Content-Type: application/json' \
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

## Metadata binding

Bind:

```bash
curl -X POST http://localhost:8080/pep/api/metadata/bind \
  -H 'Content-Type: application/json' \
  -d '{
    "bindingId": "bind-1",
    "labelXml": "<securityLabel />",
    "payloadText": "hello",
    "mediaType": "text/plain"
  }'
```

Unbind:

```bash
curl -X POST http://localhost:8080/pep/api/metadata/unbind \
  -H 'Content-Type: application/json' \
  -d '{ "envelopeXml": "..." }'
```

## Discovery and system endpoints

- `GET /.well-known/authzen-configuration`
- `GET /system/api/info`
- `GET /system/api/config`
- `GET /system/api/enforcement-points`
- `GET /system/api/audit`
- `GET /pap/api/audit`
- `GET /pip/api/health`
- `GET /pip/api/health/cached`

## Extra admin examples

### Check policy conflicts

```bash
curl -X POST http://localhost:8080/pap/api/policies/check-conflicts \
  -H 'Content-Type: application/json' \
  -d '{
    "content": "{\"id\":\"candidate\",\"effect\":\"Deny\",\"conditions\":[{\"path\":\"action.name\",\"equals\":\"delete\"}]}"
  }'
```

### View cached PIP health

```bash
curl http://localhost:8080/pip/api/health/cached
```

### Query the full system audit stream

```bash
curl 'http://localhost:8080/system/api/audit?page=1&pageSize=25'
```
