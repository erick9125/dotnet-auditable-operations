namespace AuditableOperations.Abstractions;

public interface IAuditValueFormatter
{
    object? Format(object? value);
}
