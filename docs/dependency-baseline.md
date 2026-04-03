# Dependency baseline / SBOM placeholder

This repository does not yet ship a formal SPDX or CycloneDX SBOM artifact. Until automated SBOM generation is added to CI, this file serves as a lightweight dependency baseline for gate review and manual change tracking.

## Direct NuGet dependencies in current repo state

### Runtime projects

- `Microsoft.AspNetCore.Authentication.JwtBearer` `10.0.5`
- `Microsoft.EntityFrameworkCore.Design` `10.0.5` *(design-time/private asset)*
- `Microsoft.EntityFrameworkCore.Sqlite` `10.0.5`
- `Microsoft.Extensions.Caching.Memory` `10.0.5`
- `Microsoft.Extensions.Hosting.Abstractions` `10.0.5`
- `Microsoft.Extensions.Http` `10.0.5`
- `System.DirectoryServices.Protocols` `10.0.0`

### Test projects

- `coverlet.collector` `6.0.4`
- `Microsoft.NET.Test.Sdk` `17.14.1`
- `xunit` `2.9.3`
- `xunit.runner.visualstudio` `3.1.4`

## Project-to-project dependency baseline

- `AbacController.Api` references Core, Pdp, Pap, Pip, Pep, Data, Audit, and Blazor
- `AbacController.Audit` references Core and Data
- `AbacController.Data` references Core
- `AbacController.Pap`, `AbacController.Pdp`, `AbacController.Pep`, and `AbacController.Pip` reference Core
- Unit tests currently reference Audit, Core, Pdp, Pap, and Pep
- Integration tests reference Core, Pdp, Pap, and Pep

## Follow-up

Recommended next step: add CI generation of a real SBOM artifact (for example SPDX or CycloneDX) and attach it to release/build outputs.
