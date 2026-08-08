using System.Collections.Concurrent;
using AuditableOperations.Abstractions;
using AuditableOperations.Models;

namespace AuditableOperations.Sinks;

public sealed class InMemoryAuditSink : IAuditSink
{
    private readonly ConcurrentQueue<AuditRecord> _records = new();

    public IReadOnlyList<AuditRecord> Records => _records.ToArray();

    public Task WriteAsync(
        IReadOnlyCollection<AuditRecord> records,
        CancellationToken cancellationToken = default)
    {
        Write(records);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Write(IReadOnlyCollection<AuditRecord> records)
    {
        foreach (var record in records)
        {
            _records.Enqueue(record);
        }
    }

    public void Clear()
    {
        while (_records.TryDequeue(out _))
        {
        }
    }
}
