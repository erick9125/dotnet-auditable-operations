# Contributing

## Development

```bash
dotnet restore
dotnet build
dotnet test tests/AuditableOperations.Tests.Unit/AuditableOperations.Tests.Unit.csproj
dotnet pack src/AuditableOperations/AuditableOperations.csproj -c Release
```

The integration tests start a PostgreSQL container through Testcontainers, so **Docker must be
running** for them:

```bash
dotnet test tests/AuditableOperations.Tests.Integration/AuditableOperations.Tests.Integration.csproj
```

A plain `dotnet test` runs both projects and will fail without Docker.

## Guidelines

- Keep 0.1.0 focused on the EF Core interceptor core.
- Do not add dashboards, brokers, SIEM, or HTTP body capture.
- Prefer tests that assert redaction happens before sink persistence.
- Document any change to transaction guarantees.
- The build runs with `TreatWarningsAsErrors`, and XML documentation is required on public members —
  an undocumented public API fails the build rather than shipping empty IntelliSense.
- Stay on FluentAssertions 6.x. Version 8 changed to a commercial license.

## Pull requests

1. Add or update tests.
2. Update `CHANGELOG.md`. The release workflow refuses to publish a version with no changelog entry.
3. Keep public API changes intentional and documented.

## Releasing

See [docs/releasing.md](docs/releasing.md).
