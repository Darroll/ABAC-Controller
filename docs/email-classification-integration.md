# Email Classification × ABAC Controller integration

This document describes the server-side extensions the ABAC Controller exposes
so the Email Classification API can delegate all of its SPIF, classification
and clearance concerns to a single central authority.

## Roles

- **ABAC Controller** — SPIF storage, classification query, entitlement model
  (baseline + group + user grants and denies), clearance via PIP, recipient
  check endpoint, webhook publisher.
- **Email Classification API** — Outlook-specific BFF. Owns the add-in contract,
  send-gate overlay logic, Outlook admin UI, and a webhook subscriber that
  re-broadcasts ABAC change events to connected add-in clients via its existing
  SignalR hub. Does NOT own SPIFs, assignments or clearance.
- **Outlook add-in (Blazor WASM)** — talks only to the Email Classification API.

## Endpoint map

### Classification load (Outlook compose open)

```
Add-in → POST /api/policies/active (Email JWT)
  Email API
    → POST /pdp/api/classifications/query (forwarded JWT + EnforceEntitlements=true)
      ABAC PDP ClassificationQueryEngine:
        1. Resolve SPIF (application default = "email-classification")
        2. PIP.Resolve(subject) → securityClearance, groups   [docs/pip-clearance-setup.md]
        3. EntitlementResolver(tenantId, subjectId, groupIds) → ResolvedEntitlements
        4. Filter: policy ∩ entitlements ∩ clearance ∩ app_scope
        5. Audit: classification_query
      ← AllowedClassificationsResult
  ← ActivePoliciesResponse (translator)
```

### Recipient check at send

```
Add-in send-gate → POST /api/policies/check-recipients
  Email API → POST /pdp/api/recipients/check (forwarded JWT)
    For each recipient:
      PIP.Resolve(recipient) → securityClearance
      Hierarchy dominance check (LACV ≥ target)
    Aggregate Permit|Deny + per-recipient reasons
  ← RecipientCheckResult[]
```

### Webhook propagation (near-real-time policy change)

```
ABAC PAP write (SPIF, policy, entitlement or application)
  → WebhookPublisher.PublishAsync → outbox + Channel signal
  WebhookDispatcherHostedService
    → HMAC-SHA256 sign body (sha256=HEX)
    → POST {subscription.CallbackUrl}
         X-Abac-Signature, X-Abac-Event-Id, X-Abac-Event-Type
  Email API /internal/abac-events
    → validate HMAC + dedupe eventId
    → IPolicyChangeNotifier → /hubs/policy → add-ins
```

Retry schedule: 30 s → 2 m → 10 m. After 3 failed attempts the row is
`DeadLetter`ed. Subscribers can catch up with:

```
GET /pap/api/webhooks/{id}/events?since=2026-04-07T00:00:00Z
```

## New scopes

| Scope | Grants |
|---|---|
| `abac:entitlement:admin` | baseline/group/user entitlement CRUD |
| `abac:recipient:check` | recipient clearance checks |
| `abac:webhook:admin` | webhook subscription CRUD + replay |
| `abac:audit:mirror:write` | mirror external audit events (Phase B) |

## New endpoints

- `GET/POST/DELETE /pap/api/entitlements/{tenantId}/baseline`
- `GET/POST/DELETE /pap/api/entitlements/{tenantId}/groups/{groupId}`
- `GET /pap/api/entitlements/{tenantId}/users/{userId}`
- `POST /pap/api/entitlements/{tenantId}/users/{userId}/grants`
- `POST /pap/api/entitlements/{tenantId}/users/{userId}/denies`
- `DELETE /pap/api/entitlements/{tenantId}/users/{userId}?policyOid=...&classificationLacv=...`
- `POST /pdp/api/recipients/check`
- `GET/POST/DELETE /pap/api/webhooks`
- `GET /pap/api/webhooks/{id}/events?since=...`

## Entitlement semantics

`ResolvedEntitlements.Permits(policyOid, classificationLacv)`:

1. Whole-policy deny → **false**
2. Classification-level deny for that LACV → **false**
3. Whole-policy grant → **true**
4. Classification-level grant for that LACV → **true**
5. Otherwise → **false**

The Classification Query Engine applies this as a filter layer between clearance
and native policy, but only when the caller sets
`ClassificationAssignmentQuery.EnforceEntitlements = true`. Callers that pre-date
the entitlement work continue to see the old three-layer behaviour.

## Application registration

The Email Classification API is registered in ABAC as a single application:

```json
PUT /pap/api/applications/email-classification
{
  "name": "Email Classification",
  "description": "Outlook Web Add-in classification BFF",
  "defaultPolicyOid": "2.16.840.1.101.2.1.8.2.1",
  "allowedClassificationLacvs": [1, 2, 3],
  "maxClassificationHierarchy": 3,
  "allowedTagSetOids": [],
  "isActive": true
}
```

The `ApplicationRegistration` row acts as the shared app-scope filter across
all Outlook tenants.
