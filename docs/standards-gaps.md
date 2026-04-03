# Standards gaps and current boundaries

## STANAG 4774

- `XmlStanag4774Codec` implements label syntax encode/decode for the controller's internal `SecurityLabel` model.
- Decode preserves canonical identifiers when present in the XML payload via extension attributes such as policy OID, classification LACV, tag set OID, enum type, and category LACV.
- This is sufficient for round-trip fidelity in controller-generated labels and for standards-aware tests.

## STANAG 4778

- The repository now includes a **minimal metadata binding path** via `Stanag4778MetadataBinder`.
- Supported shape:
  - bind an inline STANAG 4774 XML label plus opaque payload bytes/text into a simple STANAG 4778-style XML envelope
  - unbind the envelope and recover the embedded label and payload
  - invoke that path through `POST /api/v1/pep/metadata/bind` and `POST /api/v1/pep/metadata/unbind`
- Current limits:
  - payload is carried **inline** in the XML envelope as base64
  - no detached/reference-based binding model yet
  - no canonicalization/signing of the binding envelope itself yet
  - this is pragmatic controller support, not a claim of full STANAG 4778 interoperability coverage

## XML SPIF validation

- Parser validation now has three layers:
  1. **Schema-backed validation** for the core supported SPIF structure.
  2. **Semantic validation** for cross-reference, duplication, required-category, OID, and policy-shape checks that XSD alone does not cover well.
  3. **Pluggable XML-DSig verification hook** via `IXmlSignatureVerifier`.
- Default XML-DSig behavior is intentionally safe:
  - unsigned SPIFs continue through validation
  - signed or `keyIdentifier`-annotated SPIFs are rejected by the default `RejectingXmlSignatureVerifier`
  - deployments can replace that verifier with a real trust implementation later without changing parser flow
- Full cryptographic trust validation is therefore **not yet implemented**, but signature handling is no longer absent from the validation path.
