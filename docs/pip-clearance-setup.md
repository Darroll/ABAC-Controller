# Configuring PIP sources for user clearance

The ABAC Controller's Policy Information Point (PIP) is the authoritative source
for the `securityClearance` attribute used by the Classification Query Engine
(`ClassificationQueryEngine`) and the Recipient Check endpoint
(`POST /pdp/api/recipients/check`).

This document shows how to configure PIP sources so that every subject the PDP
evaluates — including recipients checked by a client such as Email
Classification — has a `securityClearance` value resolved at request time.

Two source shapes cover the common cases:

1. **OIDC source** — pointed at Entra ID / Azure AD, pulling a custom security
   attribute from the UserInfo endpoint.
2. **LDAP source** — pointed at an Active Directory / OpenLDAP instance, for
   certificate-authenticated subjects where clearance is published via
   `SubjectDirectoryAttributes`.

## Target attribute

In every case the PIP source must emit an attribute whose name matches the
string literal `securityClearance` and whose value deserializes into the
`AbacController.Core.Domain.Labels.SecurityClearance` record. That record
carries:

- `PolicyOid` — the SPIF OID the clearance applies to
- `ClassificationLacvs` — the set of permitted classification LACV values
- `CategoryClearances` — per-tag-set category permissions (optional for v1)

Fail-closed default: when the PIP cannot resolve a `securityClearance` value,
the engine treats the subject as having no clearance and all classifications are
filtered out (see `ClassificationQueryEngine.EvaluateClearanceFilter`).

## OIDC source (Entra ID)

Create a PIP source via `POST /pip/api/sources` with body:

```json
{
  "id": "entra-clearance",
  "name": "Entra ID custom attribute for user clearance",
  "sourceType": "oidc",
  "providesAttributes": ["securityClearance"],
  "priority": 10,
  "cacheTtlSeconds": 300,
  "configJson": {
    "userInfoEndpoint": "https://login.microsoftonline.com/{tenant}/openid/userinfo",
    "claimMapping": {
      "securityClearance": "extension_securityClearance"
    }
  }
}
```

The PIP resolver fetches the OIDC UserInfo payload for the subject's token, reads
`extension_securityClearance` (or whichever claim name your Entra extension
exposes), and projects it onto `securityClearance`.

Define the Entra custom attribute with a JSON-encoded value such as:

```json
{
  "policyOid": "2.16.840.1.101.2.1.8.2.1",
  "classificationLacvs": [1, 2, 3]
}
```

## LDAP source (X.509 / cert-authenticated subjects)

For deployments where Email Classification users authenticate with an S/MIME or
client certificate, publish the clearance in a directory attribute and resolve
it via LDAP:

```json
{
  "id": "ldap-clearance",
  "name": "Directory clearance attribute",
  "sourceType": "ldap",
  "providesAttributes": ["securityClearance"],
  "priority": 20,
  "cacheTtlSeconds": 300,
  "configJson": {
    "host": "ldap.example.internal",
    "port": 636,
    "useSsl": true,
    "baseDn": "ou=people,dc=example,dc=internal",
    "filter": "(userPrincipalName={subjectId})",
    "attributeName": "securityClearance"
  }
}
```

## Development / pilot

Before pointing the PIP at real Entra or LDAP, you can use a static source to
smoke-test the classification load and recipient check flows:

```json
{
  "id": "static-clearance",
  "name": "Static clearance (development)",
  "sourceType": "static",
  "providesAttributes": ["securityClearance"],
  "priority": 1,
  "configJson": {
    "subjects": {
      "alice@example.com": {
        "securityClearance": {
          "policyOid": "2.16.840.1.101.2.1.8.2.1",
          "classificationLacvs": [1, 2, 3]
        }
      },
      "bob@example.com": {
        "securityClearance": {
          "policyOid": "2.16.840.1.101.2.1.8.2.1",
          "classificationLacvs": [1]
        }
      }
    }
  }
}
```

Use this to verify the Email Classification integration end-to-end before
depending on a live directory.

## Caching & invalidation

PIP values are cached per source/per subject/per attribute. The default TTL is
300 s for the sources above. When you change a user's clearance upstream, invalidate
the cache for that subject via `POST /pip/api/cache/invalidate/{subjectId}`.

## Multi-source resolution

`IPipResolver` walks sources in ascending `priority` order and uses the first
source that returns a non-null `securityClearance`. Configure your highest-trust
source (typically OIDC/Entra) with the lowest priority number so it wins when
available, and configure LDAP as a fallback.
