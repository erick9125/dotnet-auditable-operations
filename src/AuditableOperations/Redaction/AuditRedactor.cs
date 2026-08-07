using System.Reflection;
using AuditableOperations.Abstractions;
using AuditableOperations.Attributes;
using AuditableOperations.Models;
using Microsoft.Extensions.Options;

namespace AuditableOperations.Redaction;

public sealed class AuditRedactor
{
    private readonly AuditableOperationsOptions _options;
    private readonly IAuditValueFormatter _formatter;

    public AuditRedactor(
        IOptions<AuditableOperationsOptions> options,
        IAuditValueFormatter formatter)
    {
        _options = options.Value;
        _formatter = formatter;
    }

    public AuditPropertyChange CreateChange(
        PropertyInfo? propertyInfo,
        string propertyName,
        object? previousValue,
        object? currentValue)
    {
        var shouldRedact = _options.RedactSensitiveValues
            && propertyInfo?.IsDefined(typeof(AuditRedactAttribute), inherit: true) == true;

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
