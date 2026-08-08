# erick9125.AuditableOperations

[![CI](https://img.shields.io/badge/ci-GitHub%20Actions-blue)](.github/workflows/ci.yml)
[![NuGet](https://img.shields.io/badge/nuget-erick9125.AuditableOperations-blue)](https://www.nuget.org/packages/erick9125.AuditableOperations)
[![Target](https://img.shields.io/badge/.NET-9.0-512BD4)](#)
[![License](https://img.shields.io/badge/license-MIT-green)](LICENSE)

Auditoría estructurada para **ASP.NET Core** y **EF Core**.

Captura automáticamente inserts, updates y deletes de entidades, los enriquece con contexto de ejecución, redacta propiedades sensibles y persiste registros estructurados mediante un sink intercambiable — sin llenar servicios y controladores de código repetido.

> **English docs:** [README.md](README.md)

---

## Promesa (0.1.0)

> Capturar automáticamente cambios de entidades EF Core y persistir registros de auditoría enriquecidos con contexto de request, redactando propiedades sensibles de forma segura.

---

## El problema

En muchas aplicaciones hay que responder preguntas como:

- ¿Quién modificó este registro?
- ¿Qué campos cambiaron y cuál era el valor anterior?
- ¿Desde qué endpoint o job ocurrió?
- ¿Cuál era el correlation ID / tenant / usuario?

La implementación típica termina así:

```csharp
await auditService.LogAsync(userId, "UPDATE_ORDER", before, after);
```

Ese patrón se olvida con facilidad, es inconsistente entre equipos y es peligroso cuando se registran valores sensibles por accidente.

**AuditableOperations** mueve la auditoría a infraestructura. Tus servicios siguen haciendo `SaveChangesAsync()` — la librería hace el resto.

---

## Cómo funciona

```text
Request HTTP / job en background
        │
        ▼
┌───────────────────────┐
│ IAuditContextAccessor │  usuario · tenant · correlation · source
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
captura pendiente          finaliza IDs
(old/new, redacta)         escribe en IAuditSink
     │                         │
     │                    ┌────┴─────┐
     │                    ▼          ▼
     │            InMemorySink  DatabaseSink
     │                         (AuditDbContext)
     └─ si falla: descarta capturas pendientes
```

### Ciclo de vida

| Fase | Qué ocurre |
|------|------------|
| `SavingChanges` | Inspecciona el `ChangeTracker`, filtra entidades auditables, captura propiedades modificadas y redacta valores sensibles |
| EF persiste | El insert/update/delete de negocio corre con normalidad |
| `SavedChanges` | Resuelve claves primarias generadas, construye `AuditRecord`s y llama a `IAuditSink.WriteAsync` |
| `SaveChangesFailed` | Descarta capturas pendientes — no queda auditoría huérfana por un save fallido |

Los IDs generados por base de datos se completan **después** de persistir, para que los registros `Created` tengan el id real.

---

## Características

| Característica | Comportamiento |
|----------------|----------------|
| Inserts / updates / deletes | Se mapean a `Created`, `Updated`, `Deleted` |
| Solo propiedades modificadas | Usa `property.IsModified` + comparación old/new |
| Owned types / value objects | Se integran al registro del dueño como `Address.City` |
| Contexto de ejecución | Usuario, tenant opcional, correlation ID, source |
| Redacción | `[AuditRedact]` → `***` **antes** del sink |
| Exclusión | `[AuditIgnore]` en tipo o propiedad |
| Opt-in de entidades | `[Audited]` (configurable) |
| Sinks intercambiables | `InMemoryAuditSink`, `DatabaseAuditSink` o tu propio `IAuditSink` |
| Sin recursión del interceptor | El almacén de auditoría usa un `AuditDbContext` independiente |
| Workloads sin HTTP | `IAuditContextAccessor` personalizado para workers/jobs |

---

## Instalación

```bash
dotnet add package erick9125.AuditableOperations
```

**Requisitos:** .NET 9, EF Core 9, ASP.NET Core (para contexto HTTP).

---

## Inicio rápido

### 1. Marca las entidades

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

### 2. Registra los servicios

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
        .UseAuditableOperations(sp);
});
```

> Usá la sobrecarga `(sp, options)` de `AddDbContext`. El interceptor es scoped porque lee el contexto
> de auditoría por request, lo que descarta `AddDbContextPool` — el pooling construye las opciones una
> sola vez para toda la aplicación y fijaría un único accessor para todos los requests.
>
> Si no registrás ningún sink, los registros se descartan y se loguea una advertencia una sola vez.
> Registrá uno con `AddInMemoryAuditSink()`, `AddDatabaseAuditSink(...)` o tu propio `IAuditSink`.

### 3. Asegura el esquema de auditoría

```csharp
using (var scope = app.Services.CreateScope())
{
    var auditDb = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
    await auditDb.Database.EnsureCreatedAsync();
    // o aplica migraciones en producción
}
```

### 4. Sigue escribiendo código de aplicación normal

```csharp
order.Status = "Approved";
await db.SaveChangesAsync(); // el registro de auditoría se genera solo
```

Sin `auditService.LogAsync(...)` en controladores ni servicios de aplicación.

---

## Atributos

| Atributo | Destino | Efecto |
|----------|---------|--------|
| `[Audited]` | Clase | Incluye la entidad en la captura (si `RequireAuditedAttribute` es `true`) |
| `[AuditRedact]` | Propiedad | Sustituye previous/current por `***` y marca `IsRedacted = true` |
| `[AuditIgnore]` | Clase o propiedad | Omite el tipo o la propiedad por completo |

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

## Configuración

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

| Opción | Default | Descripción |
|--------|---------|-------------|
| `EnableEntityChanges` | `true` | Interruptor general de captura EF |
| `AuditAddedEntities` | `true` | Auditar inserts |
| `AuditModifiedEntities` | `true` | Auditar updates |
| `AuditDeletedEntities` | `true` | Auditar deletes |
| `CaptureUser` | `true` | Resolver usuario desde el accessor |
| `CaptureTenant` | `true` | Resolver tenant desde el accessor |
| `RedactedPlaceholder` | `"***"` | Valor escrito para propiedades `[AuditRedact]` |
| `RequireAuditedAttribute` | `true` | Solo auditar entidades con `[Audited]` |
| `IgnoreConcurrencyTokens` | `true` | Ignorar row versions / tokens de concurrencia |
| `IgnoreShadowProperties` | `true` | Ignorar shadow properties de EF |
| `SinkFailureBehavior` | `LogAndContinue` | Qué hacer si el sink falla tras el commit de negocio |
| `MaxOwnedTypeDepth` | `5` | Profundidad máxima al recorrer owned types (value objects) |

> **No** existe una opción para desactivar la redacción. `[AuditRedact]` es una decisión de seguridad
> por propiedad y ningún flag global debe poder anularla. Para dejar de auditar una propiedad
> sensible por completo, usa `[AuditIgnore]`.

---

## Contexto de ejecución

La auditoría debe funcionar en HTTP **y** en procesos en background. El contrato es:

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

### HTTP (por defecto en apps web)

```csharp
builder.Services.AddHttpAuditContext();
```

`HttpAuditContextAccessor` resuelve:

| Campo | Origen |
|-------|--------|
| `UserId` | claim `sub`, luego `NameIdentifier`, luego `Identity.Name` |
| `TenantId` | claim `tenant_id` o `tenant` |
| `CorrelationId` | `HttpContext.TraceIdentifier` |
| `Source` | `"PUT /api/orders/{id}"` (método + path) |

### Jobs / workers

```csharp
services.AddSingleton<IAuditContextAccessor>(
    new StaticAuditContextAccessor(new AuditContext
    {
        UserId = "worker-sync",
        CorrelationId = activityId,
        Source = "order-sync-job"
    }));
```

Implementa `IAuditContextAccessor` según cómo tu host provea identidad (AsyncLocal, payload del job, headers del mensaje, etc.).

---

## Sinks

### En memoria (tests)

```csharp
services.AddAuditableOperations();
services.AddInMemoryAuditSink();

// después
var sink = sp.GetRequiredService<InMemoryAuditSink>();
sink.Records.Should().ContainSingle(r => r.Action == "Updated");
```

### Base de datos (producción)

```csharp
services.AddDatabaseAuditSink(options =>
    options.UseNpgsql(configuration.GetConnectionString("Audit")));
```

Persiste en la tabla `audit_entries` mediante un `AuditDbContext` dedicado. Separar el almacén de auditoría del `DbContext` de aplicación evita la recursión del interceptor.

### Sink personalizado

```csharp
public sealed class SeqAuditSink : IAuditSink
{
    public Task WriteAsync(
        IReadOnlyCollection<AuditRecord> records,
        CancellationToken cancellationToken = default)
    {
        // reenvía a Seq, Elasticsearch, cola, etc.
        return Task.CompletedTask;
    }
}

services.AddSingleton<IAuditSink, SeqAuditSink>();
```

Ver [docs/custom-sinks.md](docs/custom-sinks.md).

---

## Ejemplo de registro de auditoría

Después de:

```csharp
order.Status = "Approved";
order.InternalNote = "cliente vip";
await db.SaveChangesAsync();
```

Obtienes algo así:

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

Notas:

- Las propiedades sin cambios se omiten.
- Las propiedades `[AuditIgnore]` nunca aparecen.
- Los valores redactados nunca llegan al sink en claro.

---

## Pruebas

```csharp
var services = new ServiceCollection();
services.AddLogging();
services.AddAuditableOperations();
services.AddInMemoryAuditSink();
services.AddDbContext<AppDbContext>((sp, options) =>
{
    options.UseSqlite(connection)
           .UseAuditableOperations(sp);
});

// validateScopes replica ASP.NET Core: un DbContext scoped debe resolverse desde un scope.
await using var provider = services.BuildServiceProvider(validateScopes: true);
using var scope = provider.CreateScope();
var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
var sink = provider.GetRequiredService<InMemoryAuditSink>();

db.Orders.Add(new Order { Status = "Pending", InternalNote = "secreto" });
await db.SaveChangesAsync();

var record = sink.Records.Single();
Assert.Equal("Created", record.Action);
Assert.DoesNotContain("secreto", JsonSerializer.Serialize(record));
```

Las pruebas de integración de este repo usan **Testcontainers + PostgreSQL**. Para escenarios de auditoría, conviene comportamiento relacional real frente a EF InMemory.

---

## Garantías transaccionales

0.1.0 usa persistencia **post-SaveChanges** con un almacén de auditoría independiente.

| Escenario | ¿Se escribe auditoría? |
|-----------|------------------------|
| `SaveChanges` tiene éxito | Sí |
| `SaveChanges` lanza excepción | No |
| Transacción explícita: `SaveChanges` ok y luego `Rollback` | Posiblemente sí (auditoría huérfana) |

Si el sink falla después de guardar el negocio, `SinkFailureBehavior` decide qué pasa. El default `LogAndContinue` registra el error y deja que la operación de negocio termine bien — propagar no deshace el commit y solo invita a un retry que duplica los datos. Usa `SinkFailureBehavior.Throw` para fallar de forma ruidosa. Detalle completo: [docs/transactions.md](docs/transactions.md).

---

## Seguridad

Este paquete puede tocar datos sensibles de la aplicación. Principios de diseño:

- Redactar **antes** de persistir en el sink
- Nunca capturar bodies HTTP completos
- Nunca serializar grafos de navegación de EF
- Nunca volcar claims / tokens / passwords completos por defecto
- El consumidor debe marcar propiedades sensibles con `[AuditRedact]` o `[AuditIgnore]`

Ver [SECURITY.md](SECURITY.md) y [docs/security.md](docs/security.md).

La redacción automática de nombres comunes (`password`, `token`, `apiKey`, …) está planificada para **0.2.0**.

---

## Lo que 0.1.0 **no** incluye

Fuera de alcance a propósito en la primera versión:

- Dashboard de auditoría / diff visual
- Integraciones Kafka, Elasticsearch, SIEM
- Event sourcing o reversión automática de cambios
- Auditoría completa de requests HTTP / captura del body
- Multi-tenant de producto más allá del `TenantId` opcional
- Retención / cifrado configurables complejos
- OpenTelemetry (preparado para 0.3.0)

Primero el núcleo, bien hecho.

---

## Aplicación de ejemplo

[`samples/AspNetCorePostgres`](samples/AspNetCorePostgres) — API mínima de órdenes:

| Método | Ruta | Efecto |
|--------|------|--------|
| `POST` | `/orders` | Crear → auditoría `Created` |
| `PUT` | `/orders/{id}` | Editar → auditoría `Updated` |
| `DELETE` | `/orders/{id}` | Eliminar → auditoría `Deleted` |
| `GET` | `/audit` | Consultar registros recientes |

---

## Roadmap

| Versión | Enfoque |
|---------|---------|
| **0.1.0** | Interceptor EF, auditoría CRUD, contexto HTTP, redacción, sinks, tests PostgreSQL, NuGet |
| **0.2.0** | Config fluent de ignore, detección automática de sensibles, SQL Server, eventos manuales |
| **0.3.0** | OpenTelemetry, sinks async/buffered, hooks de retención |
| **0.4.0** | Visor separado (`dotnet-audit-viewer`) |

---

## Documentación

| Documento | Tema |
|-----------|------|
| [docs/security.md](docs/security.md) | Manejo de datos sensibles |
| [docs/transactions.md](docs/transactions.md) | Garantías transaccionales |
| [docs/redaction.md](docs/redaction.md) | Comportamiento de redacción |
| [docs/custom-sinks.md](docs/custom-sinks.md) | Implementar `IAuditSink` |
| [docs/audit-store-schema.md](docs/audit-store-schema.md) | Tabla de auditoría, migraciones, retención |
| [docs/releasing.md](docs/releasing.md) | Cortar y publicar una release |
| [CHANGELOG.md](CHANGELOG.md) | Notas de versión |
| [CONTRIBUTING.md](CONTRIBUTING.md) | Flujo de desarrollo |

---

## Licencia

MIT © erick9125
