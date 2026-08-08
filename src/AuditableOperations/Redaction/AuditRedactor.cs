using AuditableOperations.Abstractions;
using AuditableOperations.Models;
using Microsoft.Extensions.Options;

namespace AuditableOperations.Redaction;

/// <summary>
/// Builds <see cref="AuditPropertyChange"/> instances, replacing values of properties marked with
/// <see cref="Attributes.AuditRedactAttribute"/> before they can reach a sink.
/// </summary>
public sealed class AuditRedactor
{
    private readonly AuditableOperationsOptions _options;
    private readonly IAuditValueFormatter _formatter;

    /// <summary>Initializes a new instance of the <see cref="AuditRedactor"/> class.</summary>
    /// <param name="options">Audit configuration.</param>
    /// <param name="formatter">Formatter applied to non-redacted values.</param>
    public AuditRedactor(
        IOptions<AuditableOperationsOptions> options,
        IAuditValueFormatter formatter)
    {
        _options = options.Value;
        _formatter = formatter;
    }

    /// <summary>Creates a change entry for a single property.</summary>
    /// <param name="propertyName">Name reported in the audit record, qualified for owned types.</param>
    /// <param name="shouldRedact">
    /// Whether the property is marked for redaction, as decided by
    /// <see cref="EntityFramework.EntityMetadataResolver.ShouldRedactProperty"/>.
    /// </param>
    /// <param name="previousValue">Value before the change, or <see langword="null"/> for inserts.</param>
    /// <param name="currentValue">Value after the change, or <see langword="null"/> for deletes.</param>
    /// <returns>A change entry safe to hand to any <see cref="IAuditSink"/>.</returns>
    public AuditPropertyChange CreateChange(
        string propertyName,
        bool shouldRedact,
        object? previousValue,
        object? currentValue)
    {
        // Redaction is never conditional on a global switch: the attribute is a per-property
        // security decision and must survive any configuration.
        if (shouldRedact)
        {
            return new AuditPropertyChange
            {
                Property = propertyName,
                PreviousValue = previousValue is null ? null : _options.RedactedPlaceholder,
                CurrentValue = currentValue is null ? null : _options.RedactedPlaceholder,
                IsRedacted = true
            };
        }

        return new AuditPropertyChange
        {
            Property = propertyName,
            PreviousValue = _formatter.Format(previousValue),
            CurrentValue = _formatter.Format(currentValue),
            IsRedacted = false
        };
    }
}
