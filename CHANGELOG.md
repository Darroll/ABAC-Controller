# Changelog

## Unreleased

### Changed

- changed audit queue saturation handling so audit writes apply explicit backpressure and fail on timeout instead of silently dropping the oldest buffered events
- aligned repository baseline documentation with the implemented .NET 10 runtime and current HTTP/JSON + embedded Blazor architecture
- added a lightweight dependency baseline / SBOM placeholder for gate-review traceability
