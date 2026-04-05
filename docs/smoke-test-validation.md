# Smoke Test Validation

This document records a manual v1 smoke pass against the current ABAC Controller container image and admin UI.

## Scope

Validated from a running container:

- Blazor admin shell loads
- PIP source CRUD/list/test flow
- SPIF import/list/export flow
- PAP policy set + policy + version flow
- System info and audit endpoints
- AuthZEN evaluation request behavior with the current runtime expectations

## Environment

Validated from the repo root with:

- Image build: `sg docker -c "docker build -t abac-controller-smoke ."`
- Container run:

```bash
sg docker -c "docker run -d --name abac-controller-smoke-debug \
  -p 18080:8080 -p 18081:8081 \
  -e ASPNETCORE_ENVIRONMENT=Development \
  -e ABAC_Auth__EnableDevelopmentAuth=true \
  -e ABAC_Database__ConnectionString='Data Source=/data/abac-controller.db' \
  abac-controller-smoke"
```

Base URL used for validation: `http://127.0.0.1:18080`

## Exact Smoke Flow

A disposable smoke identifier was used so the run could create and then delete test records.

### 1. Confirm the admin UI shell loads

```bash
curl -fsS http://127.0.0.1:18080/ | grep -o 'ABAC Controller — Dashboard'
```

Observed result:

- `ABAC Controller — Dashboard`

### 2. Create and list a PIP source

Example request shape:

```bash
curl -X PUT http://127.0.0.1:18080/pip/api/sources/<smoke-id> \
  -H 'Content-Type: application/json' \
  -d '{
    "id": "<smoke-id>",
    "name": "Smoke Static Source",
    "sourceType": "static",
    "configJson": "{\"attributes\":{\"subject.department\":\"engineering\"}}",
    "providesAttributes": "subject.department",
    "priority": 10,
    "isRequired": false,
    "cacheEnabled": true,
    "cacheTtlSeconds": 300,
    "cacheMaxEntries": 100
  }'
```

Observed result:

- `200 OK`
- Source echoed back with the expected `id`
- `GET /pip/api/sources` contained the new source

### 3. Exercise the PIP source test endpoint

```bash
curl -X POST http://127.0.0.1:18080/pip/api/sources/<smoke-id>/test
```

Observed result:

- `200 OK`
- Response message:
  - `Connectivity test endpoint present; runtime connector-specific active test not wired for persisted sources yet.`

Interpretation:

- Route works end-to-end and is UI-usable.
- Current persisted-source test behavior is a placeholder/availability check, not a connector-specific live probe.

### 4. Import, list, and export a SPIF

A minimal valid XMLSPIF sample was used:

```xml
<spif:SPIF xmlns:spif="http://www.xmlspif.org/spif" schemaVersion="2.1" creationDate="20260101000000Z">
  <spif:securityPolicyId name="SMOKE" id="1.2.3.4.<unique>" />
  <spif:securityClassifications>
    <spif:securityClassification name="CONFIDENTIAL" lacv="2" hierarchy="2" />
    <spif:securityClassification name="SECRET" lacv="3" hierarchy="3" />
  </spif:securityClassifications>
</spif:SPIF>
```

Observed result:

- `POST /pap/api/spifs/import` returned `200 OK` with `success: true`
- `GET /pap/api/spifs` returned the imported SPIF
- `GET /pap/api/spifs/{id}/export` returned the original XML

### 5. Create a policy set, policy, and active version

Observed result:

- `PUT /pap/api/policy-sets/{id}` returned `200 OK`
- `PUT /pap/api/policies/{id}` returned `200 OK`
- `POST /pap/api/policies/{id}/versions` returned `200 OK`

### 6. Call AuthZEN evaluation

Observed result:

- `POST /access/v1/evaluation` returned `200 OK`
- Decision was `false`
- Runtime reason:
  - `Evaluation error: Missing security label in request`

Interpretation:

- The evaluation route is healthy.
- A bare AuthZEN request without the required security label does not currently produce a permit decision.
- This is important operator guidance for smoke testing: use labeled requests when validating successful policy enforcement flows.

### 7. Check system endpoints

Observed result:

- `GET /system/api/info` returned `200 OK`
- `GET /system/api/audit?page=1&pageSize=5` returned `200 OK`

This audit result is specifically important because a smoke run against the previous behavior exposed a SQLite failure path when ordering `DateTimeOffset` values directly.

## Issues Found During Smoke

### Blazor admin pages were still calling retired route prefixes

The admin UI was using legacy `api/v1/...` paths while the current server surface is split across:

- `pap/api/...`
- `pip/api/...`
- `system/api/...`

Impact before fix:

- PIP admin flows did not line up with the live API
- SPIF, policy, dashboard, system, and audit pages were also targeting stale paths
- SPIF edit flow expected a detail route that no longer exists

Fix applied:

- Updated Blazor admin pages to the current route prefixes
- Switched SPIF edit loading to use `GET /pap/api/spifs/{id}/export`

### SQLite audit query failure

Smoke validation against a running instance exposed:

- `GET /system/api/audit` could fail on SQLite with a `DateTimeOffset` ordering translation error

Fix applied:

- `AuditRepository` now uses a SQLite-safe in-memory ordering path after server-side filtering
- Same fix pattern applied to `GetByDecisionIdAsync`
- Added unit coverage for SQLite ordering behavior

## Validation Summary

Container-based smoke run result:

- UI shell: pass
- PIP create/list/test: pass
- SPIF import/list/export: pass
- PAP policy lifecycle basics: pass
- System info: pass
- System audit: pass after SQLite fix
- AuthZEN evaluation route health: pass
- AuthZEN permit outcome without security label: expected deny/error context under current runtime rules

## Cleanup

The smoke pass deleted the disposable records it created:

- test PIP source
- test policy
- test policy set
- test SPIF
