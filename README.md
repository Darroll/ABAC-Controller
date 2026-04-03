# ABAC Controller

A standalone, vendor-agnostic Attribute-Based Access Control (ABAC) authoring and enforcement system. The current baseline is a single-container .NET 10 application that hosts the core ABAC stack — PDP, PAP, PIP, PEP-oriented APIs, persistence, audit processing, gRPC services, JSON-transcoded REST endpoints, and an embedded Blazor administration UI.

## Overview

This repository contains the current implementation baseline for the ABAC Controller. It currently includes:

- PDP policy evaluation and decision caching
- PAP SPIF ingestion, parsing, and policy persistence
- pluggable PIP attribute resolution with cache/TTL behavior
- gRPC service contracts backed by `.proto` files and ASP.NET Core gRPC service mappings
- REST exposure for protobuf-defined APIs via JSON transcoding
- AuthZEN-style REST evaluation and discovery endpoints
- STANAG 4774 XML label encode/decode support
- a minimal STANAG 4778-style metadata binding/unbinding path
- durable audit capture with bounded buffering and explicit backpressure

Some architecture targets remain partial rather than fully standards-complete. See `docs/standards-gaps.md` and `docs/runtime-baseline.md` for the current truth.

## Components

| Component | Purpose |
|-----------|---------|
| **PDP** (Policy Decision Point) | Evaluates authorization requests using ACDF-style policy logic, policy evaluation, and decision caching |
| **PAP** (Policy Administration Point) | Imports, validates, stores, and manages SPIF/policy data |
| **PIP** (Policy Information Point) | Resolves external attributes and applies cache/TTL behavior |
| **PEP / API** (Policy Enforcement Point surface) | Exposes AuthZEN endpoints, gRPC/REST API surface, label handling, and metadata-binding helpers |
| **Audit** | Buffers and persists authorization audit events with batch database writes |
| **Blazor UI** | Embedded administration surface for runtime/policy workflows |

## Standards posture

Implemented or partially implemented:

- **NIST SP 800-162** — ABAC concepts and architecture alignment
- **NIST SP 800-53** — control-oriented implementation focus for access control and auditing
- **xmlspif.org v3.0** — primary SPIF parsing target with semantic validation
- **AuthZEN 1.0** — evaluation and discovery REST endpoints
- **gRPC + protobuf + JSON transcoding** — primary service contracts with REST exposure from proto annotations
- **STANAG 4774** — XML label encode/decode for the internal `SecurityLabel` model
- **STANAG 4778-style metadata binding** — minimal inline-envelope bind/unbind workflow for embedded labels + payloads

Known gaps / limits:

- **STANAG 4778** support is intentionally minimal, inline-envelope focused, and not a full interoperability claim
- **XML-DSig verification** is wired as a pluggable validation hook, but the default implementation is safe reject-by-default for signed SPIFs rather than full trust-chain validation
- broader BA/architecture scope remains larger than the currently delivered feature surface in some PAP/PIP/PEP/System areas

## Runtime baseline

- **Target framework:** .NET 10 (`net10.0` across src and tests)
- **Hosting model:** ASP.NET Core + embedded Blazor Server UI
- **Primary transport baseline:** gRPC with REST/JSON transcoding from protobuf contracts
- **Additional REST surface:** AuthZEN endpoints and controller-based metadata helpers
- **Persistence:** SQLite via EF Core in the active runtime baseline
- **Audit ingestion:** bounded in-memory channel with explicit backpressure when saturated

## Project structure

```text
AbacController/
├── protos/
│   └── abaccontroller/v1/          # Proto contracts for gRPC + transcoded REST
├── src/
│   ├── AbacController.Core/        # Domain models, interfaces, constants
│   ├── AbacController.Pdp/         # ACDF engine, decision cache, PDP service logic
│   ├── AbacController.Pap/         # SPIF parser, registry, policy management
│   ├── AbacController.Pep/         # Label codecs, marking generator, metadata binding
│   ├── AbacController.Pip/         # Attribute connectors, caching, resolution
│   ├── AbacController.Data/        # EF Core DbContext, entities, repositories
│   ├── AbacController.Audit/       # Audit pipeline and batch writer
│   ├── AbacController.Api/         # gRPC services, REST controllers, runtime wiring
│   └── AbacController.Blazor/      # Embedded administration UI
├── tests/
│   ├── AbacController.Tests.Unit/
│   └── AbacController.Tests.Integration/
└── docs/
    ├── runtime-baseline.md
    ├── standards-gaps.md
    ├── dependency-baseline.md
    └── sbom.spdx.json
```

## Getting started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

### Build

```bash
~/.dotnet/dotnet build AbacController.slnx
```

### Run

```bash
~/.dotnet/dotnet run --project src/AbacController.Api
```

### Test

```bash
~/.dotnet/dotnet test AbacController.slnx
```

## Current API surface

### gRPC services

- `abaccontroller.v1.PDPService`
- `abaccontroller.v1.PAPService`
- `abaccontroller.v1.PIPService`
- `abaccontroller.v1.PEPService`
- `abaccontroller.v1.SystemService`

### JSON-transcoded REST endpoints

Representative protobuf-backed REST paths include:

```text
POST /api/v1/pdp/evaluate
POST /api/v1/pdp/evaluate/batch
POST /api/v1/pdp/evaluate/explain
```

Additional component endpoints are defined in `protos/abaccontroller/v1/*.proto`.

### AuthZEN 1.0 and metadata helpers

```text
POST /access/v1/evaluation
POST /access/v1/evaluations
GET  /.well-known/authzen-configuration
POST /api/v1/pep/metadata/bind
POST /api/v1/pep/metadata/unbind
GET  /api/v1/pep/metadata/codecs
```

### Health and observability

```text
GET /health/live
GET /health/ready
GET /health/startup
GET /metrics
```

## Dependency and compliance artifacts

- Lightweight formal SBOM artifact: `docs/sbom.spdx.json`
- Dependency summary: `docs/dependency-baseline.md`
- Runtime truth source: `docs/runtime-baseline.md`
- Standards boundaries: `docs/standards-gaps.md`

## Development notes

- Audit writes no longer discard the oldest buffered events when the queue is full. The writer now waits briefly and then fails explicitly if the system cannot accept more audit records.
- The repository now includes proto contracts, gRPC service mappings, and JSON transcoding in the active host baseline.
- Standards claims should stay narrow and truthful: the metadata-binding path is real, but intentionally minimal.

## License

Proprietary — © archTIS Limited. All rights reserved.

## Status

🚧 Under active development.
