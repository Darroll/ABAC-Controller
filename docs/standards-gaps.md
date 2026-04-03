# Standards gaps and current boundaries

## STANAG 4774

- `XmlStanag4774Codec` implements label syntax encode/decode for the controller's internal `SecurityLabel` model.
- Decode now preserves canonical identifiers when present in the XML payload via extension attributes such as policy OID, classification LACV, tag set OID, enum type, and category LACV.
- This is enough for round-trip fidelity in controller-generated labels and for standards-aware tests.

## STANAG 4778

- **Not implemented in this task.**
- The repository currently exposes the 4778 namespace constant only (`SpifNamespaces.Stanag4778`) and does **not** provide a metadata binding/unbinding implementation.
- That means the controller can handle STANAG 4774 label syntax, but it does **not** yet bind that metadata to external payloads/documents per STANAG 4778.
- Any production claim of full 4774/4778 support would therefore be inaccurate until a binding layer is added.

## XML SPIF validation

- Parser validation now has two layers:
  1. **Schema-backed validation** for the core supported SPIF structure.
  2. **Semantic validation** for cross-reference, duplication, required-category, OID, and policy-shape checks that XSD alone does not cover well.
- XML-DSig verification is still **not implemented**. A `keyIdentifier` can be parsed, but trust of a SPIF is not cryptographically established yet.
