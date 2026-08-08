namespace AuditableOperations.Models;

/// <summary>
/// One property that changed, with its values before and after.
/// </summary>
public sealed record AuditPropertyChange
{
    /// <summary>
    /// Property name. Owned-type members are qualified with their navigation path, such as
    /// <c>"Address.City"</c>.
    /// </summary>
    public required string Property { get; init; }

    /// <summary>
    /// Value before the change, or <see langword="null"/> on insert. Replaced with the redaction
    /// placeholder when <see cref="IsRedacted"/> is <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// Typed as <see cref="object"/>. After a round trip through a JSON-backed sink this is a
    /// <see cref="System.Text.Json.JsonElement"/> rather than the original CLR type.
    /// </remarks>
    public object? PreviousValue { get; init; }

    /// <summary>
    /// Value after the change, or <see langword="null"/> on delete. Replaced with the redaction
    /// placeholder when <see cref="IsRedacted"/> is <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// Typed as <see cref="object"/>. After a round trip through a JSON-backed sink this is a
    /// <see cref="System.Text.Json.JsonElement"/> rather than the original CLR type.
    /// </remarks>
    public object? CurrentValue { get; init; }

    /// <summary>
    /// Whether the values were replaced because the property carries
    /// <see cref="Attributes.AuditRedactAttribute"/>.
    /// </summary>
    public bool IsRedacted { get; init; }
}
