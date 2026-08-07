namespace AuditableOperations;

public sealed class AuditableOperationsOptions
{
    public bool EnableEntityChanges { get; set; } = true;

    public bool AuditAddedEntities { get; set; } = true;

    public bool AuditModifiedEntities { get; set; } = true;

    public bool AuditDeletedEntities { get; set; } = true;

    public bool CaptureUser { get; set; } = true;

    public bool CaptureTenant { get; set; } = true;

    public bool RedactSensitiveValues { get; set; } = true;

    public string RedactedPlaceholder { get; set; } = "***";

    public bool RequireAuditedAttribute { get; set; } = true;

    public bool IgnoreConcurrencyTokens { get; set; } = true;

    public bool IgnoreShadowProperties { get; set; } = true;
}
