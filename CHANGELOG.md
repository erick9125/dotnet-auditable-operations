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
- Enrich records with user, tenant, correlation ID, and source through `IAuditContextAccessor`
- Provide `HttpAuditContextAccessor` and null/custom accessors
- Support `[Audited]`, `[AuditRedact]`, and `[AuditIgnore]`
- Persist through `InMemoryAuditSink` and `DatabaseAuditSink`
- Document transaction and security guarantees
- Add unit tests and PostgreSQL Testcontainers integration tests
