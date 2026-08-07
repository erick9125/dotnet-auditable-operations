using AuditableOperations.Models;

namespace AuditableOperations.Abstractions;

public interface IAuditSink
{
    Task WriteAsync(
        IReadOnlyCollection<AuditRecord> records,
        CancellationToken cancellationToken = default);
}
