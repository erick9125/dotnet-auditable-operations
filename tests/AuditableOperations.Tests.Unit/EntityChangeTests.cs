using System.Security.Claims;
using AuditableOperations.Abstractions;
using AuditableOperations.Attributes;
using AuditableOperations.DependencyInjection;
using AuditableOperations.EntityFramework;
using AuditableOperations.Models;
using AuditableOperations.Sinks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AuditableOperations.Tests.Unit;

public sealed class EntityChangeTests
{
    [Fact]
    public async Task Created_captures_entity_with_generated_id_and_ignores_ignored_properties()
    {
        await using var harness = await TestHarness.CreateAsync();

        var entity = new WorkOrder
        {
            Status = "Pending",
            InternalComment = "secret-note",
            CacheUpdatedAt = DateTime.UtcNow
        };

        harness.Db.WorkOrders.Add(entity);
        await harness.Db.SaveChangesAsync();

        harness.Sink.Records.Should().ContainSingle();
        var record = harness.Sink.Records.Single();
        record.Action.Should().Be(nameof(AuditAction.Created));
        record.EntityType.Should().Be(nameof(WorkOrder));
        record.EntityId.Should().Be(entity.Id.ToString());
        record.Changes.Should().NotContain(x => x.Property == nameof(WorkOrder.CacheUpdatedAt));
        record.Changes.Should().Contain(x => x.Property == nameof(WorkOrder.Status) && Equals(x.CurrentValue, "Pending"));
        record.Changes.Should().Contain(x =>
            x.Property == nameof(WorkOrder.InternalComment)
            && x.IsRedacted
            && Equals(x.CurrentValue, "***"));
    }

    [Fact]
    public async Task Modified_records_only_changed_properties_with_old_and_new_values()
    {
        await using var harness = await TestHarness.CreateAsync();

        var entity = new WorkOrder
        {
            Status = "Pending",
            InternalComment = "before-secret",
            Title = "Original"
        };

        harness.Db.WorkOrders.Add(entity);
        await harness.Db.SaveChangesAsync();
        harness.Sink.Clear();

        entity.Status = "Approved";
        entity.InternalComment = "after-secret";
        await harness.Db.SaveChangesAsync();

        harness.Sink.Records.Should().ContainSingle();
        var record = harness.Sink.Records.Single();
        record.Action.Should().Be(nameof(AuditAction.Updated));
        record.Changes.Should().HaveCount(2);
        record.Changes.Should().Contain(x =>
            x.Property == nameof(WorkOrder.Status)
            && Equals(x.PreviousValue, "Pending")
            && Equals(x.CurrentValue, "Approved")
            && !x.IsRedacted);
        record.Changes.Should().Contain(x =>
            x.Property == nameof(WorkOrder.InternalComment)
            && x.IsRedacted
            && Equals(x.PreviousValue, "***")
            && Equals(x.CurrentValue, "***"));
        record.Changes.Should().NotContain(x => x.Property == nameof(WorkOrder.Title));
    }

    [Fact]
    public async Task Deleted_captures_entity_identity_and_previous_values()
    {
        await using var harness = await TestHarness.CreateAsync();

        var entity = new WorkOrder
        {
            Status = "Pending",
            InternalComment = "delete-secret"
        };

        harness.Db.WorkOrders.Add(entity);
        await harness.Db.SaveChangesAsync();
        var id = entity.Id;
        harness.Sink.Clear();

        harness.Db.WorkOrders.Remove(entity);
        await harness.Db.SaveChangesAsync();

        harness.Sink.Records.Should().ContainSingle();
        var record = harness.Sink.Records.Single();
        record.Action.Should().Be(nameof(AuditAction.Deleted));
        record.EntityId.Should().Be(id.ToString());
        record.Changes.Should().Contain(x =>
            x.Property == nameof(WorkOrder.InternalComment)
            && x.IsRedacted
            && Equals(x.PreviousValue, "***"));
    }

    [Fact]
    public async Task Unaffected_entity_without_audited_attribute_is_ignored()
    {
        await using var harness = await TestHarness.CreateAsync();

        harness.Db.CacheEntries.Add(new CacheEntry { Key = "k", Value = "v" });
        await harness.Db.SaveChangesAsync();

        harness.Sink.Records.Should().BeEmpty();
    }
}

public sealed class AuditContextTests
{
    [Fact]
    public async Task Http_context_captures_user_tenant_correlation_and_source()
    {
        await using var harness = await TestHarness.CreateAsync(configure: services =>
        {
            services.RemoveAll<IAuditContextAccessor>();
            services.AddHttpAuditContext();
        });

        var httpContext = new DefaultHttpContext();
        httpContext.TraceIdentifier = "corr-123";
        httpContext.Request.Method = "PUT";
        httpContext.Request.Path = "/api/work-orders/1";
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("sub", "user-42"),
            new Claim("tenant_id", "company-7")
        ], "Test"));

        harness.HttpContextAccessor.HttpContext = httpContext;

        harness.Db.WorkOrders.Add(new WorkOrder { Status = "Pending" });
        await harness.Db.SaveChangesAsync();

        var record = harness.Sink.Records.Single();
        record.UserId.Should().Be("user-42");
        record.TenantId.Should().Be("company-7");
        record.CorrelationId.Should().Be("corr-123");
        record.Source.Should().Be("PUT /api/work-orders/1");
    }

    [Fact]
    public async Task Custom_accessor_works_without_http()
    {
        await using var harness = await TestHarness.CreateAsync(configure: services =>
        {
            services.RemoveAll<IAuditContextAccessor>();
            services.AddSingleton<IAuditContextAccessor>(new StaticAuditContextAccessor(new AuditContext
            {
                UserId = "worker-1",
                TenantId = "tenant-9",
                CorrelationId = "job-77",
                Source = "background-job"
            }));
        });

        harness.Db.WorkOrders.Add(new WorkOrder { Status = "Queued" });
        await harness.Db.SaveChangesAsync();

        var record = harness.Sink.Records.Single();
        record.UserId.Should().Be("worker-1");
        record.TenantId.Should().Be("tenant-9");
        record.CorrelationId.Should().Be("job-77");
        record.Source.Should().Be("background-job");
    }
}

public sealed class ConcurrencyTests
{
    [Fact]
    public async Task Concurrent_saves_keep_correlation_ids_isolated()
    {
        var sink = new InMemoryAuditSink();

        async Task RunAsync(string correlationId, string status)
        {
            await using var harness = await TestHarness.CreateAsync(
                sharedSink: sink,
                configure: services =>
                {
                    services.RemoveAll<IAuditContextAccessor>();
                    services.AddSingleton<IAuditContextAccessor>(new StaticAuditContextAccessor(new AuditContext
                    {
                        CorrelationId = correlationId,
                        Source = "test"
                    }));
                });

            harness.Db.WorkOrders.Add(new WorkOrder { Status = status });
            await harness.Db.SaveChangesAsync();
        }

        await Task.WhenAll(
            RunAsync("corr-a", "A"),
            RunAsync("corr-b", "B"));

        sink.Records.Should().HaveCount(2);
        sink.Records.Select(x => x.CorrelationId).Should().BeEquivalentTo(["corr-a", "corr-b"]);
        sink.Records.Should().Contain(x => x.CorrelationId == "corr-a" && x.Changes.Any(c => Equals(c.CurrentValue, "A")));
        sink.Records.Should().Contain(x => x.CorrelationId == "corr-b" && x.Changes.Any(c => Equals(c.CurrentValue, "B")));
    }
}

internal sealed class StaticAuditContextAccessor(AuditContext context) : IAuditContextAccessor
{
    public AuditContext GetCurrent() => context;
}

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
        InMemoryAuditSink? sharedSink = null)
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuditableOperations();
        services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();

        if (sharedSink is null)
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
                .AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>());
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

internal sealed class TestDbContext : DbContext
{
    public TestDbContext(DbContextOptions<TestDbContext> options)
        : base(options)
    {
    }

    public DbSet<WorkOrder> WorkOrders => Set<WorkOrder>();

    public DbSet<CacheEntry> CacheEntries => Set<CacheEntry>();
}

[Audited]
internal sealed class WorkOrder
{
    public Guid Id { get; set; }

    public string Status { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    [AuditRedact]
    public string InternalComment { get; set; } = string.Empty;

    [AuditIgnore]
    public DateTime CacheUpdatedAt { get; set; }
}

[AuditIgnore]
internal sealed class CacheEntry
{
    public int Id { get; set; }

    public string Key { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;
}
