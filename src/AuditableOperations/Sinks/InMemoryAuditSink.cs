using System.Collections.Concurrent;
using AuditableOperations.Abstractions;
using AuditableOperations.Models;

namespace AuditableOperations.Sinks;

/// <summary>
/// Keeps records in memory for tests and local inspection.
/// </summary>
/// <remarks>
/// Unbounded: records accumulate for the lifetime of the instance, which is a singleton. Do not use
/// this sink in production.
/// </remarks>
public sealed class InMemoryAuditSink : IAuditSink
{
    private readonly ConcurrentQueue<AuditRecord> _records = new();

    /// <summary>Snapshot of everything written so far, in write order.</summary>
    public IReadOnlyList<AuditRecord> Records => _records.ToArray();

    /// <inheritdoc />
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

    /// <summary>Discards every recorded entry.</summary>
    public void Clear()
    {
        while (_records.TryDequeue(out _))
        {
        }
    }
}
