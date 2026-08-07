namespace AuditableOperations.Models;

public sealed record AuditContext
{
    public string? UserId { get; init; }

    public string? TenantId { get; init; }

    public string? CorrelationId { get; init; }

    public string? Source { get; init; }
}
