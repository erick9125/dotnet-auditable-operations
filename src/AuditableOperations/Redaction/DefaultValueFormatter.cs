using System.Collections;
using System.Globalization;
using System.Text.Json;
using AuditableOperations.Abstractions;

namespace AuditableOperations.Redaction;

public sealed class DefaultValueFormatter : IAuditValueFormatter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    public object? Format(object? value)
    {
        if (value is null)
        {
            return null;
        }

        var type = value.GetType();

        if (type.IsEnum)
        {
            return value.ToString();
        }

        if (value is string or bool or Guid or DateTime or DateTimeOffset or DateOnly or TimeOnly)
        {
            return value;
        }

        if (value is byte[] bytes)
        {
            return Convert.ToBase64String(bytes);
        }

        if (IsNumeric(type))
        {
            return Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
        }

        if (value is IEnumerable enumerable and not string)
        {
            var items = new List<object?>();
            foreach (var item in enumerable)
            {
                items.Add(Format(item));
            }

            return items;
        }

        if (IsSimpleType(type))
        {
            return value;
        }

        try
        {
            return JsonSerializer.Serialize(value, JsonOptions);
        }
        catch
        {
            return value.ToString();
        }
    }

    private static bool IsNumeric(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        return type == typeof(byte)
            || type == typeof(sbyte)
            || type == typeof(short)
            || type == typeof(ushort)
            || type == typeof(int)
            || type == typeof(uint)
            || type == typeof(long)
            || type == typeof(ulong)
            || type == typeof(float)
            || type == typeof(double)
            || type == typeof(decimal);
    }

    private static bool IsSimpleType(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        return type.IsPrimitive
            || type.IsEnum
            || type == typeof(string)
            || type == typeof(decimal)
            || type == typeof(Guid)
            || type == typeof(DateTime)
            || type == typeof(DateTimeOffset)
            || type == typeof(DateOnly)
            || type == typeof(TimeOnly)
            || type == typeof(TimeSpan);
    }
}
