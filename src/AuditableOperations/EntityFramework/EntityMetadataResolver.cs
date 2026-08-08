using System.Collections.Concurrent;
using AuditableOperations.Attributes;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Options;

namespace AuditableOperations.EntityFramework;

/// <summary>
/// Decides what is auditable, caching the reflection results per EF Core model element.
/// </summary>
public sealed class EntityMetadataResolver
{
    private readonly AuditableOperationsOptions _options;
    private readonly ConcurrentDictionary<Type, bool> _entityAuditCache = new();
    private readonly ConcurrentDictionary<IProperty, PropertyAuditRules> _propertyRulesCache = new();
    private readonly ConcurrentDictionary<IEntityType, bool> _ownedNavigationCache = new();
    private readonly ConcurrentDictionary<INavigationBase, bool> _navigationAuditCache = new();

    /// <summary>Initializes a new instance of the <see cref="EntityMetadataResolver"/> class.</summary>
    /// <param name="options">Audit configuration.</param>
    public EntityMetadataResolver(IOptions<AuditableOperationsOptions> options)
    {
        _options = options.Value;
    }

    /// <summary>Determines whether an entity entry should produce audit records.</summary>
    /// <param name="entry">The change-tracker entry.</param>
    /// <returns><see langword="true"/> when the entity is auditable.</returns>
    public bool ShouldAuditEntity(EntityEntry entry)
    {
        return _entityAuditCache.GetOrAdd(entry.Metadata.ClrType, static (type, requireAudited) =>
        {
            if (type.IsDefined(typeof(AuditIgnoreAttribute), inherit: true))
            {
                return false;
            }

            return !requireAudited || type.IsDefined(typeof(AuditedAttribute), inherit: true);
        }, _options.RequireAuditedAttribute);
    }

    /// <summary>Determines whether a scalar property should appear in the audit record.</summary>
    /// <param name="property">The property entry.</param>
    /// <returns><see langword="true"/> when the property is auditable.</returns>
    public bool ShouldAuditProperty(PropertyEntry property)
    {
        return !GetPropertyRules(property.Metadata).Ignore;
    }

    /// <summary>Determines whether a property's values must be replaced with the redaction placeholder.</summary>
    /// <param name="property">The property entry.</param>
    /// <returns><see langword="true"/> when the property is marked with <see cref="AuditRedactAttribute"/>.</returns>
    public bool ShouldRedactProperty(PropertyEntry property)
    {
        return GetPropertyRules(property.Metadata).Redact;
    }

    /// <summary>
    /// Determines whether an entity type owns any value objects, so callers can skip walking the
    /// graph of entities that cannot contribute owned changes.
    /// </summary>
    /// <param name="entityType">The EF Core entity type.</param>
    /// <returns><see langword="true"/> when at least one navigation targets an owned type.</returns>
    public bool HasOwnedNavigations(IEntityType entityType)
    {
        return _ownedNavigationCache.GetOrAdd(
            entityType,
            static type => type.GetNavigations().Any(navigation => navigation.TargetEntityType.IsOwned()));
    }

    /// <summary>Determines whether changes inside an owned navigation should be captured.</summary>
    /// <param name="navigation">The navigation pointing at the owned type.</param>
    /// <returns><see langword="true"/> when neither the navigation nor the owned type opts out.</returns>
    public bool ShouldAuditOwnedNavigation(INavigationBase navigation)
    {
        return _navigationAuditCache.GetOrAdd(navigation, static nav =>
        {
            if (nav.PropertyInfo?.IsDefined(typeof(AuditIgnoreAttribute), inherit: true) == true)
            {
                return false;
            }

            return !nav.TargetEntityType.ClrType.IsDefined(typeof(AuditIgnoreAttribute), inherit: true);
        });
    }

    private PropertyAuditRules GetPropertyRules(IProperty property)
    {
        return _propertyRulesCache.GetOrAdd(property, static (prop, options) =>
        {
            if (options.IgnoreShadowProperties && prop.IsShadowProperty())
            {
                return PropertyAuditRules.Ignored;
            }

            if (options.IgnoreConcurrencyTokens && prop.IsConcurrencyToken)
            {
                return PropertyAuditRules.Ignored;
            }

            // Primary keys identify the record through AuditRecord.EntityId instead of being
            // reported as a property change.
            if (prop.IsPrimaryKey())
            {
                return PropertyAuditRules.Ignored;
            }

            var propertyInfo = prop.PropertyInfo;
            if (propertyInfo is null || propertyInfo.IsDefined(typeof(AuditIgnoreAttribute), inherit: true))
            {
                return PropertyAuditRules.Ignored;
            }

            return new PropertyAuditRules(
                Ignore: false,
                Redact: propertyInfo.IsDefined(typeof(AuditRedactAttribute), inherit: true));
        }, _options);
    }

    private readonly record struct PropertyAuditRules(bool Ignore, bool Redact)
    {
        public static PropertyAuditRules Ignored { get; } = new(Ignore: true, Redact: false);
    }
}
