namespace AuditableOperations;

/// <summary>
/// How <see cref="EntityFramework.AuditSaveChangesInterceptor"/> reacts when
/// <see cref="Abstractions.IAuditSink.WriteAsync"/> fails.
/// </summary>
/// <remarks>
/// The sink runs after the business <c>SaveChanges</c> has committed, so rethrowing cannot roll the
/// business data back — it only surfaces an error for an operation that already succeeded, which
/// typically triggers a caller retry and duplicates the data.
/// </remarks>
public enum SinkFailureBehavior
{
    /// <summary>
    /// Log the failure at error level and let the business operation succeed. Default.
    /// The audit record is lost, so treat these log entries as alertable.
    /// </summary>
    LogAndContinue = 0,

    /// <summary>
    /// Log the failure and rethrow. Choose this only when a missing audit record must fail the
    /// request loudly, and be aware the business data is already committed.
    /// </summary>
    Throw = 1
}
