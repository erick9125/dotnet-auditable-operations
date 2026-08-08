# erick9125.AuditableOperations

[![CI](https://img.shields.io/badge/ci-GitHub%20Actions-blue)](.github/workflows/ci.yml)
[![NuGet](https://img.shields.io/badge/nuget-erick9125.AuditableOperations-blue)](https://www.nuget.org/packages/erick9125.AuditableOperations)
[![Target](https://img.shields.io/badge/.NET-9.0-512BD4)](#)
[![License](https://img.shields.io/badge/license-MIT-green)](LICENSE)

Structured audit trails for **ASP.NET Core** and **EF Core**.

Automatically capture entity inserts, updates, and deletes, enrich them with request context, redact sensitive properties, and persist structured records through a pluggable sink — without sprinkling audit calls across your domain services.

> **Spanish docs:** [README.es.md](README.es.md)

---

## Promise (0.1.0)

> Automatically capture EF Core entity changes and persist structured audit records enriched with request context while safely redacting sensitive properties.

---

## The problem

Most applications eventually need answers like:

- Who changed this record?
- Which fields changed, and from what to what?
- Which endpoint or job triggered it?
- What was the correlation ID / tenant / user?

The usual approach leaks into every service:

```csharp
await auditService.LogAsync(userId, "UPDATE_ORDER", before, after);
```

That pattern is easy to forget, inconsistent across teams, and risky when sensitive values are logged by accident.

**AuditableOperations** moves auditing into infrastructure. Your services keep doing `SaveChangesAsync()` — the library does the rest.

---

## How it works

```text
HTTP request / background job
        │
        ▼
┌───────────────────────┐
│ IAuditContextAccessor │  user · tenant · correlation · source
└───────────┬───────────┘
            │
            ▼
┌─────────────────────────────────────┐
│ Application DbContext.SaveChanges() │
└─────────────────┬───────────────────┘
                  │
        AuditSaveChangesInterceptor
                  │
     ┌────────────┴────────────┐
     ▼                         ▼
SavingChanges              SavedChanges
capture pending            finalize IDs
(old/new, redact)          write to IAuditSink
     │                         │
     │                    ┌────┴─────┐
     │                    ▼          ▼
     │            InMemorySink  DatabaseSink
     │                         (AuditDbContext)
     └─ on failure: discard pending captures
```

### Lifecycle detail

| Phase | What happens |
|-------|----------------|
| `SavingChanges` | Inspect `ChangeTracker`, keep only audited entities, capture modified properties, redact sensitive values |
| EF persists | Business insert/update/delete runs normally |
| `SavedChanges` | Resolve generated primary keys, build `AuditRecord`s, call `IAuditSink.WriteAsync` |
| `SaveChangesFailed` | Discard pending captures — no orphan audit from a failed save |

Database-generated IDs are finalized **after** persistence, so `Created` records get the real entity id.

---

## Features

| Feature | Behavior |
|---------|----------|
| Inserts / updates / deletes | Mapped to `Created`, `Updated`, `Deleted` |
| Modified properties only | Uses `property.IsModified` + old/new comparison |
| Owned types / value objects | Folded into the owner's record as `Address.City` |
| Request context | User, optional tenant, correlation ID, source |
| Redaction | `[AuditRedact]` → `***` **before** the sink |
| Exclusion | `[AuditIgnore]` on type or property |
| Opt-in entities | `[Audited]` (configurable) |
| Pluggable sinks | `InMemoryAuditSink`, `DatabaseAuditSink`, or your own `IAuditSink` |
| No interceptor recursion | Audit store uses a separate `AuditDbContext` |
| Non-HTTP workloads | Custom `IAuditContextAccessor` for workers/jobs |

---

## Install

```bash
dotnet add package erick9125.AuditableOperations
```

**Requirements:** .NET 9, EF Core 9, ASP.NET Core (for HTTP context).

---

## Quick start

### 1. Mark entities

```csharp
using AuditableOperations.Attributes;

[Audited]
public class WorkOrder
{
    public Guid Id { get; set; }

    public string Status { get; set; } = string.Empty;

    [AuditRedact]
    public string InternalComment { get; set; } = string.Empty;

    [AuditIgnore]
    public DateTime CacheUpdatedAt { get; set; }
}
```

### 2. Register services

```csharp
using AuditableOperations.DependencyInjection;
using AuditableOperations.EntityFramework;
using AuditableOperations.Sinks;
using Microsoft.EntityFrameworkCore;

builder.Services.AddAuditableOperations(options =>
{
    options.EnableEntityChanges = true;
    options.CaptureUser = true;
    options.CaptureTenant = true;
});

builder.Services.AddHttpAuditContext();

builder.Services.AddDatabaseAuditSink(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Audit")));

builder.Services.AddDbContext<AppDbContext>((sp, options) =>
{
    options
        .UseNpgsql(builder.Configuration.GetConnectionString("App"))
        .AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>());
});
```

### 3. Ensure the audit schema exists

```csharp
using (var scope = app.Services.CreateScope())
{
    var auditDb = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
    await auditDb.Database.EnsureCreatedAsync();
    // or apply migrations in production
}
```

### 4. Keep writing normal application code

```csharp
order.Status = "Approved";
await db.SaveChangesAsync(); // audit record is produced automatically
```

No `auditService.LogAsync(...)` in controllers or application services.

---

## Attributes

| Attribute | Target | Effect |
|-----------|--------|--------|
| `[Audited]` | Class | Opt the entity into change capture (when `RequireAuditedAttribute` is `true`) |
| `[AuditRedact]` | Property | Replace previous/current values with `***`; set `IsRedacted = true` |
| `[AuditIgnore]` | Class or property | Skip the type or property entirely |

```csharp
[Audited]
public class Order
{
    public Guid Id { get; set; }
    public string Status { get; set; } = string.Empty;

    [AuditRedact]
    public string InternalNote { get; set; } = string.Empty;

    [AuditIgnore]
    public DateTime CacheUpdatedAt { get; set; }
}

[AuditIgnore]
public class CacheEntry
{
    public int Id { get; set; }
    public string Key { get; set; } = string.Empty;
}
```

---

## Configuration

```csharp
builder.Services.AddAuditableOperations(options =>
{
    options.EnableEntityChanges = true;
    options.AuditAddedEntities = true;
    options.AuditModifiedEntities = true;
    options.AuditDeletedEntities = true;

    options.CaptureUser = true;
    options.CaptureTenant = true;

    options.RedactedPlaceholder = "***";

    options.RequireAuditedAttribute = true;
    options.IgnoreConcurrencyTokens = true;
    options.IgnoreShadowProperties = true;

    options.SinkFailureBehavior = SinkFailureBehavior.LogAndContinue;
    options.MaxOwnedTypeDepth = 5;
});
```

| Option | Default | Description |
|--------|---------|-------------|
| `EnableEntityChanges` | `true` | Master switch for EF change capture |
| `AuditAddedEntities` | `true` | Capture inserts |
| `AuditModifiedEntities` | `true` | Capture updates |
| `AuditDeletedEntities` | `true` | Capture deletes |
| `CaptureUser` | `true` | Resolve user from context accessor |
| `CaptureTenant` | `true` | Resolve tenant from context accessor |
| `RedactedPlaceholder` | `"***"` | Replacement written for `[AuditRedact]` properties |
| `RequireAuditedAttribute` | `true` | Only audit `[Audited]` entities |
| `IgnoreConcurrencyTokens` | `true` | Skip row versions / concurrency tokens |
| `IgnoreShadowProperties` | `true` | Skip EF shadow properties |
| `SinkFailureBehavior` | `LogAndContinue` | What to do when a sink write fails after the business commit |
| `MaxOwnedTypeDepth` | `5` | How deep to follow owned-type (value object) graphs |

> There is deliberately **no** option to disable redaction. `[AuditRedact]` is a per-property security
> decision, and a global flag must not be able to override it. To stop auditing a sensitive property
> altogether, use `[AuditIgnore]`.

---

## Execution context

Auditing must work for HTTP **and** background work. The contract is:

```csharp
public interface IAuditContextAccessor
{
    AuditContext GetCurrent();
}
```

```csharp
public sealed record AuditContext
{
    public string? UserId { get; init; }
    public string? TenantId { get; init; }
    public string? CorrelationId { get; init; }
    public string? Source { get; init; }
}
```

### HTTP (default for web apps)

```csharp
builder.Services.AddHttpAuditContext();
```

`HttpAuditContextAccessor` resolves:

| Field | Source |
|-------|--------|
| `UserId` | claim `sub`, then `NameIdentifier`, then `Identity.Name` |
| `TenantId` | claim `tenant_id` or `tenant` |
| `CorrelationId` | `HttpContext.TraceIdentifier` |
| `Source` | `"PUT /api/orders/{id}"` (method + path) |

### Background jobs / workers

```csharp
services.AddSingleton<IAuditContextAccessor>(
    new StaticAuditContextAccessor(new AuditContext
    {
        UserId = "worker-sync",
        CorrelationId = activityId,
        Source = "order-sync-job"
    }));
```

Implement `IAuditContextAccessor` however your host provides identity (AsyncLocal, job payload, message headers, etc.).

---

## Sinks

### In-memory (tests)

```csharp
services.AddAuditableOperations();
services.AddInMemoryAuditSink();

// later
var sink = sp.GetRequiredService<InMemoryAuditSink>();
sink.Records.Should().ContainSingle(r => r.Action == "Updated");
```

### Database (production)

```csharp
services.AddDatabaseAuditSink(options =>
    options.UseNpgsql(configuration.GetConnectionString("Audit")));
```

Persists to table `audit_entries` through a dedicated `AuditDbContext`. Keeping audit storage separate from the application `DbContext` prevents interceptor recursion.

### Custom sink

```csharp
public sealed class SeqAuditSink : IAuditSink
{
    public Task WriteAsync(
        IReadOnlyCollection<AuditRecord> records,
        CancellationToken cancellationToken = default)
    {
        // forward to Seq, Elasticsearch, queue, etc.
        return Task.CompletedTask;
    }
}

services.AddSingleton<IAuditSink, SeqAuditSink>();
```

See [docs/custom-sinks.md](docs/custom-sinks.md).

---

## Example audit record

After:

```csharp
order.Status = "Approved";
order.InternalNote = "vip customer";
await db.SaveChangesAsync();
```

You get something like:

```json
{
  "id": "0196a1c2-3d4e-7f80-9abc-def012345678",
  "action": "Updated",
  "entityType": "Order",
  "entityId": "5c91f2a1-8b3d-4e2f-9c1a-7d6e5f4a3b2c",
  "userId": "user-42",
  "tenantId": "company-7",
  "correlationId": "0HN3K2EXAMPLE",
  "source": "PUT /orders/5c91f2a1-8b3d-4e2f-9c1a-7d6e5f4a3b2c",
  "occurredAt": "2026-08-07T20:10:23+00:00",
  "changes": [
    {
      "property": "Status",
      "previousValue": "Pending",
      "currentValue": "Approved",
      "isRedacted": false
    },
    {
      "property": "InternalNote",
      "previousValue": "***",
      "currentValue": "***",
      "isRedacted": true
    }
  ]
}
```

Notes:

- Unchanged properties are omitted.
- `[AuditIgnore]` properties never appear.
- Redacted values never reach the sink in clear text.

---

## Testing

```csharp
var services = new ServiceCollection();
services.AddLogging();
services.AddAuditableOperations();
services.AddInMemoryAuditSink();
services.AddDbContext<AppDbContext>((sp, options) =>
{
    options.UseSqlite(connection)
           .AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>());
});

var sp = services.BuildServiceProvider();
var db = sp.GetRequiredService<AppDbContext>();
var sink = sp.GetRequiredService<InMemoryAuditSink>();

db.Orders.Add(new Order { Status = "Pending", InternalNote = "secret" });
await db.SaveChangesAsync();

var record = sink.Records.Single();
Assert.Equal("Created", record.Action);
Assert.DoesNotContain("secret", JsonSerializer.Serialize(record));
```

Integration tests in this repo use **Testcontainers + PostgreSQL** — prefer real relational behavior over EF InMemory for audit scenarios.

---

## Transaction guarantees

0.1.0 uses **post-SaveChanges** persistence with an independent audit store.

| Scenario | Audit written? |
|----------|----------------|
| `SaveChanges` succeeds | Yes |
| `SaveChanges` throws | No |
| Explicit ambient transaction: successful `SaveChanges`, later `Rollback` | Possibly yes (orphan audit) |

If the sink fails after business data was saved, `SinkFailureBehavior` decides what happens. The default `LogAndContinue` logs at error level and lets the business operation succeed — rethrowing cannot undo the commit and only invites a duplicating retry. Set `SinkFailureBehavior.Throw` to fail loudly instead. Full details: [docs/transactions.md](docs/transactions.md).

---

## Security

This package may touch sensitive application data. Design principles:

- Redact **before** sink persistence
- Never capture full HTTP bodies
- Never serialize EF navigation graphs
- Never dump full claim sets / tokens / passwords by default
- Consumers must mark sensitive properties with `[AuditRedact]` or `[AuditIgnore]`

See [SECURITY.md](SECURITY.md) and [docs/security.md](docs/security.md).

Automatic redaction of common names (`password`, `token`, `apiKey`, …) is planned for **0.2.0**.

---

## What 0.1.0 does **not** include

Intentionally out of scope for the first release:

- Audit dashboard / visual diff UI
- Kafka, Elasticsearch, SIEM integrations
- Event sourcing or automatic change reversal
- Full HTTP request auditing / body capture
- Multi-tenant product features beyond optional `TenantId`
- Complex retention / encryption configuration
- OpenTelemetry (prepared for 0.3.0)

Build the core well first.

---

## Sample application

[`samples/AspNetCorePostgres`](samples/AspNetCorePostgres) — minimal orders API:

| Method | Route | Effect |
|--------|-------|--------|
| `POST` | `/orders` | Create → `Created` audit |
| `PUT` | `/orders/{id}` | Update → `Updated` audit |
| `DELETE` | `/orders/{id}` | Delete → `Deleted` audit |
| `GET` | `/audit` | Inspect recent audit records |

---

## Roadmap

| Version | Focus |
|---------|--------|
| **0.1.0** | EF interceptor, CRUD audit, HTTP context, redaction, sinks, PostgreSQL tests, NuGet |
| **0.2.0** | Fluent ignore config, auto-sensitive detection, SQL Server, manual audit events |
| **0.3.0** | OpenTelemetry, async/buffered sinks, retention hooks |
| **0.4.0** | Separate viewer (`dotnet-audit-viewer`) |

---

## Documentation

| Doc | Topic |
|-----|--------|
| [docs/security.md](docs/security.md) | Sensitive data handling |
| [docs/transactions.md](docs/transactions.md) | Transaction guarantees |
| [docs/redaction.md](docs/redaction.md) | Redaction behavior |
| [docs/custom-sinks.md](docs/custom-sinks.md) | Implementing `IAuditSink` |
| [CHANGELOG.md](CHANGELOG.md) | Release notes |
| [CONTRIBUTING.md](CONTRIBUTING.md) | Development workflow |

---

## License

MIT © erick9125
