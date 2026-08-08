using AuditableOperations.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AuditableOperations.DependencyInjection;

/// <summary>
/// Wiring helpers for attaching auditing to an application <see cref="DbContext"/>.
/// </summary>
public static class DbContextOptionsBuilderExtensions
{
    /// <summary>
    /// Attaches <see cref="AuditSaveChangesInterceptor"/> to the context being configured.
    /// </summary>
    /// <param name="builder">The options builder supplied by <c>AddDbContext</c>.</param>
    /// <param name="serviceProvider">The scoped provider supplied by <c>AddDbContext</c>.</param>
    /// <returns>The same <paramref name="builder"/>, for chaining.</returns>
    /// <remarks>
    /// Use the <c>AddDbContext&lt;T&gt;((sp, options) =&gt; ...)</c> overload so the interceptor is
    /// resolved from the request scope:
    /// <code>
    /// services.AddDbContext&lt;AppDbContext&gt;((sp, options) => options
    ///     .UseNpgsql(connectionString)
    ///     .UseAuditableOperations(sp));
    /// </code>
    /// <para>
    /// The interceptor is scoped because it reads <see cref="Abstractions.IAuditContextAccessor"/>,
    /// which is per-request. This rules out <c>AddDbContextPool</c>, which builds its options once
    /// for the lifetime of the application and would pin a single accessor for every request.
    /// </para>
    /// </remarks>
    public static DbContextOptionsBuilder UseAuditableOperations(
        this DbContextOptionsBuilder builder,
        IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(serviceProvider);

        return builder.AddInterceptors(serviceProvider.GetRequiredService<AuditSaveChangesInterceptor>());
    }
}
