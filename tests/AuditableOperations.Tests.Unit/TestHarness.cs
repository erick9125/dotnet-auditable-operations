using AuditableOperations.Abstractions;
using AuditableOperations.DependencyInjection;
using AuditableOperations.Models;
using AuditableOperations.Sinks;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AuditableOperations.Tests.Unit;

internal sealed class TestHarness : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    private TestHarness(
        SqliteConnection connection,
        ServiceProvider provider,
        TestDbContext db,
        InMemoryAuditSink sink,
        IHttpContextAccessor httpContextAccessor)
    {
        _connection = connection;
        Provider = provider;
        Db = db;
        Sink = sink;
        HttpContextAccessor = httpContextAccessor;
    }

    public ServiceProvider Provider { get; }

    public TestDbContext Db { get; }

    public InMemoryAuditSink Sink { get; }

    public IHttpContextAccessor HttpContextAccessor { get; }

    public static async Task<TestHarness> CreateAsync(
        Action<IServiceCollection>? configure = null,
        InMemoryAuditSink? sharedSink = null,
        bool registerDefaultSink = true)
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuditableOperations();
        services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();

        if (!registerDefaultSink)
        {
            // Concrete type only, so Sink still resolves while IAuditSink falls back to NullAuditSink.
            services.AddSingleton<InMemoryAuditSink>();
        }
        else if (sharedSink is null)
        {
            services.AddInMemoryAuditSink();
        }
        else
        {
            services.AddSingleton(sharedSink);
            services.AddSingleton<IAuditSink>(sharedSink);
        }

        configure?.Invoke(services);

        services.AddDbContext<TestDbContext>((sp, options) =>
        {
            options
                .UseSqlite(connection)
                .UseAuditableOperations(sp);
        });

        var provider = services.BuildServiceProvider();
        var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        await db.Database.EnsureCreatedAsync();

        var sink = scope.ServiceProvider.GetRequiredService<InMemoryAuditSink>();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();

        return new TestHarness(connection, provider, db, sink, accessor);
    }

    public async ValueTask DisposeAsync()
    {
        await Db.DisposeAsync();
        await Provider.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
