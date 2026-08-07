namespace AuditableOperations.Models;

public sealed record AuditPropertyChange
{
    public required string Property { get; init; }

    public object? PreviousValue { get; init; }

    public object? CurrentValue { get; init; }

    public bool IsRedacted { get; init; }
}
