using AuditableOperations.Models;
using AuditableOperations.Redaction;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.Options;

namespace AuditableOperations.EntityFramework;

/// <summary>
/// Turns EF Core change-tracker state into <see cref="AuditRecord"/> instances.
/// </summary>
public sealed class EntityChangeCollector
{
    private const string OwnedPathSeparator = ".";

    private readonly EntityMetadataResolver _metadataResolver;
    private readonly AuditRedactor _redactor;
    private readonly AuditableOperationsOptions _options;

    /// <summary>Initializes a new instance of the <see cref="EntityChangeCollector"/> class.</summary>
    /// <param name="metadataResolver">Decides what is auditable.</param>
    /// <param name="redactor">Builds change entries and applies redaction.</param>
    /// <param name="options">Audit configuration.</param>
    public EntityChangeCollector(
        EntityMetadataResolver metadataResolver,
        AuditRedactor redactor,
        IOptions<AuditableOperationsOptions> options)
    {
        _metadataResolver = metadataResolver;
        _redactor = redactor;
        _options = options.Value;
    }

    /// <summary>
    /// Snapshots auditable changes before they are persisted. Owned types are folded into the record
    /// of the entity that owns them rather than audited as entities of their own.
    /// </summary>
    internal IReadOnlyList<PendingAuditCapture> Capture(DbContext? context)
    {
        if (context is null || !_options.EnableEntityChanges)
        {
            return Array.Empty<PendingAuditCapture>();
        }

        var captures = new List<PendingAuditCapture>();

        foreach (var entry in context.ChangeTracker.Entries())
        {
            // Owned entries are reached through their owner, so that editing only a value object
            // still attributes the change to the aggregate the consumer opted in.
            if (entry.Metadata.IsOwned())
            {
                continue;
            }

            if (!_metadataResolver.ShouldAuditEntity(entry))
            {
                continue;
            }

            var capture = CaptureEntry(entry);
            if (capture is not null)
            {
                captures.Add(capture);
            }
        }

        return captures;
    }

    /// <summary>
    /// Completes captured changes once <c>SaveChanges</c> has assigned database-generated keys.
    /// </summary>
    internal IReadOnlyList<AuditRecord> BuildRecords(
        IReadOnlyList<PendingAuditCapture> captures,
        AuditContext auditContext,
        DateTimeOffset occurredAt)
    {
        var records = new List<AuditRecord>(captures.Count);

        foreach (var capture in captures)
        {
            records.Add(new AuditRecord
            {
                Id = Guid.CreateVersion7(),
                Action = capture.Action.ToString(),
                EntityType = Limit(capture.EntityType, AuditFieldLimits.EntityType),
                EntityId = Limit(ResolveEntityId(capture.Entry) ?? string.Empty, AuditFieldLimits.EntityId),
                UserId = AuditFieldLimits.Truncate(auditContext.UserId, AuditFieldLimits.UserId),
                TenantId = AuditFieldLimits.Truncate(auditContext.TenantId, AuditFieldLimits.TenantId),
                CorrelationId = AuditFieldLimits.Truncate(auditContext.CorrelationId, AuditFieldLimits.CorrelationId),
                Source = AuditFieldLimits.Truncate(auditContext.Source, AuditFieldLimits.Source),
                Changes = capture.Changes,
                OccurredAt = occurredAt
            });
        }

        return records;
    }

    private PendingAuditCapture? CaptureEntry(EntityEntry entry)
    {
        var action = ResolveAction(entry);
        if (action is null)
        {
            return null;
        }

        var changes = new List<AuditPropertyChange>();
        CollectChanges(entry, prefix: null, changes, depth: 0);

        // A create or delete is worth recording on its own; an update is not.
        if (action == AuditAction.Updated && changes.Count == 0)
        {
            return null;
        }

        return new PendingAuditCapture
        {
            Entry = entry,
            Action = action.Value,

            // Fully qualified: two entities with the same short name in different namespaces would
            // otherwise be indistinguishable in the trail.
            EntityType = entry.Metadata.ClrType.FullName ?? entry.Metadata.ClrType.Name,
            Changes = changes
        };
    }

    private AuditAction? ResolveAction(EntityEntry entry)
    {
        return entry.State switch
        {
            EntityState.Added when _options.AuditAddedEntities => AuditAction.Created,
            EntityState.Modified when _options.AuditModifiedEntities => AuditAction.Updated,
            EntityState.Deleted when _options.AuditDeletedEntities => AuditAction.Deleted,

            // EF Core does not mark the owner as modified when only a value object changed, so an
            // unchanged root is still a candidate — it is dropped later if nothing was captured.
            EntityState.Unchanged when _options.AuditModifiedEntities
                && _metadataResolver.HasOwnedNavigations(entry.Metadata) => AuditAction.Updated,

            _ => null
        };
    }

    private void CollectChanges(
        EntityEntry entry,
        string? prefix,
        List<AuditPropertyChange> changes,
        int depth)
    {
        AppendScalarChanges(entry, prefix, changes);

        if (depth >= _options.MaxOwnedTypeDepth
            || !_metadataResolver.HasOwnedNavigations(entry.Metadata))
        {
            return;
        }

        foreach (var navigation in entry.Navigations)
        {
            if (!navigation.Metadata.TargetEntityType.IsOwned()
                || !_metadataResolver.ShouldAuditOwnedNavigation(navigation.Metadata))
            {
                continue;
            }

            switch (navigation)
            {
                case ReferenceEntry reference when reference.TargetEntry is { } target:
                    CollectChanges(target, Qualify(prefix, navigation.Metadata.Name), changes, depth + 1);
                    break;

                case CollectionEntry collection when collection.CurrentValue is not null:
                    var index = 0;
                    foreach (var item in collection.CurrentValue)
                    {
                        var target = collection.FindEntry(item);
                        if (target is not null)
                        {
                            CollectChanges(
                                target,
                                Qualify(prefix, $"{navigation.Metadata.Name}[{index}]"),
                                changes,
                                depth + 1);
                        }

                        index++;
                    }

                    break;
            }
        }
    }

    private void AppendScalarChanges(EntityEntry entry, string? prefix, List<AuditPropertyChange> changes)
    {
        var capturesDelta = entry.State == EntityState.Modified;

        if (!capturesDelta
            && entry.State is not (EntityState.Added or EntityState.Deleted))
        {
            return;
        }

        var readsPrevious = entry.State != EntityState.Added;
        var readsCurrent = entry.State != EntityState.Deleted;

        foreach (var property in entry.Properties)
        {
            if (capturesDelta && !property.IsModified)
            {
                continue;
            }

            if (!_metadataResolver.ShouldAuditProperty(property))
            {
                continue;
            }

            var previousValue = readsPrevious ? property.OriginalValue : null;
            var currentValue = readsCurrent ? property.CurrentValue : null;

            if (capturesDelta && AreEquivalent(property, previousValue, currentValue))
            {
                continue;
            }

            changes.Add(_redactor.CreateChange(
                Qualify(prefix, property.Metadata.Name),
                _metadataResolver.ShouldRedactProperty(property),
                previousValue,
                currentValue));
        }
    }

    /// <summary>
    /// Compares two values with the property's own EF Core comparer rather than
    /// <see cref="object.Equals(object, object)"/>.
    /// </summary>
    /// <remarks>
    /// EF already filters most redundant edits through <c>IsModified</c>, so this rarely changes the
    /// outcome. It matters for properties carrying a value converter or custom comparer, where the
    /// CLR notion of equality and the model's notion disagree — using the model's keeps this check
    /// consistent with the <c>IsModified</c> flag it complements.
    /// </remarks>
    private static bool AreEquivalent(PropertyEntry property, object? previous, object? current)
    {
        if (previous is null || current is null)
        {
            return previous is null && current is null;
        }

        var comparer = property.Metadata.GetValueComparer();
        return comparer is not null
            ? comparer.Equals(previous, current)
            : Equals(previous, current);
    }

    private static string Qualify(string? prefix, string name)
    {
        return prefix is null ? name : string.Concat(prefix, OwnedPathSeparator, name);
    }

    private static string Limit(string value, int maxLength)
    {
        return AuditFieldLimits.Truncate(value, maxLength)!;
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
            return ReadKeyValue(entry, key.Properties[0].Name);
        }

        return string.Join("|", key.Properties.Select(property => ReadKeyValue(entry, property.Name) ?? string.Empty));
    }

    private static string? ReadKeyValue(EntityEntry entry, string propertyName)
    {
        var property = entry.Property(propertyName);
        return (property.CurrentValue ?? property.OriginalValue)?.ToString();
    }
}
