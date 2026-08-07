using System.Collections.Concurrent;
using System.Reflection;
using AuditableOperations.Attributes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Options;

namespace AuditableOperations.EntityFramework;

public sealed class EntityMetadataResolver
{
    private readonly AuditableOperationsOptions _options;
    private readonly ConcurrentDictionary<Type, bool> _entityAuditCache = new();
    private readonly ConcurrentDictionary<IProperty, PropertyAuditRules> _propertyRulesCache = new();

    public EntityMetadataResolver(IOptions<AuditableOperationsOptions> options)
    {
        _options = options.Value;
    }

    public bool ShouldAuditEntity(EntityEntry entry)
    {
        var clrType = entry.Metadata.ClrType;
        if (clrType.IsDefined(typeof(AuditIgnoreAttribute), inherit: true))
        {
            return false;
        }

        return _entityAuditCache.GetOrAdd(clrType, static (type, requireAudited) =>
        {
            if (!requireAudited)
            {
                return true;
            }

            return type.IsDefined(typeof(AuditedAttribute), inherit: true);
        }, _options.RequireAuditedAttribute);
    }

    public bool ShouldAuditProperty(PropertyEntry property)
    {
        var rules = GetPropertyRules(property.Metadata);
        return !rules.Ignore;
    }

    public bool ShouldRedactProperty(IProperty property)
    {
        return GetPropertyRules(property).Redact;
    }

    public PropertyInfo? GetClrProperty(IProperty property)
    {
        return property.PropertyInfo;
    }

    private PropertyAuditRules GetPropertyRules(IProperty property)
    {
        return _propertyRulesCache.GetOrAdd(property, static (prop, options) =>
        {
            if (options.IgnoreShadowProperties && prop.IsShadowProperty())
            {
                return new PropertyAuditRules(Ignore: true, Redact: false);
            }

            if (options.IgnoreConcurrencyTokens && prop.IsConcurrencyToken)
            {
                return new PropertyAuditRules(Ignore: true, Redact: false);
            }

            if (prop.IsPrimaryKey())
            {
                return new PropertyAuditRules(Ignore: true, Redact: false);
            }

            var propertyInfo = prop.PropertyInfo;
            if (propertyInfo is null)
            {
                return new PropertyAuditRules(Ignore: true, Redact: false);
            }

            if (propertyInfo.IsDefined(typeof(AuditIgnoreAttribute), inherit: true))
            {
                return new PropertyAuditRules(Ignore: true, Redact: false);
            }

            var redact = propertyInfo.IsDefined(typeof(AuditRedactAttribute), inherit: true);
            return new PropertyAuditRules(Ignore: false, Redact: redact);
        }, _options);
    }

    private readonly record struct PropertyAuditRules(bool Ignore, bool Redact);
}
