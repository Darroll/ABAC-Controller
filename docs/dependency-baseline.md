# Dependency baseline

This file is the human-readable companion to the formal lightweight SBOM artifact at `docs/sbom.spdx.json`.

## What changed

The earlier placeholder-only dependency note has been replaced by:

- a structured SPDX-style JSON SBOM: `docs/sbom.spdx.json`
- this summary document for reviewer convenience

## Scope

The SBOM currently captures:

- direct NuGet package references declared in repo `.csproj` files
- internal project components present in the repo
- project-to-project dependency relationships
- target framework baseline (`net10.0`)

It does **not** yet attempt to fully enumerate transitive NuGet closure, OS/container packages, or cryptographic hashes for build outputs. That follow-up belongs in CI/release automation.

## Direct dependency summary

### Runtime-oriented direct NuGet packages

- `Google.Api.CommonProtos` `2.16.0`
- `Google.Protobuf` `3.32.0`
- `Grpc.AspNetCore` `2.76.0`
- `Grpc.Tools` `2.76.0` *(build-time / private asset)*
- `Microsoft.AspNetCore.Authentication.JwtBearer` `10.0.5`
- `Microsoft.AspNetCore.Grpc.JsonTranscoding` `10.0.0`
- `Microsoft.EntityFrameworkCore.Design` `10.0.5` *(design-time)*
- `Microsoft.EntityFrameworkCore.Sqlite` `10.0.5`
- `Microsoft.Extensions.Caching.Memory` `10.0.5`
- `Microsoft.Extensions.Hosting.Abstractions` `10.0.5`
- `Microsoft.Extensions.Http` `10.0.5`
- `System.DirectoryServices.Protocols` `10.0.0`

### Test-only direct NuGet packages

- `coverlet.collector` `6.0.4`
- `Microsoft.NET.Test.Sdk` `17.14.1`
- `xunit` `2.9.3`
- `xunit.runner.visualstudio` `3.1.4`

## Internal component baseline

Repo projects represented in the SBOM:

- `AbacController.Api`
- `AbacController.Audit`
- `AbacController.Blazor`
- `AbacController.Core`
- `AbacController.Data`
- `AbacController.Pap`
- `AbacController.Pdp`
- `AbacController.Pep`
- `AbacController.Pip`
- `AbacController.Tests.Unit`
- `AbacController.Tests.Integration`

## Review note

This artifact is intentionally truthful and lightweight:

- formal enough for gate traceability
- not presented as a complete supply-chain inventory
- suitable until CI-based SBOM generation is added
