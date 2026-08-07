# Initial GitHub issues (0.1.0)

- feat: capture added EF Core entities
- feat: capture modified properties with old and new values
- feat: capture deleted entities
- feat: add AuditRedact attribute
- feat: add AuditIgnore attribute
- feat: add HTTP audit context accessor
- feat: add custom audit context accessor support
- feat: implement in-memory audit sink
- feat: implement relational audit sink
- test: verify generated entity IDs are captured
- test: verify rollback behavior when SaveChanges fails
- test: verify concurrent request context isolation
- docs: document sensitive data handling
- docs: document transaction guarantees

## Follow-ups (0.2.0+)

- feat: fluent IgnoreEntity / IgnoreProperty configuration
- feat: automatic sensitive field name detection
- feat: SQL Server provider samples and tests
- feat: manual audit events API
- feat: OpenTelemetry activities and metrics
- feat: same-database transactional sink option
