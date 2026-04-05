# EmailClassification → ABAC Controller v1 migration guide

This guide captures the expected migration path for teams moving from the earlier EmailClassification-style baseline to the current ABAC Controller v1 runtime.

## What changed

ABAC Controller v1 is not just a rename. The current runtime consolidates:

- SPIF import, validation, export, and registry management
- native policy-set / policy / version lifecycle management
- AuthZEN evaluation endpoints
- STANAG 4774 label encode/decode support
- PIP source management and health testing
- audit capture and query APIs
- an embedded Blazor administration UI

The old EmailClassification baseline was primarily an email-centric classification workflow. ABAC Controller generalizes that model into a broader ABAC control plane while preserving the core policy-label-clearance concepts.

## Capability mapping

| Legacy EmailClassification concept | ABAC Controller v1 equivalent |
|---|---|
| Classification policy baseline | SPIF document managed through PAP |
| Email classification vocabulary | SPIF classifications, tag sets, marking data, and equivalent policies |
| Classification policy editing | Blazor SPIF builder/editor plus PAP import/export APIs |
| Decision logic | PDP evaluation via AuthZEN or gRPC/JSON transcoding |
| External attribute lookups | PIP sources (`static`, `file`, `rest`, `ldap`, `oidc`) |
| Audit trail | System and PAP audit query endpoints |
| Admin console | Embedded Blazor admin UI |

## Recommended migration sequence

1. **Inventory the current EmailClassification baseline**
   - exported policy XML / classification metadata
   - custom tag vocabularies and dissemination controls
   - external lookup dependencies
   - integration points that currently call email-specific APIs

2. **Normalize the policy vocabulary into SPIF terms**
   - classifications → `securityClassification`
   - category groups / markers → `securityCategoryTagSet`
   - cross-domain mappings → `equivalentPolicy`, equivalent classifications, and category mappings
   - rendering text / abbreviations / codes → SPIF marking data

3. **Import SPIFs first**
   - import and validate the governing SPIF before migrating runtime decision logic
   - set the governing SPIF as default only after semantic validation passes

4. **Port decision logic into native policies**
   - migrate permit/deny rules into policy sets and versioned native policy documents
   - activate policies only after SPIF registration and smoke tests succeed

5. **Re-wire external context lookups into PIP sources**
   - static bootstrap data → `static`
   - file-based lookup tables → `file`
   - HTTP services → `rest`
   - directory lookups → `ldap`
   - bearer-token-backed identity enrichment → `oidc`

6. **Switch calling integrations to ABAC endpoints**
   - AuthZEN for authorization decisions
   - PAP endpoints for policy/SPIF administration
   - metadata endpoints for label binding/unbinding workflows

7. **Verify audit and UI workflows**
   - confirm decision IDs, applied policies, and attribute provenance appear as expected
   - validate the admin UI flows for SPIF import, policy version activation, and PIP health checks

## API mapping cheat sheet

### Decision APIs

Use AuthZEN-style evaluation for application integrations:

- `POST /access/v1/evaluation`
- `POST /access/v1/evaluations`
- `POST /access/v1/subjects`
- `POST /access/v1/resources`
- `POST /access/v1/actions`

Use PDP helper endpoints when you need richer diagnostics during migration:

- `POST /pdp/api/evaluate/explain`
- `POST /pdp/api/evaluate/simulate`
- `POST /pdp/api/evaluate/async`

### Policy and SPIF administration

Controller-based admin endpoints:

- `GET /pap/api/policy-sets`
- `PUT /pap/api/policy-sets/{id}`
- `PUT /pap/api/policies/{id}`
- `POST /pap/api/policies/{id}/versions`
- `POST /pap/api/policies/{id}/versions/{versionId}/activate`
- `GET /pap/api/spifs`
- `POST /pap/api/spifs/import`
- `GET /pap/api/spifs/{id}/export`

### PIP administration

- `GET /pip/api/sources`
- `PUT /pip/api/sources/{id}`
- `POST /pip/api/sources/{id}/test`
- `GET /pip/api/health`
- `GET /pip/api/health/cached`

### Audit and system visibility

- `GET /system/api/info`
- `GET /system/api/config`
- `GET /system/api/enforcement-points`
- `GET /system/api/audit`
- `GET /system/api/audit/{id}`

## Blazor admin surface

The embedded admin UI covers the most common migration-time tasks:

- SPIF management/import/export
- SPIF diffing and builder/editor workflows
- policy-set and policy-version management
- PIP source CRUD and connectivity tests
- system status and audit review
- decision testing / explain flows

If you are replacing an EmailClassification admin tool, this is the default operator surface to move to first.

## Data model translation notes

### Classifications

Map the legacy baseline classification ladder into:

- `PolicyOid`
- `ClassificationLacv`
- `ClassificationName`
- optional marking metadata such as phrase text, abbreviations, and colors

### Categories and dissemination controls

Legacy categories usually become SPIF tag sets and tags:

- category family → tag set
- allowed values / choices → tags or tag categories
- dissemination / release constraints → restrictive or permissive tag types
- UI-only grouping hints → semantic categories in the Blazor builder state

### Equivalent policies

If the EmailClassification baseline carried cross-policy mappings, move them into SPIF equivalent policy definitions and explicit classification/category mappings where needed.

## Testing checklist

Before cutting traffic over, validate at least the following:

- SPIF schema validation succeeds
- semantic validation produces only understood warnings
- imported SPIF can be exported and diffed cleanly
- migrated policies activate successfully
- AuthZEN evaluation returns expected permit/deny outcomes
- explain traces show the expected ACDF and policy steps
- PIP health checks pass for every external dependency
- audit queries show decision IDs and applied policy references

## Known boundaries

- Signed SPIF input still requires a real XML signature verifier; the default runtime rejects signed SPIFs rather than pretending to verify them.
- STANAG 4778 support remains intentionally minimal and focused on inline metadata-binding workflows.
- Migration is primarily semantic, not binary compatibility. Existing clients should be updated to target the ABAC Controller API surface explicitly.

## Related docs

- `README.md`
- `API.md`
- `USAGE.md`
- `docs/runtime-baseline.md`
- `docs/standards-gaps.md`
