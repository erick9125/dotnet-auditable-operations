using AuditableOperations.Models;

namespace AuditableOperations.Abstractions;

/// <summary>
/// Destination for completed audit records.
/// </summary>
public interface IAuditSink
{
    /// <summary>Writes records produced by an asynchronous <c>SaveChangesAsync</c>.</summary>
    /// <param name="records">The records to persist. Never <see langword="null"/>, may be empty.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task WriteAsync(
        IReadOnlyCollection<AuditRecord> records,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes records produced by a synchronous <c>SaveChanges</c>.
    /// </summary>
    /// <param name="records">The records to persist. Never <see langword="null"/>, may be empty.</param>
    /// <remarks>
    /// The default implementation blocks on <see cref="WriteAsync"/>. Sinks that perform I/O should
    /// override it with a genuinely synchronous path — blocking a thread pool thread on I/O under
    /// load is how thread pool starvation starts.
    /// </remarks>
    void Write(IReadOnlyCollection<AuditRecord> records)
        => WriteAsync(records).GetAwaiter().GetResult();
}
