# Transaction guarantees (0.1.0)

## Chosen strategy

**Post-SaveChanges persistence** with an independent audit `DbContext`.

```
Application DbContext
        ↓
AuditSaveChangesInterceptor captures pending changes
        ↓
EF persists business entities
        ↓
On success: finalize IDs → IAuditSink
On failure: discard pending captures
```

## Guarantees

| Scenario | Audit written? |
|----------|----------------|
| `SaveChanges` succeeds | Yes |
| `SaveChanges` throws | No |
| Business transaction rolls back because `SaveChanges` failed | No |
| Explicit ambient transaction: `SaveChanges` succeeds, later `Rollback` | Possibly yes (orphan audit) |

## Why not same-transaction by default?

`DatabaseAuditSink` uses a separate `AuditDbContext` to:

1. Avoid interceptor recursion (`SaveChanges` → audit → `SaveChanges` → ...)
2. Keep audit storage decoupled from the application model

Sharing one transaction across two databases is not generally available. Same-database transactional sinks remain a future enhancement.

## Sink failure behavior

If `IAuditSink.WriteAsync` throws after a successful business `SaveChanges`, the exception propagates to the caller. Business data may already be committed while audit persistence failed. Log and monitor `audit.records.failed` once OpenTelemetry support lands in 0.3.0.
