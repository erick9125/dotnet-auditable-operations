using AuditableOperations.Abstractions;
using AuditableOperations.Context;
using AuditableOperations.EntityFramework;
using AuditableOperations.Redaction;
using AuditableOperations.Sinks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AuditableOperations.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAuditableOperations(
        this IServiceCollection services,
        Action<AuditableOperationsOptions>? configure = null)
    {
        if (configure is not null)
        {
            services.Configure(configure);
        }
        else
        {
            services.Configure<AuditableOperationsOptions>(_ => { });
        }

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

    public static IServiceCollection AddHttpAuditContext(
        this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.RemoveAll<IAuditContextAccessor>();
        services.AddScoped<IAuditContextAccessor, HttpAuditContextAccessor>();
        return services;
    }

    public static IServiceCollection AddInMemoryAuditSink(
        this IServiceCollection services)
    {
        services.RemoveAll<IAuditSink>();
        services.AddSingleton<InMemoryAuditSink>();
        services.AddSingleton<IAuditSink>(sp => sp.GetRequiredService<InMemoryAuditSink>());
        return services;
    }

    public static IServiceCollection AddDatabaseAuditSink(
        this IServiceCollection services,
        Action<IServiceProvider, DbContextOptionsBuilder> configureDbContext)
    {
        services.RemoveAll<IAuditSink>();
        services.AddDbContext<AuditDbContext>(configureDbContext);
        services.AddScoped<IAuditSink, DatabaseAuditSink>();
        return services;
    }

    public static IServiceCollection AddDatabaseAuditSink(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder> configureDbContext)
    {
        return services.AddDatabaseAuditSink((_, options) => configureDbContext(options));
    }
}
