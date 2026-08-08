namespace AuditableOperations.Attributes;

/// <summary>
/// Excludes a type or property from auditing. Applied to a class, the entity produces no records at
/// all; applied to a property, the property never appears in a record.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class AuditIgnoreAttribute : Attribute
{
}
