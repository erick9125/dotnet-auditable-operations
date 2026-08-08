namespace AuditableOperations.Models;

/// <summary>
/// The kind of change an <see cref="AuditRecord"/> describes.
/// </summary>
public enum AuditAction
{
    /// <summary>The entity was inserted.</summary>
    Created,

    /// <summary>The entity, or one of its owned types, was modified.</summary>
    Updated,

    /// <summary>The entity was deleted.</summary>
    Deleted
}
