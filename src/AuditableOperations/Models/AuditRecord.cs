namespace AuditableOperations.Models;

/// <summary>
/// A single auditable operation on one entity, ready to be handed to an
/// <see cref="Abstractions.IAuditSink"/>.
/// </summary>
/// <remarks>
/// String fields are truncated to <see cref="AuditFieldLimits"/> before the record reaches a sink.
/// </remarks>
public sealed record AuditRecord
{
    /// <summary>Record identifier. A version 7 GUID, so ordering by id follows creation time.</summary>
    public Guid Id { get; init; }

    /// <summary>The <see cref="AuditAction"/> name: <c>Created</c>, <c>Updated</c> or <c>Deleted</c>.</summary>
    public required string Action { get; init; }

    /// <summary>Full CLR type name of the audited entity.</summary>
    public required string EntityType { get; init; }

    /// <summary>
    /// Primary key of the audited entity as text. Composite keys are joined with <c>"|"</c>.
    /// Resolved after <c>SaveChanges</c>, so database-generated keys hold their real value.
    /// </summary>
    public required string EntityId { get; init; }

    /// <summary>Acting user, from <see cref="AuditContext.UserId"/>.</summary>
    public string? UserId { get; init; }

    /// <summary>Tenant, from <see cref="AuditContext.TenantId"/>.</summary>
    public string? TenantId { get; init; }

    /// <summary>Correlation identifier, from <see cref="AuditContext.CorrelationId"/>.</summary>
    public string? CorrelationId { get; init; }

    /// <summary>Origin of the operation, from <see cref="AuditContext.Source"/>.</summary>
    public string? Source { get; init; }

    /// <summary>
    /// Properties that changed. Empty is legitimate for a create or delete whose properties are all
    /// ignored.
    /// </summary>
    public IReadOnlyList<AuditPropertyChange> Changes { get; init; }
        = Array.Empty<AuditPropertyChange>();

    /// <summary>
    /// When the change was persisted. Shared by every record produced by the same
    /// <c>SaveChanges</c> call.
    /// </summary>
    public DateTimeOffset OccurredAt { get; init; }
}
