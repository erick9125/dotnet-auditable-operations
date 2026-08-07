# Custom sinks

Implement `IAuditSink`:

```csharp
public interface IAuditSink
{
    Task WriteAsync(
        IReadOnlyCollection<AuditRecord> records,
        CancellationToken cancellationToken = default);
}
```

Register your implementation after `AddAuditableOperations`:

```csharp
services.AddAuditableOperations();
services.AddSingleton<IAuditSink, MyAuditSink>();
```

Built-in sinks:

- `InMemoryAuditSink` — tests and local inspection
- `DatabaseAuditSink` — relational persistence via `AuditDbContext`

Important: sinks must never call `SaveChanges` on an application `DbContext` that uses `AuditSaveChangesInterceptor` unless recursion is explicitly suppressed.
