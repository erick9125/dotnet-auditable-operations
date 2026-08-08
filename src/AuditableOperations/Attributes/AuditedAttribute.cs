namespace AuditableOperations.Attributes;

/// <summary>
/// Opts an entity type into change capture while
/// <see cref="AuditableOperationsOptions.RequireAuditedAttribute"/> is enabled (the default).
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class AuditedAttribute : Attribute
{
}
