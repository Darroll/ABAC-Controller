# Changelog

## Unreleased

### Changed

- changed audit queue saturation handling so audit writes apply explicit backpressure and fail on timeout instead of silently dropping the oldest buffered events
- aligned repository baseline documentation with the implemented .NET 10 runtime and current HTTP/JSON + embedded Blazor architecture
- added a lightweight formal SPDX-style SBOM artifact plus refreshed dependency baseline documentation for gate-review traceability
- wired persisted PIP source definitions into the runtime resolver and health-check path so admin-created sources now participate in live attribute resolution and source testing
