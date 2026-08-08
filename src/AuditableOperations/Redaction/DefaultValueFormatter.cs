using System.Collections;
using System.Text.Json;
using AuditableOperations.Abstractions;

namespace AuditableOperations.Redaction;

/// <summary>
/// Converts CLR property values into shapes that serialize predictably into an audit record.
/// </summary>
public sealed class DefaultValueFormatter : IAuditValueFormatter
{
    /// <summary>Bytes of a binary value kept before the base64 output is truncated.</summary>
    public const int MaxBinaryLength = 4 * 1024;

    /// <summary>Maximum nesting followed into collections before falling back to <c>ToString</c>.</summary>
    public const int MaxCollectionDepth = 3;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    /// <inheritdoc />
    public object? Format(object? value) => Format(value, depth: 0);

    private static object? Format(object? value, int depth)
    {
        if (value is null)
        {
            return null;
        }

        // Boxing already unwrapped Nullable<T>, so the runtime type is never Nullable<T> here.
        switch (value)
        {
            case string or bool or Guid or DateTime or DateTimeOffset or DateOnly or TimeOnly or TimeSpan:
                return value;

            case byte[] bytes:
                return FormatBinary(bytes);
        }

        var type = value.GetType();

        if (type.IsEnum)
        {
            return value.ToString();
        }

        // Covers every numeric and char type; decimal is not a primitive but formats the same way.
        if (type.IsPrimitive || type == typeof(decimal))
        {
            return value;
        }

        if (value is IEnumerable enumerable)
        {
            if (depth >= MaxCollectionDepth)
            {
                return value.ToString();
            }

            var items = new List<object?>();
            foreach (var item in enumerable)
            {
                items.Add(Format(item, depth + 1));
            }

            return items;
        }

        try
        {
            return JsonSerializer.Serialize(value, JsonOptions);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            // Reference cycles and unmapped types: fall back rather than fail an audit write.
            return value.ToString();
        }
    }

    private static string FormatBinary(byte[] bytes)
    {
        if (bytes.Length <= MaxBinaryLength)
        {
            return Convert.ToBase64String(bytes);
        }

        // A blob column would otherwise put its entire contents into the trail on every change.
        return string.Concat(
            Convert.ToBase64String(bytes, 0, MaxBinaryLength),
            AuditFieldLimits.TruncationMarker,
            $" ({bytes.Length} bytes)");
    }
}
