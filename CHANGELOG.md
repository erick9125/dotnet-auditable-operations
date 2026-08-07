# Changelog

## 0.1.0

- Capture EF Core Added, Modified, and Deleted entities via `AuditSaveChangesInterceptor`
- Finalize database-generated entity IDs after `SaveChanges`
- Enrich records with user, tenant, correlation ID, and source through `IAuditContextAccessor`
- Provide `HttpAuditContextAccessor` and null/custom accessors
- Support `[Audited]`, `[AuditRedact]`, and `[AuditIgnore]`
- Persist through `InMemoryAuditSink` and `DatabaseAuditSink`
- Document transaction and security guarantees
- Add unit tests and PostgreSQL Testcontainers integration tests
