using AuditableOperations.Abstractions;
using AuditableOperations.Models;
using Microsoft.Extensions.Logging;

namespace AuditableOperations.Sinks;

/// <summary>
/// Fallback sink registered when the consumer has not chosen one. It discards records and warns
/// once, so a forgotten sink registration surfaces as an actionable log entry instead of an opaque
/// dependency injection failure when the <c>DbContext</c> is first constructed.
/// </summary>
public sealed class NullAuditSink : IAuditSink
{
    private readonly ILogger<NullAuditSink> _logger;
    private int _warned;

    /// <summary>Initializes a new instance of the <see cref="NullAuditSink"/> class.</summary>
    /// <param name="logger">Logger used for the one-time warning.</param>
    public NullAuditSink(ILogger<NullAuditSink> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public Task WriteAsync(
        IReadOnlyCollection<AuditRecord> records,
        CancellationToken cancellationToken = default)
    {
        WarnOnce(records.Count);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Write(IReadOnlyCollection<AuditRecord> records)
    {
        WarnOnce(records.Count);
    }

    private void WarnOnce(int discardedCount)
    {
        if (discardedCount == 0 || Interlocked.Exchange(ref _warned, 1) != 0)
        {
            return;
        }

        _logger.LogWarning(
            "No IAuditSink is registered, so audit records are being discarded (first batch: {Count}). " +
            "Call AddInMemoryAuditSink(), AddDatabaseAuditSink(...) or register your own IAuditSink.",
            discardedCount);
    }
}
