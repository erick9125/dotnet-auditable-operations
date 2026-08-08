using System.Collections.Concurrent;
using AuditableOperations.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AuditableOperations.EntityFramework;

/// <summary>
/// Captures auditable changes before <c>SaveChanges</c> and writes them to the configured
/// <see cref="IAuditSink"/> once the business data has been persisted.
/// </summary>
/// <remarks>
/// Register this interceptor with the same lifetime as the audit context accessor it depends on
/// (scoped by default). A singleton registration would capture a single
/// <see cref="IAuditContextAccessor"/> for the process and attribute records to the wrong user.
/// </remarks>
public sealed class AuditSaveChangesInterceptor : SaveChangesInterceptor
{
    private readonly EntityChangeCollector _collector;
    private readonly IAuditContextAccessor _contextAccessor;
    private readonly IAuditSink _sink;
    private readonly ILogger<AuditSaveChangesInterceptor> _logger;
    private readonly AuditableOperationsOptions _options;
    private readonly ConcurrentDictionary<DbContextId, IReadOnlyList<PendingAuditCapture>> _pending = new();

    /// <summary>Initializes a new instance of the <see cref="AuditSaveChangesInterceptor"/> class.</summary>
    /// <param name="collector">Captures and finalizes entity changes.</param>
    /// <param name="contextAccessor">Supplies user, tenant, correlation and source.</param>
    /// <param name="sink">Destination for the produced records.</param>
    /// <param name="logger">Logger used to report sink failures.</param>
    /// <param name="options">Audit configuration.</param>
    public AuditSaveChangesInterceptor(
        EntityChangeCollector collector,
        IAuditContextAccessor contextAccessor,
        IAuditSink sink,
        ILogger<AuditSaveChangesInterceptor> logger,
        IOptions<AuditableOperationsOptions> options)
    {
        _collector = collector;
        _contextAccessor = contextAccessor;
        _sink = sink;
        _logger = logger;
        _options = options.Value;
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
        var records = _collector.BuildRecords(captures, auditContext, DateTimeOffset.UtcNow);

        try
        {
            await _sink.WriteAsync(records, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // The business transaction already committed, so rethrowing cannot undo it — it only
            // reports a failure for an operation that succeeded, inviting a duplicating retry.
            _logger.LogError(
                ex,
                "Failed to persist {Count} audit records for context {ContextId}. Audit trail is incomplete.",
                records.Count,
                context.ContextId);

            if (_options.SinkFailureBehavior == SinkFailureBehavior.Throw)
            {
                throw;
            }
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
