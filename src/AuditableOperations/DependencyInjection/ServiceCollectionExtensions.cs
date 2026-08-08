using AuditableOperations.Abstractions;
using AuditableOperations.Context;
using AuditableOperations.EntityFramework;
using AuditableOperations.Redaction;
using AuditableOperations.Sinks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace AuditableOperations.DependencyInjection;

/// <summary>
/// Registration helpers for the audit pipeline.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers change capture, redaction and the audit interceptor.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional configuration callback.</param>
    /// <returns>The same <paramref name="services"/>, for chaining.</returns>
    /// <remarks>
    /// Call this first, then a sink helper. Without a sink, <see cref="NullAuditSink"/> discards
    /// records and warns once. Attach the interceptor to your context with
    /// <see cref="DbContextOptionsBuilderExtensions.UseAuditableOperations"/>.
    /// </remarks>
    public static IServiceCollection AddAuditableOperations(
        this IServiceCollection services,
        Action<AuditableOperationsOptions>? configure = null)
    {
        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<AuditableOperationsOptions>, AuditableOperationsOptionsValidator>());

        services.TryAddSingleton<IAuditValueFormatter, DefaultValueFormatter>();
        services.TryAddSingleton<EntityMetadataResolver>();
        services.TryAddSingleton<AuditRedactor>();
        services.TryAddScoped<EntityChangeCollector>();
        services.TryAddScoped<AuditSaveChangesInterceptor>();
        services.TryAddScoped<IAuditContextAccessor, NullAuditContextAccessor>();

        // Fallback so a forgotten sink registration warns at runtime instead of failing with an
        // opaque dependency injection error when the DbContext is first constructed. The
        // AddXxxAuditSink helpers replace it.
        services.TryAddSingleton<IAuditSink, NullAuditSink>();

        return services;
    }

    /// <summary>
    /// Resolves the audit context from the current HTTP request, replacing any accessor already
    /// registered.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same <paramref name="services"/>, for chaining.</returns>
    public static IServiceCollection AddHttpAuditContext(
        this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.RemoveAll<IAuditContextAccessor>();
        services.AddScoped<IAuditContextAccessor, HttpAuditContextAccessor>();
        return services;
    }

    /// <summary>
    /// Uses <see cref="InMemoryAuditSink"/>, replacing any sink already registered. For tests and
    /// local inspection only — it retains every record.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same <paramref name="services"/>, for chaining.</returns>
    public static IServiceCollection AddInMemoryAuditSink(
        this IServiceCollection services)
    {
        services.RemoveAll<IAuditSink>();
        services.AddSingleton<InMemoryAuditSink>();
        services.AddSingleton<IAuditSink>(sp => sp.GetRequiredService<InMemoryAuditSink>());
        return services;
    }

    /// <summary>
    /// Uses <see cref="DatabaseAuditSink"/> backed by a dedicated <see cref="AuditDbContext"/>,
    /// replacing any sink already registered.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureDbContext">Configures the audit store's provider and connection.</param>
    /// <returns>The same <paramref name="services"/>, for chaining.</returns>
    public static IServiceCollection AddDatabaseAuditSink(
        this IServiceCollection services,
        Action<IServiceProvider, DbContextOptionsBuilder> configureDbContext)
    {
        services.RemoveAll<IAuditSink>();
        services.AddDbContext<AuditDbContext>(configureDbContext);
        services.AddScoped<IAuditSink, DatabaseAuditSink>();
        return services;
    }

    /// <inheritdoc cref="AddDatabaseAuditSink(IServiceCollection, Action{IServiceProvider, DbContextOptionsBuilder})" />
    /// <param name="services">The service collection.</param>
    /// <param name="configureDbContext">Configures the audit store's provider and connection.</param>
    public static IServiceCollection AddDatabaseAuditSink(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder> configureDbContext)
    {
        return services.AddDatabaseAuditSink((_, options) => configureDbContext(options));
    }
}
