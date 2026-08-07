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
        var dbContext = scope.ServiceProvider.GetRequiredService<AuditDbContext>();

        foreach (var record in records)
        {
            dbContext.AuditEntries.Add(AuditEntryEntity.FromRecord(record));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
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
            entity.Property(x => x.Action).HasMaxLength(32).IsRequired();
            entity.Property(x => x.EntityType).HasMaxLength(256).IsRequired();
            entity.Property(x => x.EntityId).HasMaxLength(128).IsRequired();
            entity.Property(x => x.UserId).HasMaxLength(128);
            entity.Property(x => x.TenantId).HasMaxLength(128);
            entity.Property(x => x.CorrelationId).HasMaxLength(128);
            entity.Property(x => x.Source).HasMaxLength(512);
            entity.Property(x => x.ChangesJson).HasColumnType("text").IsRequired();
            entity.HasIndex(x => x.EntityType);
            entity.HasIndex(x => x.EntityId);
            entity.HasIndex(x => x.OccurredAt);
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
        return new AuditEntryEntity
        {
            Id = record.Id,
            Action = record.Action,
            EntityType = record.EntityType,
            EntityId = record.EntityId,
            UserId = record.UserId,
            TenantId = record.TenantId,
            CorrelationId = record.CorrelationId,
            Source = record.Source,
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
