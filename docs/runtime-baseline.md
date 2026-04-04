# Runtime baseline

This document describes the implemented runtime baseline for the current repository state. It is intentionally more concrete than the long-term architecture vision and should be treated as the source of truth for what the repo actually exposes today.

## Platform baseline

- **Framework:** .NET 10 (`net10.0`)
- **Application model:** ASP.NET Core web host with embedded Blazor Server components
- **Solution entry point:** `src/AbacController.Api`
- **Build/test baseline:** `~/.dotnet/dotnet build AbacController.slnx` and `~/.dotnet/dotnet test AbacController.slnx`

## Active runtime shape

The current branch runs as a single process / single container-style application with these in-proc components:

- API host
- gRPC service layer
- JSON-transcoded REST layer generated from protobuf contracts
- AuthZEN REST controller layer
- PDP evaluation engine
- PAP policy/parsing services
- PIP resolution and cache services
- PEP-related label handling helpers
- STANAG 4778-style metadata binding helper
- EF Core data access layer
- buffered audit writer + background batch flusher
- embedded Blazor UI

## API baseline actually implemented

The implemented external API surface in this branch is mixed gRPC + HTTP/JSON:

### gRPC host wiring

- service registration via `services.AddGrpc().AddJsonTranscoding()`
- mapped services in `Program.cs`:
  - `PdpGrpcService`
  - `PapGrpcService`
  - `PipGrpcService`
  - `PepGrpcService`
  - `SystemGrpcService`

### Proto / transcoding baseline

- protobuf contracts live in `protos/abaccontroller/v1/`
- current packages include:
  - `common.proto`
  - `pdp.proto`
  - `pap.proto`
  - `pip.proto`
  - `pep.proto`
  - `system.proto`
- REST endpoints are exposed from proto annotations through ASP.NET Core JSON transcoding

### Controller-based HTTP endpoints

Additional controller-based endpoints remain part of the runtime baseline:

- AuthZEN 1.0 evaluation endpoints in `AuthZenController`
- metadata helper endpoints in `PepMetadataController`
  - `POST /pep/api/metadata/bind`
  - `POST /pep/api/metadata/unbind`
  - `GET /pep/api/metadata/codecs`
- health endpoints (`/health/live`, `/health/ready`, `/health/startup`)
- metrics endpoint (`/metrics`)

## Storage baseline

- **Configured provider in host wiring:** SQLite via EF Core
- **Default connection string fallback:** `Data Source=abac-controller.db`
- **Initialization behavior:** database is ensured/initialized at startup

PostgreSQL remains an architecture option but is not the active runtime baseline in current host registration.

## Standards-related runtime behavior

### STANAG 4774

- XML label codec is present and registered through `ILabelCodecRegistry`
- label encode/decode is part of the active runtime baseline

### STANAG 4778

A minimal metadata-binding path is implemented:

- binding helper: `Stanag4778MetadataBinder`
- API exposure: `PepMetadataController`
- supported model: inline XML envelope with embedded STANAG 4774 XML label and inline base64 payload

This should be described as **minimal STANAG 4778-style support**, not as full standards-complete interoperability.

### XML-DSig path

- SPIF parsing includes a pluggable `IXmlSignatureVerifier`
- the default verifier is fail-safe and rejects signed/key-identified SPIFs unless a concrete verifier is supplied
- full trust-chain validation is **not** the baseline behavior today

## Not part of the implemented baseline

The following should **not** be overstated as completed runtime behavior in this repo state:

- full STANAG 4778 detached/reference-based binding workflows
- signature generation/canonicalization for 4778 envelopes
- full cryptographic trust validation for signed SPIFs out of the box
- full BA/architecture breadth across every PAP/PIP/PEP/System management capability

## Audit durability baseline

The audit subsystem uses a bounded `Channel<AuditEvent>` feeding a background batch writer.

Current behavior:

- bounded queue capacity: 10,000 events
- full queue behavior: explicit backpressure (`BoundedChannelFullMode.Wait`)
- enqueue timeout: 5 seconds
- saturation outcome: enqueue fails with a timeout instead of silently discarding the oldest audit event
- persistence model: periodic batch writes to the database

This is a deliberate durability choice: if the system cannot safely accept another audit record, the failure is surfaced instead of hidden.
