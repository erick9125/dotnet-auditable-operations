using AuditableOperations.Models;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace AuditableOperations.EntityFramework;

/// <summary>
/// A change captured before <c>SaveChanges</c> runs, held until generated keys can be resolved.
/// </summary>
internal sealed class PendingAuditCapture
{
    /// <summary>The root (non-owned) entry the record will describe.</summary>
    public required EntityEntry Entry { get; init; }

    public required AuditAction Action { get; init; }

    public required string EntityType { get; init; }

    public required List<AuditPropertyChange> Changes { get; init; }
}
