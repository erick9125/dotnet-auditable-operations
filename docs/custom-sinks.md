# Custom sinks

Implement `IAuditSink`:

```csharp
public interface IAuditSink
{
    Task WriteAsync(
        IReadOnlyCollection<AuditRecord> records,
        CancellationToken cancellationToken = default);

    // Default implementation blocks on WriteAsync.
    void Write(IReadOnlyCollection<AuditRecord> records);
}
```

`Write` is used when the application calls the synchronous `DbContext.SaveChanges()`. Only
`WriteAsync` is required — but if your sink performs I/O, override `Write` with a genuinely
synchronous path. The default implementation blocks a thread pool thread, which is how thread pool
starvation starts under load. `InMemoryAuditSink` and `DatabaseAuditSink` both override it.

## Field limits

Records are truncated to `AuditFieldLimits` before they reach a sink (`EntityType` 256, `EntityId`
128, `UserId` / `TenantId` / `CorrelationId` 128, `Source` 512). Honor those widths in your own
schema, or widen it — values are never rejected, only shortened, because the business transaction
has already committed by the time the sink runs.

Register your implementation after `AddAuditableOperations`:

```csharp
services.AddAuditableOperations();
services.AddSingleton<IAuditSink, MyAuditSink>();
```

Built-in sinks:

- `NullAuditSink` — registered by default; discards records and warns once so a forgotten sink
  registration is visible instead of silent
- `InMemoryAuditSink` — tests and local inspection. Unbounded: it keeps every record for the life of
  the process, so do not use it in production
- `DatabaseAuditSink` — relational persistence via `AuditDbContext`

Important: sinks must never call `SaveChanges` on an application `DbContext` that uses `AuditSaveChangesInterceptor` unless recursion is explicitly suppressed.
