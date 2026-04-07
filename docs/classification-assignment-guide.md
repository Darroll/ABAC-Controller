# Classification Assignment Guide

This guide explains how applications query the ABAC Controller to determine which security classifications a user is permitted to assign. The ABAC Controller is the **authoritative source of truth** for classification policy — applications render the allowed set but never filter locally.

## Architecture

The allowed-classifications query applies three filter layers in sequence:

```
SPIF Classifications (all non-obsolete)
  │
  ▼
Layer 1: Clearance Filter
  Hierarchy dominance check against subject's SecurityClearance.
  Removes classifications above the subject's clearance level.
  │
  ▼
Layer 2: Native Policy Filter
  Evaluates native policy rules with action="classify" and
  resource.type="classification". NotApplicable = allowed;
  only explicit Deny blocks a classification.
  │
  ▼
Layer 3: Application Scope Filter
  Applies registered application constraints:
  LACV whitelist, hierarchy ceiling, tag set whitelist.
  │
  ▼
Allowed Classifications (returned to caller)
```

### Where policy lives vs. where UI filtering lives

| Concern | Owner | Examples |
|---------|-------|---------|
| Which classifications exist | PAP (SPIF) | UNCLASSIFIED, CONFIDENTIAL, SECRET, TOP SECRET |
| Who may assign which classification | PAP (policies + clearance) | "Group A may classify up to SECRET" |
| Which classifications an app may use | PAP (application registration) | "Email app may only use U, C, S" |
| How to display classifications in a specific screen | Application (local UI logic) | Sort order, grouping, disabled states |
| Default classification for a compose window | Application (local preference) | "Default to UNCLASSIFIED" |

## Authentication

The classification query endpoints require the `abac:classification:query` scope. Application management requires `abac:application:admin`.

## Application Registration

Register an application to define its classification scope constraints:

```bash
curl -X PUT http://localhost:8080/pap/api/applications/email-classification \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  -d '{
    "name": "Email Classification",
    "description": "Outlook classification add-in",
    "defaultPolicyOid": "2.16.840.1.101.2.1.3.13",
    "allowedClassificationLacvs": [1, 2, 3],
    "maxClassificationHierarchy": 3,
    "allowedTagSetOids": ["2.16.840.1.101.2.1.8.3.0"],
    "isActive": true
  }'
```

Fields:
- `allowedClassificationLacvs`: Whitelist of LACV values. Empty array = all allowed.
- `maxClassificationHierarchy`: Ceiling on hierarchy value. Null = no ceiling.
- `allowedTagSetOids`: Whitelist of tag set OIDs. Empty = all allowed.

## Querying Allowed Classifications

### Single Query

```bash
curl -X POST http://localhost:8080/pdp/api/classifications/allowed \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  -d '{
    "subject": {
      "type": "user",
      "id": "user-123",
      "properties": {
        "securityClearance": {
          "policyOid": "2.16.840.1.101.2.1.3.13",
          "classificationLacvs": [1, 2, 3],
          "categoryTagSets": [
            {
              "tagSetOid": "2.16.840.1.101.2.1.8.3.0",
              "tags": [
                {
                  "tagType": "Restrictive",
                  "bits": [10, 11]
                }
              ]
            }
          ]
        },
        "groups": ["engineering", "cleared-staff"]
      }
    },
    "applicationId": "email-classification",
    "resource": {
      "type": "email",
      "id": "draft-456"
    },
    "includeCategories": true,
    "includeMarkingData": false,
    "includeTrace": false
  }'
```

### Response Structure

```json
{
  "requestId": null,
  "resultId": "abc123def456",
  "policyOid": "2.16.840.1.101.2.1.3.13",
  "policyName": "US DoD",
  "applicationId": "email-classification",
  "tenantId": null,
  "classifications": [
    {
      "name": "UNCLASSIFIED",
      "lacv": 1,
      "hierarchy": 1,
      "fgColor": "#000000",
      "bgColor": "#00FF00",
      "allowedCategories": [...],
      "requiredCategories": [],
      "excludedCategories": []
    },
    {
      "name": "CONFIDENTIAL",
      "lacv": 2,
      "hierarchy": 2,
      "fgColor": "#FFFFFF",
      "bgColor": "#0000FF",
      "allowedCategories": [...],
      "requiredCategories": [],
      "excludedCategories": []
    }
  ],
  "totalSpifClassifications": 5,
  "evaluationTime": "00:00:00.0032000",
  "trace": null
}
```

### Batch Query

```bash
curl -X POST http://localhost:8080/pdp/api/classifications/allowed/batch \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  -d '{
    "queries": [
      { "subject": {...}, "applicationId": "email-classification" },
      { "subject": {...}, "applicationId": "doc-management" }
    ]
  }'
```

Maximum 50 queries per batch.

## Writing Classification Assignment Policies

Native policies can restrict which classifications specific users or groups may assign. Create policy rules with `action.name = "classify"` and `resource.type = "classification"`:

### Example: Restrict group to SECRET and below

```json
{
  "combiningAlgorithm": "deny-overrides",
  "rules": [
    {
      "id": "deny-ts-for-basic-group",
      "effect": "Deny",
      "conditions": [
        { "path": "action.name", "equals": "classify" },
        { "path": "resource.properties.classificationHierarchy", "equals": 4 },
        { "path": "subject.properties.groups", "contains": "basic-users" }
      ]
    }
  ]
}
```

### Example: Only allow specific application to use certain classifications

```json
{
  "rules": [
    {
      "id": "deny-ts-for-email",
      "effect": "Deny",
      "conditions": [
        { "path": "action.name", "equals": "classify" },
        { "path": "resource.properties.classificationHierarchy", "equals": 4 },
        { "path": "resource.properties.applicationId", "equals": "email-classification" }
      ]
    }
  ]
}
```

### Policy evaluation semantics

- `Deny` → classification is blocked
- `Permit` → classification is explicitly allowed
- `NotApplicable` → no policy addresses this classification; **defaults to allowed**
- `Indeterminate` → evaluation error; classification is blocked (fail-closed)

## Migration from EmailClassification Local Filtering

If your application currently filters classifications locally (e.g., via `ClassificationFilterService`):

1. **Register the application** via `PUT /pap/api/applications/{id}` with appropriate scope constraints.
2. **Move clearance-based filtering** to the ABAC Controller by including `securityClearance` in subject properties.
3. **Move group/role-based rules** into native policies in the PAP.
4. **Replace local filter calls** with `POST /pdp/api/classifications/allowed`.
5. **Keep only UI-specific filtering** in the application (display order, grouping, default selection).

The application should cache the allowed-classifications result per session and refresh on policy change or clearance update.

## Diagnostic Trace

Set `includeTrace: true` to get a step-by-step breakdown of how each classification was filtered:

```json
{
  "trace": {
    "steps": [
      {
        "classificationName": "TOP SECRET",
        "lacv": 4,
        "filterLayer": "clearance_filter",
        "passed": false,
        "reason": "Clearance does not dominate hierarchy 4"
      },
      {
        "classificationName": "SECRET",
        "lacv": 3,
        "filterLayer": "clearance_filter",
        "passed": true,
        "reason": "Clearance permits hierarchy 3"
      }
    ]
  }
}
```

## Tenant Isolation

All queries are scoped to the tenant identified by the `X-Tenant-Id` header or `tenant_id` JWT claim. Different tenants may have different SPIFs, policies, and application registrations.

## API Reference

| Endpoint | Method | Auth Scope | Purpose |
|----------|--------|-----------|---------|
| `/pdp/api/classifications/allowed` | POST | `abac:classification:query` | Query allowed classifications |
| `/pdp/api/classifications/allowed/batch` | POST | `abac:classification:query` | Batch query (max 50) |
| `/pap/api/applications` | GET | `abac:application:admin` | List applications |
| `/pap/api/applications/{id}` | GET | `abac:application:admin` | Get application |
| `/pap/api/applications/{id}` | PUT | `abac:application:admin` | Upsert application |
| `/pap/api/applications/{id}` | DELETE | `abac:application:admin` | Delete application |
