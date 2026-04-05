# Changelog

## Unreleased

### Changed

- changed audit queue saturation handling so audit writes apply explicit backpressure and fail on timeout instead of silently dropping the oldest buffered events
- aligned repository baseline documentation with the implemented .NET 10 runtime and current HTTP/JSON + embedded Blazor architecture
- added a lightweight formal SPDX-style SBOM artifact plus refreshed dependency baseline documentation for gate-review traceability
- wired persisted PIP source definitions into the runtime resolver and health-check path so admin-created sources now participate in live attribute resolution and source testing
- added an operator-focused deployment guide with a pre-deployment checklist, go-live checks, and truthful Docker/Kubernetes examples
- drafted v1 release notes and linked the main docs to deployment and release-readiness references
- corrected the XACML JSON route documentation to match the implemented `POST /access/v1/xacml-json` endpoint
