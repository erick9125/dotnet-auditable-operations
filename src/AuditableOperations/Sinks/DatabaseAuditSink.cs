using System.Text.Json;
using AuditableOperations.Abstractions;
using AuditableOperations.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AuditableOperations.Sinks;

/// <summary>
/// Persists audit records into <see cref="AuditDbContext"/>.
/// </summary>
/// <remarks>
/// Each write runs in its own dependency injection scope with a dedicated <see cref="DbContext"/>.
/// That keeps the audit write out of the application context — which would otherwise re-enter the
/// interceptor — and out of its transaction, so audit persistence cannot be rolled back with the
/// business data. See <c>docs/transactions.md</c> for the resulting guarantees.
/// </remarks>
public sealed class DatabaseAuditSink : IAuditSink
{
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>Initializes a new instance of the <see cref="DatabaseAuditSink"/> class.</summary>
    /// <param name="scopeFactory">Factory used to create the scope owning the audit context.</param>
    public DatabaseAuditSink(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    /// <inheritdoc />
    public async Task WriteAsync(
        IReadOnlyCollection<AuditRecord> records,
        CancellationToken cancellationToken = default)
    {
        if (records.Count == 0)
        {
            return;
        }

        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = Stage(scope.ServiceProvider, records);

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Write(IReadOnlyCollection<AuditRecord> records)
    {
        if (records.Count == 0)
        {
            return;
        }

        // A real synchronous path: the default interface implementation would block a thread pool
        // thread on database I/O for every synchronous SaveChanges.
        using var scope = _scopeFactory.CreateScope();
        Stage(scope.ServiceProvider, records).SaveChanges();
    }

    private static AuditDbContext Stage(IServiceProvider services, IReadOnlyCollection<AuditRecord> records)
    {
        var dbContext = services.GetRequiredService<AuditDbContext>();

        foreach (var record in records)
        {
            dbContext.AuditEntries.Add(AuditEntryEntity.FromRecord(record));
        }

        return dbContext;
    }
}

/// <summary>
/// Storage context for the audit trail, deliberately separate from the application context.
/// </summary>
public sealed class AuditDbContext : DbContext
{
    /// <summary>Initializes a new instance of the <see cref="AuditDbContext"/> class.</summary>
    /// <param name="options">Options configured by <c>AddDatabaseAuditSink</c>.</param>
    public AuditDbContext(DbContextOptions<AuditDbContext> options)
        : base(options)
    {
    }

    /// <summary>The <c>audit_entries</c> table.</summary>
    public DbSet<AuditEntryEntity> AuditEntries => Set<AuditEntryEntity>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AuditEntryEntity>(entity =>
        {
            entity.ToTable("audit_entries");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Action).HasMaxLength(AuditFieldLimits.Action).IsRequired();
            entity.Property(x => x.EntityType).HasMaxLength(AuditFieldLimits.EntityType).IsRequired();
            entity.Property(x => x.EntityId).HasMaxLength(AuditFieldLimits.EntityId).IsRequired();
            entity.Property(x => x.UserId).HasMaxLength(AuditFieldLimits.UserId);
            entity.Property(x => x.TenantId).HasMaxLength(AuditFieldLimits.TenantId);
            entity.Property(x => x.CorrelationId).HasMaxLength(AuditFieldLimits.CorrelationId);
            entity.Property(x => x.Source).HasMaxLength(AuditFieldLimits.Source);
            entity.Property(x => x.ChangesJson).IsRequired();
            entity.HasIndex(x => x.OccurredAt);

            // Serves the natural query: the history of one entity, most recent first.
            entity.HasIndex(x => new { x.EntityType, x.EntityId, x.OccurredAt });
        });
    }
}

/// <summary>
/// Relational shape of an <see cref="AuditRecord"/>. Property changes are stored as a JSON document
/// so the schema does not have to change when a consumer's entities do.
/// </summary>
public sealed class AuditEntryEntity
{
    /// <summary>
    /// Serialization settings for <see cref="ChangesJson"/>. camelCase is pinned explicitly so the
    /// stored payload matches the documented shape and stays stable across releases.
    /// </summary>
    private static readonly JsonSerializerOptions ChangesJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    /// <inheritdoc cref="AuditRecord.Id" />
    public Guid Id { get; set; }

    /// <inheritdoc cref="AuditRecord.Action" />
    public string Action { get; set; } = string.Empty;

    /// <inheritdoc cref="AuditRecord.EntityType" />
    public string EntityType { get; set; } = string.Empty;

    /// <inheritdoc cref="AuditRecord.EntityId" />
    public string EntityId { get; set; } = string.Empty;

    /// <inheritdoc cref="AuditRecord.UserId" />
    public string? UserId { get; set; }

    /// <inheritdoc cref="AuditRecord.TenantId" />
    public string? TenantId { get; set; }

    /// <inheritdoc cref="AuditRecord.CorrelationId" />
    public string? CorrelationId { get; set; }

    /// <inheritdoc cref="AuditRecord.Source" />
    public string? Source { get; set; }

    /// <summary>
    /// <see cref="AuditRecord.Changes"/> serialized as a camelCase JSON array.
    /// </summary>
    public string ChangesJson { get; set; } = "[]";

    /// <inheritdoc cref="AuditRecord.OccurredAt" />
    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>Projects a record into its stored form, clamping fields to their column widths.</summary>
    /// <param name="record">The record to store.</param>
    /// <returns>The entity to insert.</returns>
    public static AuditEntryEntity FromRecord(AuditRecord record)
    {
        // Defensive: records built outside EntityChangeCollector (custom producers) are clamped here
        // so an oversized value cannot fail the write after the business data has committed.
        return new AuditEntryEntity
        {
            Id = record.Id,
            Action = AuditFieldLimits.Truncate(record.Action, AuditFieldLimits.Action)!,
            EntityType = AuditFieldLimits.Truncate(record.EntityType, AuditFieldLimits.EntityType)!,
            EntityId = AuditFieldLimits.Truncate(record.EntityId, AuditFieldLimits.EntityId)!,
            UserId = AuditFieldLimits.Truncate(record.UserId, AuditFieldLimits.UserId),
            TenantId = AuditFieldLimits.Truncate(record.TenantId, AuditFieldLimits.TenantId),
            CorrelationId = AuditFieldLimits.Truncate(record.CorrelationId, AuditFieldLimits.CorrelationId),
            Source = AuditFieldLimits.Truncate(record.Source, AuditFieldLimits.Source),
            ChangesJson = JsonSerializer.Serialize(record.Changes, ChangesJsonOptions),
            OccurredAt = record.OccurredAt
        };
    }

    /// <summary>
    /// Rebuilds an <see cref="AuditRecord"/> from its stored form.
    /// </summary>
    /// <remarks>
    /// <see cref="AuditPropertyChange.PreviousValue"/> and
    /// <see cref="AuditPropertyChange.CurrentValue"/> are declared as <see cref="object"/>, so on the
    /// way back they are <see cref="System.Text.Json.JsonElement"/> rather than the original CLR
    /// type. Compare them as text, or read them through <c>JsonElement</c>; do not expect a
    /// round-tripped record to equal the one that was written.
    /// </remarks>
    /// <returns>The record as stored, with change values as <c>JsonElement</c>.</returns>
    public AuditRecord ToRecord()
    {
        var changes = JsonSerializer.Deserialize<List<AuditPropertyChange>>(ChangesJson, ChangesJsonOptions)
            ?? [];

        return new AuditRecord
        {
            Id = Id,
            Action = Action,
            EntityType = EntityType,
            EntityId = EntityId,
            UserId = UserId,
            TenantId = TenantId,
            CorrelationId = CorrelationId,
            Source = Source,
            Changes = changes,
            OccurredAt = OccurredAt
        };
    }
}
