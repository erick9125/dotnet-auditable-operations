namespace AuditableOperations.Attributes;

/// <summary>
/// Replaces a property's values with <see cref="AuditableOperationsOptions.RedactedPlaceholder"/>
/// before they reach any sink.
/// </summary>
/// <remarks>
/// This is honored unconditionally — no configuration can switch it off. Use
/// <see cref="AuditIgnoreAttribute"/> to keep the property out of the trail entirely.
/// </remarks>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class AuditRedactAttribute : Attribute
{
}
