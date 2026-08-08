namespace AuditableOperations;

/// <summary>
/// Configuration for entity change capture, context enrichment and sink behavior.
/// </summary>
public sealed class AuditableOperationsOptions
{
    /// <summary>Master switch for EF Core change capture. Default <see langword="true"/>.</summary>
    public bool EnableEntityChanges { get; set; } = true;

    /// <summary>Capture inserts as <see cref="Models.AuditAction.Created"/>. Default <see langword="true"/>.</summary>
    public bool AuditAddedEntities { get; set; } = true;

    /// <summary>Capture updates as <see cref="Models.AuditAction.Updated"/>. Default <see langword="true"/>.</summary>
    public bool AuditModifiedEntities { get; set; } = true;

    /// <summary>Capture deletes as <see cref="Models.AuditAction.Deleted"/>. Default <see langword="true"/>.</summary>
    public bool AuditDeletedEntities { get; set; } = true;

    /// <summary>Resolve <see cref="Models.AuditContext.UserId"/> from the context accessor. Default <see langword="true"/>.</summary>
    public bool CaptureUser { get; set; } = true;

    /// <summary>Resolve <see cref="Models.AuditContext.TenantId"/> from the context accessor. Default <see langword="true"/>.</summary>
    public bool CaptureTenant { get; set; } = true;

    /// <summary>
    /// Replacement written in place of values on properties marked with
    /// <see cref="Attributes.AuditRedactAttribute"/>. Must be non-empty.
    /// </summary>
    /// <remarks>
    /// There is deliberately no option to disable redaction: <see cref="Attributes.AuditRedactAttribute"/>
    /// is an explicit security decision on a specific property and a global flag must not be able to
    /// override it. To stop auditing a sensitive property altogether, use
    /// <see cref="Attributes.AuditIgnoreAttribute"/>.
    /// </remarks>
    public string RedactedPlaceholder { get; set; } = "***";

    /// <summary>Only audit types marked with <see cref="Attributes.AuditedAttribute"/>. Default <see langword="true"/>.</summary>
    public bool RequireAuditedAttribute { get; set; } = true;

    /// <summary>Skip row versions and other concurrency tokens. Default <see langword="true"/>.</summary>
    public bool IgnoreConcurrencyTokens { get; set; } = true;

    /// <summary>Skip EF Core shadow properties. Default <see langword="true"/>.</summary>
    public bool IgnoreShadowProperties { get; set; } = true;

    /// <summary>
    /// What to do when a sink write fails after the business transaction committed.
    /// Default <see cref="SinkFailureBehavior.LogAndContinue"/>.
    /// </summary>
    public SinkFailureBehavior SinkFailureBehavior { get; set; } = SinkFailureBehavior.LogAndContinue;

    /// <summary>
    /// Maximum depth followed into owned-type (value object) graphs when capturing changes.
    /// Default <c>5</c>.
    /// </summary>
    public int MaxOwnedTypeDepth { get; set; } = 5;
}
