# CONTRIBUTING

## Development workflow

1. Create a branch.
2. Make a focused change.
3. Add or update tests.
4. Run build and test locally.
5. Open a PR with a clear summary and risk notes.

## Build and test commands

```bash
~/.dotnet/dotnet build AbacController.slnx --nologo -v q
~/.dotnet/dotnet test AbacController.slnx --nologo -v q
```

Docker-backed integration tests use the real image build path and expect Docker access through:

```bash
sg docker -c "docker ..."
```

## Code standards

- keep public APIs documented with XML doc comments
- prefer dependency inversion over direct infrastructure access
- preserve append-only audit semantics
- keep caches explicit and invalidation behavior tested
- do not silently swallow data-loss conditions
- keep production auth explicit; do not broaden development auth behavior

## Testing expectations

When you change behavior:

- add or update unit tests for the changed class or public method
- add regression coverage for fixed defects
- run the full solution test suite before merging

## Documentation expectations

Update docs when you change:

- runtime configuration
- public API shape
- container behavior
- architecture or persistence assumptions

Relevant files:

- `README.md`
- `INSTALL.md`
- `USAGE.md`
- `ARCHITECTURE.md`
- `API.md`
- `docs/runtime-baseline.md`
- `docs/standards-gaps.md`

## Commit hygiene

Use small, reviewable commits with imperative messages, for example:

- `fix: correct audit query filtering`
- `test: add regression coverage for cache invalidation`
- `docs: document Docker runtime and AuthZEN flow`

## Pull request checklist

- [ ] build passes cleanly
- [ ] full test suite passes
- [ ] docs updated where needed
- [ ] security implications reviewed
- [ ] behavior changes called out explicitly
