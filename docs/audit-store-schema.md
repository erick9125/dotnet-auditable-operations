# Audit store schema

`AuditDbContext` maps a single table, `audit_entries`:

```sql
CREATE TABLE "audit_entries" (
    "Id"            uuid          NOT NULL PRIMARY KEY,
    "Action"        varchar(32)   NOT NULL,
    "EntityType"    varchar(256)  NOT NULL,
    "EntityId"      varchar(128)  NOT NULL,
    "UserId"        varchar(128)  NULL,
    "TenantId"      varchar(128)  NULL,
    "CorrelationId" varchar(128)  NULL,
    "Source"        varchar(512)  NULL,
    "ChangesJson"   text          NOT NULL,
    "OccurredAt"    timestamptz   NOT NULL
);

CREATE INDEX "IX_audit_entries_EntityType_EntityId_OccurredAt"
    ON "audit_entries" ("EntityType", "EntityId", "OccurredAt");

CREATE INDEX "IX_audit_entries_OccurredAt"
    ON "audit_entries" ("OccurredAt");
```

Column types are whatever the provider chooses for the mapped CLR types and lengths; the example
above shows PostgreSQL. Widths come from `AuditFieldLimits`, and records are truncated to them before
a sink ever sees them.

> **Known inconsistency:** the table is `snake_case` while columns keep their PascalCase property
> names, because only the table name is configured explicitly. This is cosmetic but becomes permanent
> once the package is published — decide before 0.1.0 whether to normalize it.

## Creating the schema

The library ships no EF migrations on purpose: migrations are provider-specific, and this package
supports any relational provider. Pick one of these instead.

### `EnsureCreated` — prototypes and tests

```csharp
var auditDb = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
await auditDb.Database.EnsureCreatedAsync();
```

Creates the schema if the database is empty and does nothing otherwise. It has no upgrade path, so it
is not appropriate for production.

### Your own migrations — production

Add a migrations project that references this package and your provider, then generate migrations
against `AuditDbContext`:

```bash
dotnet ef migrations add InitialAuditSchema \
    --context AuditDbContext \
    --project src/YourApp.AuditMigrations \
    --startup-project src/YourApp
```

The migrations live in your repository, version with your application, and upgrade through
`dotnet ef database update` or `Database.MigrateAsync()`.

### Hand-written DDL

Run the script above, adjusted for your provider. This suits teams whose schema changes go through a
DBA or a separate migration tool.

## Retention

The library does not delete anything. Audit tables grow without bound, so plan for partitioning by
`OccurredAt`, a scheduled purge, or archival. Retention hooks are planned for 0.3.0.
