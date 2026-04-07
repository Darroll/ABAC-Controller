# Standards gaps and current boundaries

## STANAG 4774

- `XmlStanag4774Codec` implements label syntax encode/decode for the controller's internal `SecurityLabel` model.
- Decode preserves canonical identifiers when present in the XML payload via extension attributes such as policy OID, classification LACV, tag set OID, enum type, and category LACV.
- This is sufficient for round-trip fidelity in controller-generated labels and for standards-aware tests.

## STANAG 4778

`Stanag4778MetadataBinder` produces and consumes conformant STANAG 4778 BDOs per ADatP-4778.2 Edition A Version 1 (December 2020).

### Phase 0 — Inline BDO wire format (implemented)

Correct BDO root element and namespace with inline binary payload (local extension):

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

`*` `DataObject` is a local extension element (not in spec). Full spec conformance for binary data requires Phase 1+ detached binding.

API: `POST /pep/api/metadata/bind`, `POST /pep/api/metadata/unbind`

### Phase 1 — HTTP Binding Profile (implemented)

REST Binding Profile (ADatP-4778.2 Chapter 7). Null-URI detached BDO for the HTTP entity body:

```
BindingInformation
  MetadataBindingContainer
    MetadataBinding  @xml:id
      Metadata       @xml:id
        [STANAG 4774 originatorConfidentialityLabel XML]
      DataReference  @URI=""  @xmime:contentType="message/http"
```

`DataReference URI=""` semantically binds the label to the HTTP entity body. No `DataObject` — the data is carried by the transport. The BDO is placed in the `Binding-Data:` HTTP header:

```
Binding-Data: binding-type="urn:nato:stanag:4778:bindinginformation:1:0";
              binding-data-object="<base64 encoded BDO>"
```

`BindingDataHeaderCodec` handles the header encode/decode. Malformed header input returns HTTP 400.

API: `POST /pep/api/metadata/bind-http`, `POST /pep/api/metadata/unbind-http`

### Phase 2+ — not yet implemented

| Feature | Phase | Notes |
|---------|-------|-------|
| External URI + XMLDSIG Manifest/digest | 2 | Binary data referenced by URI (e.g., `./file.bin`) requires a `ds:Manifest` with `DigestValue`; SHA-384 is mandatory |
| XML-DSig envelope signing (`ds:Signature`) | 2 | Sign the `MetadataBinding` element; uses `mb:Id` reference targets per crypto annexes |
| BDO embedded in SPIF extensions | 3 | A BDO goes *inside* `spif:extensions` to label the SPIF document itself (PAP concern, not binder) |
| Multiple `MetadataBinding` per container | 3 | One BDO carrying labels for multiple data objects |
| `MetadataReference` (external label link) | 3 | Point to a label stored outside the BDO |
| Conformance test suite | 4 | Interoperability testing against reference implementations |

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
