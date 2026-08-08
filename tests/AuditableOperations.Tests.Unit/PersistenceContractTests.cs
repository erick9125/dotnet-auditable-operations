using AuditableOperations.Abstractions;
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
using Microsoft.Extensions.Logging;

namespace AuditableOperations.Tests.Unit;

/// <summary>
/// Locks in the persistence guarantees the README advertises but that no test covered.
/// </summary>
public sealed class GeneratedKeyTests
{
    [Fact]
    public async Task Database_generated_key_is_resolved_after_save_not_captured_as_zero()
    {
        await using var harness = await TestHarness.CreateAsync();

        var ticket = new Ticket { Subject = "printer jam" };
        harness.Db.Tickets.Add(ticket);
        await harness.Db.SaveChangesAsync();

        ticket.Id.Should().NotBe(0, "EF must have assigned the identity value");
        harness.Sink.Records.Single().EntityId.Should().Be(ticket.Id.ToString());
    }

    [Fact]
    public async Task Composite_key_is_reported_as_the_joined_key_values()
    {
        await using var harness = await TestHarness.CreateAsync();

        harness.Db.LongKeyed.Add(new LongKeyed { Left = "left", Right = "right", Payload = "x" });
        await harness.Db.SaveChangesAsync();

        harness.Sink.Records.Single().EntityId.Should().Be("left|right");
    }

    [Fact]
    public async Task Entity_type_is_the_full_clr_type_name_so_same_named_types_do_not_collide()
    {
        await using var harness = await TestHarness.CreateAsync();

        harness.Db.WorkOrders.Add(new WorkOrder { Status = "Pending" });
        await harness.Db.SaveChangesAsync();

        harness.Sink.Records.Single().EntityType
            .Should().Be(typeof(WorkOrder).FullName);
    }
}

public sealed class SynchronousSaveChangesTests
{
    [Fact]
    public async Task Synchronous_SaveChanges_still_writes_audit_records()
    {
        await using var harness = await TestHarness.CreateAsync();

        var ticket = new Ticket { Subject = "sync path" };
        harness.Db.Tickets.Add(ticket);
        harness.Db.SaveChanges();

        var record = harness.Sink.Records.Should().ContainSingle().Subject;
        record.Action.Should().Be(nameof(AuditAction.Created));
        record.EntityId.Should().Be(ticket.Id.ToString());
    }

    [Fact]
    public async Task Each_SaveChanges_overload_uses_the_matching_sink_path()
    {
        var sink = new PathRecordingAuditSink();
        await using var harness = await TestHarness.CreateAsync(configure: services =>
        {
            services.RemoveAll<IAuditSink>();
            services.AddSingleton<IAuditSink>(sink);
        });

        harness.Db.Tickets.Add(new Ticket { Subject = "sync" });
        harness.Db.SaveChanges();

        sink.SyncCalls.Should().Be(1, "SaveChanges must not block on the async path");
        sink.AsyncCalls.Should().Be(0);

        harness.Db.Tickets.Add(new Ticket { Subject = "async" });
        await harness.Db.SaveChangesAsync();

        sink.SyncCalls.Should().Be(1);
        sink.AsyncCalls.Should().Be(1);
    }
}

public sealed class DefaultSinkTests
{
    [Fact]
    public async Task Forgetting_to_register_a_sink_warns_instead_of_failing_the_save()
    {
        var logs = new List<string>();

        await using var harness = await TestHarness.CreateAsync(
            registerDefaultSink: false,
            configure: services => services.AddSingleton<ILoggerProvider>(new ListLoggerProvider(logs)));

        harness.Db.WorkOrders.Add(new WorkOrder { Status = "Pending" });

        var act = async () => await harness.Db.SaveChangesAsync();

        await act.Should().NotThrowAsync();
        (await harness.Db.WorkOrders.CountAsync()).Should().Be(1);
        logs.Should().ContainSingle(x => x.Contains("No IAuditSink is registered", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_warning_is_emitted_only_once()
    {
        var logs = new List<string>();

        await using var harness = await TestHarness.CreateAsync(
            registerDefaultSink: false,
            configure: services => services.AddSingleton<ILoggerProvider>(new ListLoggerProvider(logs)));

        for (var i = 0; i < 3; i++)
        {
            harness.Db.WorkOrders.Add(new WorkOrder { Status = $"s{i}" });
            await harness.Db.SaveChangesAsync();
        }

        logs.Count(x => x.Contains("No IAuditSink is registered", StringComparison.Ordinal)).Should().Be(1);
    }
}

internal sealed class PathRecordingAuditSink : IAuditSink
{
    public int SyncCalls { get; private set; }

    public int AsyncCalls { get; private set; }

    public Task WriteAsync(IReadOnlyCollection<AuditRecord> records, CancellationToken cancellationToken = default)
    {
        AsyncCalls++;
        return Task.CompletedTask;
    }

    public void Write(IReadOnlyCollection<AuditRecord> records)
    {
        SyncCalls++;
    }
}

internal sealed class ListLoggerProvider(List<string> messages) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new ListLogger(messages);

    public void Dispose()
    {
    }

    private sealed class ListLogger(List<string> messages) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            lock (messages)
            {
                messages.Add(formatter(state, exception));
            }
        }
    }
}

/// <summary>
/// Two concurrent requests must not borrow each other's audit context. The previous version of this
/// test built a separate DI container per request, so it exercised nothing.
/// </summary>
public sealed class ConcurrentRequestIsolationTests
{
    [Fact]
    public async Task Concurrent_scopes_sharing_one_provider_keep_their_own_request_context()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuditableOperations();
        services.AddInMemoryAuditSink();
        services.AddHttpAuditContext();

        // One connection per scope: SQLite cannot serve two concurrent commands, and the point of
        // this test is the shared container, accessor and sink — not shared storage.
        services.AddScoped(_ =>
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            connection.Open();
            return connection;
        });
        // Wired with raw AddInterceptors on purpose: UseAuditableOperations is covered by TestHarness,
        // so both supported wirings stay under test.
        services.AddDbContext<TestDbContext>((sp, options) => options
            .UseSqlite(sp.GetRequiredService<SqliteConnection>())
            .AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>()));

        await using var provider = services.BuildServiceProvider(validateScopes: true);

        var accessor = provider.GetRequiredService<IHttpContextAccessor>();
        var barrier = new Barrier(2);

        async Task SimulateRequestAsync(string user, string status)
        {
            await Task.Yield();

            var httpContext = new DefaultHttpContext { TraceIdentifier = $"corr-{user}" };
            httpContext.Request.Method = "POST";
            httpContext.Request.Path = $"/api/{user}";
            httpContext.User = new System.Security.Claims.ClaimsPrincipal(
                new System.Security.Claims.ClaimsIdentity([new System.Security.Claims.Claim("sub", user)], "Test"));
            accessor.HttpContext = httpContext;

            using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            await db.Database.EnsureCreatedAsync();
            db.WorkOrders.Add(new WorkOrder { Status = status });

            // Force both requests to interleave around the capture/persist boundary.
            barrier.SignalAndWait();
            await db.SaveChangesAsync();
        }

        await Task.WhenAll(
            SimulateRequestAsync("alice", "A"),
            SimulateRequestAsync("bob", "B"));

        var records = provider.GetRequiredService<InMemoryAuditSink>().Records;
        records.Should().HaveCount(2);

        records.Should().ContainSingle(x =>
            x.UserId == "alice"
            && x.CorrelationId == "corr-alice"
            && x.Source == "POST /api/alice"
            && x.Changes.Any(c => Equals(c.CurrentValue, "A")));

        records.Should().ContainSingle(x =>
            x.UserId == "bob"
            && x.CorrelationId == "corr-bob"
            && x.Source == "POST /api/bob"
            && x.Changes.Any(c => Equals(c.CurrentValue, "B")));
    }
}
