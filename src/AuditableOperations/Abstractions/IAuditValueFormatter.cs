namespace AuditableOperations.Abstractions;

/// <summary>
/// Converts CLR property values into the shape stored in an audit record.
/// </summary>
public interface IAuditValueFormatter
{
    /// <summary>Formats a single property value.</summary>
    /// <param name="value">The raw value read from the entity. May be <see langword="null"/>.</param>
    /// <returns>A value suitable for serialization by a sink.</returns>
    object? Format(object? value);
}
