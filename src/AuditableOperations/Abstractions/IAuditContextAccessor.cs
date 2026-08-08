using AuditableOperations.Models;

namespace AuditableOperations.Abstractions;

/// <summary>
/// Supplies the ambient identity and origin attached to audit records. Implement this to support a
/// host the library does not know about — background workers, message consumers, CLI tools.
/// </summary>
public interface IAuditContextAccessor
{
    /// <summary>Returns the context for the operation in flight.</summary>
    /// <returns>An <see cref="AuditContext"/>; empty when nothing is known.</returns>
    AuditContext GetCurrent();
}
