# Changelog

## 0.1.0

- Capture EF Core Added, Modified, and Deleted entities via `AuditSaveChangesInterceptor`
- Capture owned types (value objects) on the owning entity's record under qualified names such as
  `Address.City`; changing only a value object still produces an `Updated` record
- Honor `[AuditRedact]` unconditionally — no configuration flag can disable redaction
- Truncate record fields to the widths in `AuditFieldLimits`, so a caller-controlled value such as a
  long request path cannot fail the audit write after the business transaction committed
- `SinkFailureBehavior` (default `LogAndContinue`) controls whether a post-commit sink failure breaks
  the business operation
- Finalize database-generated entity IDs after `SaveChanges`
- Depend on the `Microsoft.AspNetCore.Http` package instead of the ASP.NET Core shared framework, so
  workers and console hosts can consume the library
- Report `EntityType` as the full CLR type name, so same-named types in different namespaces stay
  distinguishable
- `IAuditSink.Write` gives synchronous `SaveChanges` a real sync path instead of blocking on the
  async one; `InMemoryAuditSink` and `DatabaseAuditSink` implement it
- Register `NullAuditSink` by default, warning once instead of failing with an opaque DI error when
  no sink was registered
- Add `UseAuditableOperations(sp)` for `DbContextOptionsBuilder`
- Index `audit_entries` on `(EntityType, EntityId, OccurredAt)` and let the provider choose the JSON
  column type, so the schema is portable beyond PostgreSQL
- Enrich records with user, tenant, correlation ID, and source through `IAuditContextAccessor`
- Provide `HttpAuditContextAccessor` and null/custom accessors
- Support `[Audited]`, `[AuditRedact]`, and `[AuditIgnore]`
- Persist through `InMemoryAuditSink` and `DatabaseAuditSink`
- Document transaction and security guarantees
- Document the whole public API with XML comments, shipped in the package
- Validate options at startup (`RedactedPlaceholder`, `MaxOwnedTypeDepth`, `SinkFailureBehavior`)
- Cap binary values and collection nesting in `DefaultValueFormatter`, so a blob column cannot flood
  the trail on every change
- Store `ChangesJson` as camelCase, matching the documented payload shape
- Add unit tests and PostgreSQL Testcontainers integration tests
