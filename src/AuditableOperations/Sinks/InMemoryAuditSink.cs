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
        foreach (var record in records)
        {
            _records.Enqueue(record);
        }

        return Task.CompletedTask;
    }

    public void Clear()
    {
        while (_records.TryDequeue(out _))
        {
        }
    }
}
