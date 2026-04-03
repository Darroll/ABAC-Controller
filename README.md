# ABAC Controller

A standalone, vendor-agnostic Attribute-Based Access Control (ABAC) authoring and enforcement system. The current baseline is a single-container .NET 10 application that hosts the core ABAC stack — PDP, PAP, PIP, PEP, persistence, and audit processing — behind HTTP APIs plus an embedded Blazor administration UI.

## Overview

This repository contains the current implementation baseline for the ABAC Controller. It focuses on:

- policy evaluation through the PDP
- policy administration and SPIF ingestion
- pluggable attribute resolution through the PIP
- AuthZEN-style REST evaluation endpoints for the PEP/API surface
- durable audit capture with bounded buffering and explicit backpressure

Some architecture targets remain future work. In particular, the repository does **not** yet implement the planned gRPC service surface or STANAG 4778 metadata binding. See `docs/standards-gaps.md` and `docs/runtime-baseline.md` for the current truth.

## Components

| Component | Purpose |
|-----------|---------|
| **PDP** (Policy Decision Point) | Evaluates authorization requests using ACDF-style policy logic and decision caching |
| **PAP** (Policy Administration Point) | Imports, validates, stores, and manages SPIF/policy data |
| **PIP** (Policy Information Point) | Resolves external attributes and applies cache/TTL behavior |
| **PEP / API** (Policy Enforcement Point surface) | Exposes AuthZEN 1.0 evaluation endpoints and label handling helpers |
| **Audit** | Buffers and persists authorization audit events with batch database writes |

## Standards posture

Implemented or partially implemented:

- **NIST SP 800-162** — ABAC concepts and architecture alignment
- **NIST SP 800-53** — control-oriented implementation focus for access control and auditing
- **xmlspif.org v3.0** — primary SPIF parsing target with semantic validation
- **AuthZEN 1.0** — evaluation and discovery REST endpoints
- **STANAG 4774** — XML label encode/decode for the internal `SecurityLabel` model

Known gaps / limits:

- **STANAG 4778** metadata binding is not yet implemented
- **XML-DSig verification** for SPIF trust is not yet implemented
- **gRPC / transcoding API surface** is not yet present in this codebase

## Runtime baseline

- **Target framework:** .NET 10 (`net10.0` across src and tests)
- **Hosting model:** ASP.NET Core + embedded Blazor Server UI
- **Primary external API today:** HTTP/JSON controllers, including AuthZEN endpoints
- **Persistence:** SQLite by default, PostgreSQL planned/optional by architecture but not wired as the active runtime baseline in this branch
- **Audit ingestion:** bounded in-memory channel with explicit backpressure when saturated

## Project structure

```text
AbacController/
├── src/
│   ├── AbacController.Core/       # Domain models, interfaces, constants
│   ├── AbacController.Pdp/        # ACDF engine, decision cache, PDP service logic
│   ├── AbacController.Pap/        # SPIF parser, registry, policy management
│   ├── AbacController.Pep/        # Label codecs, marking generator, enforcement helpers
│   ├── AbacController.Pip/        # Attribute connectors, caching, resolution
│   ├── AbacController.Data/       # EF Core DbContext, entities, repositories
│   ├── AbacController.Audit/      # Audit pipeline and batch writer
│   ├── AbacController.Api/        # HTTP API, AuthZEN endpoints, runtime wiring
│   └── AbacController.Blazor/     # Embedded PAP administration UI
├── tests/
│   ├── AbacController.Tests.Unit/
│   └── AbacController.Tests.Integration/
└── docs/
    ├── runtime-baseline.md
    ├── standards-gaps.md
    └── dependency-baseline.md
```

## Getting started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

### Build

```bash
dotnet build AbacController.slnx
```

### Run

```bash
dotnet run --project src/AbacController.Api
```

### Test

```bash
dotnet test AbacController.slnx
```

## Current API surface

### AuthZEN 1.0 evaluation

```text
POST /access/v1/evaluation
POST /access/v1/evaluations
GET  /.well-known/authzen-configuration
```

### Health and observability

```text
GET /health/live
GET /health/ready
GET /health/startup
GET /metrics
```

## Development notes

- Audit writes no longer discard the oldest buffered events when the queue is full. The writer now blocks briefly and then fails explicitly if the system cannot accept more audit records.
- The repository documents aspirational architecture separately from implemented baseline behavior so runtime claims stay accurate.

## License

Proprietary — © archTIS Limited. All rights reserved.

## Status

🚧 Under active development.
