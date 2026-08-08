using AuditableOperations.Abstractions;
using AuditableOperations.Models;

namespace AuditableOperations.Context;

/// <summary>
/// Default accessor used when the host provides no ambient identity, such as a console tool.
/// Produces an empty <see cref="AuditContext"/>.
/// </summary>
public sealed class NullAuditContextAccessor : IAuditContextAccessor
{
    /// <inheritdoc />
    public AuditContext GetCurrent() => new();
}
