namespace AuditableOperations.Models;

public sealed record AuditRecord
{
    public Guid Id { get; init; }

    public required string Action { get; init; }

    public required string EntityType { get; init; }

    public required string EntityId { get; init; }

    public string? UserId { get; init; }

    public string? TenantId { get; init; }

    public string? CorrelationId { get; init; }

    public string? Source { get; init; }

    public IReadOnlyList<AuditPropertyChange> Changes { get; init; }
        = Array.Empty<AuditPropertyChange>();

    public DateTimeOffset OccurredAt { get; init; }
}
