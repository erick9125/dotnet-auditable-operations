namespace AuditableOperations;

/// <summary>
/// Maximum lengths applied to <see cref="Models.AuditRecord"/> string fields before they reach a sink.
/// </summary>
/// <remarks>
/// Audit records are written after the business transaction has already committed, so a value that
/// overflows its storage column would fail the audit write with the business data already persisted.
/// Values are therefore truncated at capture time rather than rejected. These limits match the column
/// widths configured by <see cref="Sinks.AuditDbContext"/>; custom sinks should honor them or widen
/// their own schema accordingly.
/// </remarks>
public static class AuditFieldLimits
{
    /// <summary>Maximum length of <see cref="Models.AuditRecord.Action"/>.</summary>
    public const int Action = 32;

    /// <summary>Maximum length of <see cref="Models.AuditRecord.EntityType"/>.</summary>
    public const int EntityType = 256;

    /// <summary>Maximum length of <see cref="Models.AuditRecord.EntityId"/>.</summary>
    public const int EntityId = 128;

    /// <summary>Maximum length of <see cref="Models.AuditRecord.UserId"/>.</summary>
    public const int UserId = 128;

    /// <summary>Maximum length of <see cref="Models.AuditRecord.TenantId"/>.</summary>
    public const int TenantId = 128;

    /// <summary>Maximum length of <see cref="Models.AuditRecord.CorrelationId"/>.</summary>
    public const int CorrelationId = 128;

    /// <summary>Maximum length of <see cref="Models.AuditRecord.Source"/>.</summary>
    public const int Source = 512;

    /// <summary>Marker appended to values that were shortened, so truncation is visible in the trail.</summary>
    public const string TruncationMarker = "...";

    /// <summary>
    /// Shortens <paramref name="value"/> to <paramref name="maxLength"/> characters, appending
    /// <see cref="TruncationMarker"/> when content was removed.
    /// </summary>
    /// <param name="value">The value to shorten. <see langword="null"/> is returned unchanged.</param>
    /// <param name="maxLength">The inclusive maximum length of the result.</param>
    /// <returns>A value no longer than <paramref name="maxLength"/>.</returns>
    public static string? Truncate(string? value, int maxLength)
    {
        if (value is null || value.Length <= maxLength)
        {
            return value;
        }

        if (maxLength <= TruncationMarker.Length)
        {
            return value[..maxLength];
        }

        return string.Concat(
            value.AsSpan(0, maxLength - TruncationMarker.Length),
            TruncationMarker);
    }
}
