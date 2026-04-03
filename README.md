# ABAC Controller

A standalone, vendor-agnostic Attribute-Based Access Control (ABAC) authoring and enforcement system. Single-container application providing the complete ABAC stack — PDP, PAP, PIP, and PEP — built on .NET 10 LTS.

## Overview

The ABAC Controller is the first production-ready implementation of the full ACDF (Access Control Decision Function) against XML Security Policy Information Files (SPIF). It provides standards-compliant policy evaluation, administration, attribute resolution, and enforcement in a single deployable container.

### Components

| Component | Purpose |
|-----------|---------|
| **PDP** (Policy Decision Point) | Real-time policy evaluation engine implementing the full ACDF with XACML 4-valued semantics (Permit/Deny/NotApplicable/Indeterminate) |
| **PAP** (Policy Administration Point) | Policy authoring, SPIF import/export, versioning, validation — Blazor UI + API |
| **PIP** (Policy Information Point) | Pluggable attribute retrieval from external sources (LDAP, OIDC, REST, DB) with caching and TTL management |
| **PEP** (Policy Enforcement Point) | Enforcement gateway with AuthZEN 1.0 interface, security label generation/parsing (STANAG 4774/4778) |

## Standards Compliance

- **NIST SP 800-162** — ABAC architecture and definitions
- **NIST SP 800-53** — Security controls (AC-3, AC-4, AC-16, AC-24, AU-2, AU-3, SC-16)
- **xmlspif.org v3.0** — XML Security Policy Information File (v2.1 fallback)
- **AuthZEN 1.0** — OpenID Foundation authorization evaluation API
- **STANAG 4774/4778** — NATO security labels and metadata binding

## Architecture

```
┌─────────────────────────────────────────────┐
│           ABAC Controller Container          │
│                                              │
│  ┌──────────┐  ┌──────────┐  ┌──────────┐  │
│  │   PAP    │  │   PDP    │  │   PEP    │  │
│  │ (Author) │  │ (Decide) │  │(Enforce) │  │
│  └────┬─────┘  └────┬─────┘  └────┬─────┘  │
│       │              │              │        │
│       └──────┐  ┌────┘              │        │
│              ▼  ▼                   │        │
│         ┌──────────┐               │        │
│         │   PIP    │◄──────────────┘        │
│         │ (Lookup) │                         │
│         └────┬─────┘                         │
│              ▼                               │
│       ┌────────────┐                         │
│       │  Data Layer │  SQLite / PostgreSQL   │
│       └────────────┘                         │
└─────────────────────────────────────────────┘
```

## Tech Stack

- **.NET 10 LTS** with ReadyToRun compilation
- **Blazor Server** — embedded PAP administration UI
- **gRPC** — primary protocol (high-performance authorization decisions)
- **REST** — via grpc-gateway transcoding
- **SQLite** — embedded data store (default)
- **PostgreSQL** — optional external data store
- **EF Core** — data access with migrations
- **OAuth 2.0** — API authentication with scoped access

## Project Structure

```
AbacController/
├── src/
│   ├── AbacController.Core/       # Domain models, interfaces, constants
│   ├── AbacController.Pdp/        # ACDF engine, decision cache, PDP service
│   ├── AbacController.Pap/        # SPIF parser, registry, policy management
│   ├── AbacController.Pep/        # Label codecs, marking generator, enforcement
│   ├── AbacController.Pip/        # Attribute connectors, caching, resolution
│   ├── AbacController.Data/       # EF Core DbContext, entities, repositories
│   ├── AbacController.Audit/      # Audit pipeline, batch writer
│   ├── AbacController.Api/        # gRPC services, REST, AuthZEN endpoints
│   └── AbacController.Blazor/     # Blazor Server PAP UI
├── tests/
│   ├── AbacController.Tests.Unit/
│   └── AbacController.Tests.Integration/
└── protos/                        # gRPC protocol buffer definitions
```

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- SQLite (bundled) or PostgreSQL 15+ (optional)

### Build

```bash
dotnet build
```

### Run

```bash
dotnet run --project src/AbacController.Api
```

### Test

```bash
dotnet test
```

## Key Features

- **Full ACDF Implementation** — Classification dominance, category membership, and constraint evaluation per xmlspif.org specification
- **Multi-SPIF Support** — Load multiple security policies; resolution via label-embedded policy OID with caller override
- **Pluggable Label Codecs** — XML/STANAG 4774 built-in, extensible for BER/DER and future formats
- **Default-Deny** — Every decision requires an explicit permit policy match (NIST requirement)
- **Complete Audit Trail** — Every authorization decision logged with full context (subject, resource, action, decision, attributes, policy applied)
- **Vendor-Agnostic** — Standards-based APIs, works with any system

## API

### Authorization Decision (AuthZEN 1.0)
```
POST /access/v1/evaluation
```

### Policy Management (PAP)
```
POST   /api/v1/policies/spif/import
GET    /api/v1/policies
GET    /api/v1/policies/{id}
DELETE /api/v1/policies/{id}
```

### gRPC Services
- `AbacController.Pdp.v1.PdpService` — Authorization evaluation
- `AbacController.Pap.v1.PapService` — Policy management
- `AbacController.Pip.v1.PipService` — Attribute management
- `AbacController.Pep.v1.PepService` — Label operations

## License

Proprietary — © archTIS Limited. All rights reserved.

## Status

🚧 **Under active development** — Not yet ready for production use.
