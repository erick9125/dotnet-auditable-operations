using AuditableOperations.Models;
using AuditableOperations.Redaction;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.Options;

namespace AuditableOperations.EntityFramework;

internal sealed class PendingAuditCapture
{
    public required EntityEntry Entry { get; init; }

    public required AuditAction Action { get; init; }

    public required string EntityType { get; init; }

    public required List<AuditPropertyChange> Changes { get; init; }

    public string? TemporaryEntityId { get; init; }
}

public sealed class EntityChangeCollector
{
    private readonly EntityMetadataResolver _metadataResolver;
    private readonly AuditRedactor _redactor;
    private readonly AuditableOperationsOptions _options;

    public EntityChangeCollector(
        EntityMetadataResolver metadataResolver,
        AuditRedactor redactor,
        IOptions<AuditableOperationsOptions> options)
    {
        _metadataResolver = metadataResolver;
        _redactor = redactor;
        _options = options.Value;
    }

    internal IReadOnlyList<PendingAuditCapture> Capture(DbContext? context)
    {
        if (context is null || !_options.EnableEntityChanges)
        {
            return Array.Empty<PendingAuditCapture>();
        }

        var captures = new List<PendingAuditCapture>();

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (!_metadataResolver.ShouldAuditEntity(entry))
            {
                continue;
            }

            switch (entry.State)
            {
                case EntityState.Added when _options.AuditAddedEntities:
                    captures.Add(CaptureCreated(entry));
                    break;
                case EntityState.Modified when _options.AuditModifiedEntities:
                    var modified = CaptureModified(entry);
                    if (modified is not null)
                    {
                        captures.Add(modified);
                    }
                    break;
                case EntityState.Deleted when _options.AuditDeletedEntities:
                    captures.Add(CaptureDeleted(entry));
                    break;
            }
        }

        return captures;
    }

    internal IReadOnlyList<AuditRecord> Finalize(
        IReadOnlyList<PendingAuditCapture> captures,
        AuditContext auditContext,
        DateTimeOffset occurredAt)
    {
        var records = new List<AuditRecord>(captures.Count);

        foreach (var capture in captures)
        {
            var entityId = ResolveEntityId(capture.Entry) ?? capture.TemporaryEntityId ?? string.Empty;

            records.Add(new AuditRecord
            {
                Id = Guid.CreateVersion7(),
                Action = capture.Action.ToString(),
                EntityType = capture.EntityType,
                EntityId = entityId,
                UserId = auditContext.UserId,
                TenantId = auditContext.TenantId,
                CorrelationId = auditContext.CorrelationId,
                Source = auditContext.Source,
                Changes = capture.Changes,
                OccurredAt = occurredAt
            });
        }

        return records;
    }

    private PendingAuditCapture CaptureCreated(EntityEntry entry)
    {
        var changes = new List<AuditPropertyChange>();

        foreach (var property in entry.Properties)
        {
            if (!_metadataResolver.ShouldAuditProperty(property))
            {
                continue;
            }

            if (property.Metadata.IsPrimaryKey())
            {
                continue;
            }

            changes.Add(_redactor.CreateChange(
                _metadataResolver.GetClrProperty(property.Metadata),
                property.Metadata.Name,
                previousValue: null,
                currentValue: property.CurrentValue));
        }

        return new PendingAuditCapture
        {
            Entry = entry,
            Action = AuditAction.Created,
            EntityType = entry.Metadata.ClrType.Name,
            Changes = changes,
            TemporaryEntityId = ResolveEntityId(entry)
        };
    }

    private PendingAuditCapture? CaptureModified(EntityEntry entry)
    {
        var changes = new List<AuditPropertyChange>();

        foreach (var property in entry.Properties)
        {
            if (!property.IsModified)
            {
                continue;
            }

            if (!_metadataResolver.ShouldAuditProperty(property))
            {
                continue;
            }

            if (Equals(property.OriginalValue, property.CurrentValue))
            {
                continue;
            }

            changes.Add(_redactor.CreateChange(
                _metadataResolver.GetClrProperty(property.Metadata),
                property.Metadata.Name,
                property.OriginalValue,
                property.CurrentValue));
        }

        if (changes.Count == 0)
        {
            return null;
        }

        return new PendingAuditCapture
        {
            Entry = entry,
            Action = AuditAction.Updated,
            EntityType = entry.Metadata.ClrType.Name,
            Changes = changes,
            TemporaryEntityId = ResolveEntityId(entry)
        };
    }

    private PendingAuditCapture CaptureDeleted(EntityEntry entry)
    {
        var changes = new List<AuditPropertyChange>();

        foreach (var property in entry.Properties)
        {
            if (!_metadataResolver.ShouldAuditProperty(property))
            {
                continue;
            }

            changes.Add(_redactor.CreateChange(
                _metadataResolver.GetClrProperty(property.Metadata),
                property.Metadata.Name,
                previousValue: property.OriginalValue,
                currentValue: null));
        }

        return new PendingAuditCapture
        {
            Entry = entry,
            Action = AuditAction.Deleted,
            EntityType = entry.Metadata.ClrType.Name,
            Changes = changes,
            TemporaryEntityId = ResolveEntityId(entry)
        };
    }

    private static string? ResolveEntityId(EntityEntry entry)
    {
        var key = entry.Metadata.FindPrimaryKey();
        if (key is null || key.Properties.Count == 0)
        {
            return null;
        }

        if (key.Properties.Count == 1)
        {
            var value = entry.Property(key.Properties[0].Name).CurrentValue
                ?? entry.Property(key.Properties[0].Name).OriginalValue;
            return value?.ToString();
        }

        var parts = key.Properties
            .Select(property =>
            {
                var value = entry.Property(property.Name).CurrentValue
                    ?? entry.Property(property.Name).OriginalValue;
                return value?.ToString() ?? string.Empty;
            });

        return string.Join("|", parts);
    }
}
