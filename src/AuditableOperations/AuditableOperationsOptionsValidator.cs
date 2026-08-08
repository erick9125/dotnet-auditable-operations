using Microsoft.Extensions.Options;

namespace AuditableOperations;

/// <summary>
/// Fails fast on configuration that would silently produce a broken audit trail.
/// </summary>
internal sealed class AuditableOperationsOptionsValidator : IValidateOptions<AuditableOperationsOptions>
{
    public ValidateOptionsResult Validate(string? name, AuditableOperationsOptions options)
    {
        var failures = new List<string>();

        // An empty placeholder would produce changes flagged IsRedacted while showing nothing,
        // which reads as "the value was blank" rather than "the value was hidden".
        if (string.IsNullOrEmpty(options.RedactedPlaceholder))
        {
            failures.Add(
                $"{nameof(AuditableOperationsOptions.RedactedPlaceholder)} must be a non-empty string.");
        }

        if (options.MaxOwnedTypeDepth < 0)
        {
            failures.Add(
                $"{nameof(AuditableOperationsOptions.MaxOwnedTypeDepth)} must not be negative.");
        }

        if (!Enum.IsDefined(options.SinkFailureBehavior))
        {
            failures.Add(
                $"{nameof(AuditableOperationsOptions.SinkFailureBehavior)} is not a defined value.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
