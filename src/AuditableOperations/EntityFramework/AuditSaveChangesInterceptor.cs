using System.Collections.Concurrent;
using AuditableOperations.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace AuditableOperations.EntityFramework;

public sealed class AuditSaveChangesInterceptor : SaveChangesInterceptor
{
    private readonly EntityChangeCollector _collector;
    private readonly IAuditContextAccessor _contextAccessor;
    private readonly IAuditSink _sink;
    private readonly ILogger<AuditSaveChangesInterceptor> _logger;
    private readonly ConcurrentDictionary<DbContextId, IReadOnlyList<PendingAuditCapture>> _pending = new();

    public AuditSaveChangesInterceptor(
        EntityChangeCollector collector,
        IAuditContextAccessor contextAccessor,
        IAuditSink sink,
        ILogger<AuditSaveChangesInterceptor> logger)
    {
        _collector = collector;
        _contextAccessor = contextAccessor;
        _sink = sink;
        _logger = logger;
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        CapturePending(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        CapturePending(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public override int SavedChanges(
        SaveChangesCompletedEventData eventData,
        int result)
    {
        PersistPending(eventData.Context, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        return result;
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        await PersistPending(eventData.Context, cancellationToken);
        return result;
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        DiscardPending(eventData.Context);
    }

    public override Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        DiscardPending(eventData.Context);
        return Task.CompletedTask;
    }

    private void CapturePending(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var captures = _collector.Capture(context);
        if (captures.Count == 0)
        {
            _pending.TryRemove(context.ContextId, out _);
            return;
        }

        _pending[context.ContextId] = captures;
    }

    private async Task PersistPending(DbContext? context, CancellationToken cancellationToken)
    {
        if (context is null)
        {
            return;
        }

        if (!_pending.TryRemove(context.ContextId, out var captures) || captures.Count == 0)
        {
            return;
        }

        var auditContext = _contextAccessor.GetCurrent();
        var records = _collector.Finalize(captures, auditContext, DateTimeOffset.UtcNow);

        try
        {
            await _sink.WriteAsync(records, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to persist {Count} audit records for context {ContextId}",
                records.Count,
                context.ContextId);
            throw;
        }
    }

    private void DiscardPending(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        _pending.TryRemove(context.ContextId, out _);
    }
}
