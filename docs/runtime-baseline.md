# Runtime baseline

This document describes the implemented runtime baseline for the current repository state. It is intentionally narrower than the long-term architecture vision.

## Platform baseline

- **Framework:** .NET 10 (`net10.0`)
- **Application model:** ASP.NET Core web host with embedded Blazor Server components
- **Solution entry point:** `src/AbacController.Api`
- **Build/test baseline:** `~/.dotnet/dotnet build AbacController.slnx` and `~/.dotnet/dotnet test AbacController.slnx`

## Active runtime shape

The current branch runs as a single process / single container application with these in-proc components:

- API host
- PDP evaluation engine
- PAP policy/parsing services
- PIP resolution and cache services
- PEP-related label handling helpers
- EF Core data access layer
- buffered audit writer + background batch flusher
- embedded Blazor UI

## API baseline actually implemented

The implemented external API surface in this branch is HTTP/JSON-first:

- AuthZEN 1.0 evaluation endpoints in `AuthZenController`
- health endpoints (`/health/live`, `/health/ready`, `/health/startup`)
- metrics endpoint (`/metrics`)
- controller-based API wiring via `services.AddControllers()` and `app.MapControllers()`

## Not part of the implemented baseline

The following may appear in higher-level planning artifacts, but they are **not** implemented in this repository state and should not be described as present runtime behavior:

- gRPC services
- grpc-gateway / JSON transcoding generated from `.proto` contracts
- STANAG 4778 metadata binding / unbinding
- XML-DSig trust verification for SPIF payloads

## Storage baseline

- **Configured provider in host wiring:** SQLite via EF Core
- **Default connection string fallback:** `Data Source=abac-controller.db`
- **Initialization behavior:** database is ensured/initialized at startup

PostgreSQL remains an architecture option but is not the active runtime baseline in current host registration.

## Audit durability baseline

The audit subsystem uses a bounded `Channel<AuditEvent>` feeding a background batch writer.

Current behavior:

- bounded queue capacity: 10,000 events
- full queue behavior: explicit backpressure (`BoundedChannelFullMode.Wait`)
- enqueue timeout: 5 seconds
- saturation outcome: enqueue fails with a timeout instead of silently discarding the oldest audit event
- persistence model: periodic batch writes to the database

This is a deliberate durability choice: if the system cannot safely accept another audit record, the failure is surfaced instead of hidden.
