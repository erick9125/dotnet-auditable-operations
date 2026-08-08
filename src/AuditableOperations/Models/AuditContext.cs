namespace AuditableOperations.Models;

/// <summary>
/// Ambient information about who triggered a change and from where, supplied by
/// <see cref="Abstractions.IAuditContextAccessor"/>.
/// </summary>
public sealed record AuditContext
{
    /// <summary>Identifier of the acting user, or <see langword="null"/> when unauthenticated.</summary>
    public string? UserId { get; init; }

    /// <summary>Tenant the operation belongs to, for multi-tenant hosts.</summary>
    public string? TenantId { get; init; }

    /// <summary>Identifier correlating this change with the request or job that caused it.</summary>
    public string? CorrelationId { get; init; }

    /// <summary>Human-readable origin, such as <c>"PUT /api/orders/42"</c> or a job name.</summary>
    public string? Source { get; init; }
}
