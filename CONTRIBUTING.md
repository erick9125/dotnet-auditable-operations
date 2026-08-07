# Contributing

## Development

```bash
dotnet restore
dotnet build
dotnet test
dotnet pack src/AuditableOperations/AuditableOperations.csproj -c Release
```

## Guidelines

- Keep 0.1.0 focused on the EF Core interceptor core.
- Do not add dashboards, brokers, SIEM, or HTTP body capture.
- Prefer tests that assert redaction happens before sink persistence.
- Document any change to transaction guarantees.

## Pull requests

1. Add or update tests.
2. Update `CHANGELOG.md`.
3. Keep public API changes intentional and documented.
