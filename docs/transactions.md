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

The sink runs *after* the business `SaveChanges` has committed, so a sink failure cannot be rolled
back. `SinkFailureBehavior` decides what happens next:

| Value | Behavior |
|-------|----------|
| `LogAndContinue` (default) | Log at error level and let the business operation succeed. The audit record is lost. |
| `Throw` | Log and rethrow. The caller sees a failure for an operation whose data is already committed. |

`LogAndContinue` is the default because rethrowing gains nothing: the business data is committed
either way, and surfacing an error typically triggers a caller retry that duplicates it.

Treat these error logs as alertable — they are the only signal that the trail has a gap. Monitor
`audit.records.failed` once OpenTelemetry support lands in 0.3.0.

```csharp
services.AddAuditableOperations(options =>
{
    options.SinkFailureBehavior = SinkFailureBehavior.Throw; // fail loudly instead
});
```
