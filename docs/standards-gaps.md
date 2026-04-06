# Standards gaps and current boundaries

## STANAG 4774

- `XmlStanag4774Codec` implements label syntax encode/decode for the controller's internal `SecurityLabel` model.
- Decode preserves canonical identifiers when present in the XML payload via extension attributes such as policy OID, classification LACV, tag set OID, enum type, and category LACV.
- This is sufficient for round-trip fidelity in controller-generated labels and for standards-aware tests.

## STANAG 4778

`Stanag4778MetadataBinder` produces and consumes a **Phase 0 conformant BDO** (Binding Data Object) per ADatP-4778.2 Edition A Version 1 (December 2020).

### Phase 0 — BDO wire format (implemented)

The BDO root element and namespace are now structurally correct:

```
BindingInformation  (urn:nato:stanag:4778:bindinginformation:1:0)
  MetadataBindingContainer
    MetadataBinding  @xml:id
      Metadata       @xml:id
        [STANAG 4774 originatorConfidentialityLabel XML]
      DataReference  @URI="#do-{uuid}"  @xmime:contentType
  DataObject*        @xml:id  @encoding="base64"
    [base64-encoded payload]
```

`*` `DataObject` is a local extension element. ADatP-4778.2 has no defined element for inline binary payloads. Full spec conformance for binary data requires Phase 1 detached binding (external URI + digest).

- All identifiers use `xml:id` (standard XML ID form matching Chapter 12 SPIF examples)
- Caller-supplied `BindingId` values are validated as valid XML NCNames
- `xmime:contentType` on `DataReference` carries the payload media type when present
- `Unbind` rejects documents with the wrong root namespace, external `DataReference` URIs, missing `Metadata`, or missing `DataObject`
- API surface unchanged: `POST /pep/api/metadata/bind` and `POST /pep/api/metadata/unbind`

### Phase 1+ — not yet implemented

| Feature | Phase | Notes |
|---------|-------|-------|
| Detached binding (external URI + digest) | 1 | Binary data carried outside the BDO via `DataReference` with a real URI and integrity digest |
| SPIF embedding (`PolicyInformation`) | 1 | Embed or reference the governing SPIF inside the BDO |
| REST `Binding-Data` header | 1 | Detached BDO transport via HTTP header per ADatP-4778.2 §8 |
| XML-DSig envelope signing (`ds:Signature`) | 2 | Sign the `MetadataBinding` element using `mb:Id` reference targets |
| Multiple `MetadataBinding` per container | 3 | One BDO carrying labels for multiple data objects |
| `MetadataReference` (external label link) | 3 | Point to a label stored outside the BDO |
| Conformance test suite | 4 | Interoperability testing against reference implementations |

This is production-quality Phase 0 BDO support. It is not a claim of full ADatP-4778.2 interoperability coverage.

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
