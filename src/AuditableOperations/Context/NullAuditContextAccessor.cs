using AuditableOperations.Abstractions;
using AuditableOperations.Models;

namespace AuditableOperations.Context;

public sealed class NullAuditContextAccessor : IAuditContextAccessor
{
    public static NullAuditContextAccessor Instance { get; } = new();

    public AuditContext GetCurrent() => new();
}
