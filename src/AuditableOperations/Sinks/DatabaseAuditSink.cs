using System.Text.Json;
using AuditableOperations.Abstractions;
using AuditableOperations.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AuditableOperations.Sinks;

public sealed class DatabaseAuditSink : IAuditSink
{
    private readonly IServiceScopeFactory _scopeFactory;

    public DatabaseAuditSink(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

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

public sealed class AuditDbContext : DbContext
{
    public AuditDbContext(DbContextOptions<AuditDbContext> options)
        : base(options)
    {
    }

    public DbSet<AuditEntryEntity> AuditEntries => Set<AuditEntryEntity>();

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

public sealed class AuditEntryEntity
{
    public Guid Id { get; set; }

    public string Action { get; set; } = string.Empty;

    public string EntityType { get; set; } = string.Empty;

    public string EntityId { get; set; } = string.Empty;

    public string? UserId { get; set; }

    public string? TenantId { get; set; }

    public string? CorrelationId { get; set; }

    public string? Source { get; set; }

    public string ChangesJson { get; set; } = "[]";

    public DateTimeOffset OccurredAt { get; set; }

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
            ChangesJson = JsonSerializer.Serialize(record.Changes),
            OccurredAt = record.OccurredAt
        };
    }

    public AuditRecord ToRecord()
    {
        var changes = JsonSerializer.Deserialize<List<AuditPropertyChange>>(ChangesJson)
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
