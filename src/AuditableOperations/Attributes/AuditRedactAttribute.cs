namespace AuditableOperations.Attributes;

[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class AuditRedactAttribute : Attribute
{
}
