using AuditableOperations;
using AuditableOperations.Abstractions;
using AuditableOperations.DependencyInjection;
using AuditableOperations.Models;
using AuditableOperations.Sinks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;

namespace AuditableOperations.Tests.Integration;

public sealed class PostgresAuditSinkTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task Database_sink_persists_redacted_audit_records()
    {
        var appCs = _postgres.GetConnectionString() + ";Database=app_db";
        var auditCs = _postgres.GetConnectionString() + ";Database=audit_db";

        await EnsureDatabaseAsync(appCs);
        await EnsureDatabaseAsync(auditCs);

        await using var provider = BuildProvider(appCs, auditCs);
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IntegrationDbContext>();
        var auditDb = scope.ServiceProvider.GetRequiredService<AuditDbContext>();

        await db.Database.EnsureCreatedAsync();
        await auditDb.Database.EnsureCreatedAsync();

        var order = new IntegrationOrder
        {
            Status = "Pending",
            InternalNote = "top-secret"
        };

        db.Orders.Add(order);
        await db.SaveChangesAsync();

        var created = await auditDb.AuditEntries.SingleAsync();
        created.Action.Should().Be(nameof(AuditAction.Created));
        created.EntityId.Should().Be(order.Id.ToString());
        created.ChangesJson.Should().Contain("***");
        created.ChangesJson.Should().NotContain("top-secret");

        order.Status = "Approved";
        await db.SaveChangesAsync();

        var entries = await auditDb.AuditEntries.OrderBy(x => x.OccurredAt).ToListAsync();
        entries.Should().HaveCount(2);
        entries[1].Action.Should().Be(nameof(AuditAction.Updated));
        entries[1].ChangesJson.Should().Contain("Pending");
        entries[1].ChangesJson.Should().Contain("Approved");
    }

    [Fact]
    public async Task Failed_business_save_does_not_write_audit()
    {
        var appCs = _postgres.GetConnectionString() + ";Database=app_fail_db";
        var auditCs = _postgres.GetConnectionString() + ";Database=audit_fail_db";

        await EnsureDatabaseAsync(appCs);
        await EnsureDatabaseAsync(auditCs);

        await using var provider = BuildProvider(appCs, auditCs);
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IntegrationDbContext>();
        var auditDb = scope.ServiceProvider.GetRequiredService<AuditDbContext>();

        await db.Database.EnsureCreatedAsync();
        await auditDb.Database.EnsureCreatedAsync();

        db.Orders.Add(new IntegrationOrder
        {
            Status = new string('x', 5000),
            InternalNote = "note"
        });

        var act = async () => await db.SaveChangesAsync();
        await act.Should().ThrowAsync<Exception>();

        (await auditDb.AuditEntries.CountAsync()).Should().Be(0);
    }

    /// <summary>
    /// The request path is caller-controlled and the column is <c>varchar(512)</c>, so an oversized
    /// value used to fail the insert with the business row already committed.
    /// </summary>
    [Fact]
    public async Task Oversized_context_values_do_not_fail_the_insert_against_real_columns()
    {
        var appCs = _postgres.GetConnectionString() + ";Database=app_long_db";
        var auditCs = _postgres.GetConnectionString() + ";Database=audit_long_db";

        await EnsureDatabaseAsync(appCs);
        await EnsureDatabaseAsync(auditCs);

        await using var provider = BuildProvider(appCs, auditCs, new AuditContext
        {
            UserId = new string('u', 400),
            TenantId = new string('t', 400),
            CorrelationId = new string('c', 400),
            Source = "GET /api/" + new string('x', 2000)
        });

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IntegrationDbContext>();
        var auditDb = scope.ServiceProvider.GetRequiredService<AuditDbContext>();

        await db.Database.EnsureCreatedAsync();
        await auditDb.Database.EnsureCreatedAsync();

        db.Orders.Add(new IntegrationOrder { Status = "Pending", InternalNote = "note" });

        var act = async () => await db.SaveChangesAsync();
        await act.Should().NotThrowAsync();

        var entry = await auditDb.AuditEntries.SingleAsync();
        entry.Source!.Length.Should().BeLessThanOrEqualTo(AuditFieldLimits.Source);
        entry.UserId!.Length.Should().BeLessThanOrEqualTo(AuditFieldLimits.UserId);
        entry.TenantId!.Length.Should().BeLessThanOrEqualTo(AuditFieldLimits.TenantId);
        entry.CorrelationId!.Length.Should().BeLessThanOrEqualTo(AuditFieldLimits.CorrelationId);
        entry.Source.Should().EndWith(AuditFieldLimits.TruncationMarker);
    }

    private static ServiceProvider BuildProvider(
        string appCs,
        string auditCs,
        AuditContext? auditContext = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuditableOperations();
        services.AddDatabaseAuditSink(options => options.UseNpgsql(auditCs));

        if (auditContext is not null)
        {
            services.RemoveAll<IAuditContextAccessor>();
            services.AddSingleton<IAuditContextAccessor>(new StaticAuditContextAccessor(auditContext));
        }

        services.AddDbContext<IntegrationDbContext>((sp, options) =>
        {
            options
                .UseNpgsql(appCs)
                .UseAuditableOperations(sp);
        });

        return services.BuildServiceProvider(validateScopes: true);
    }

    private static async Task EnsureDatabaseAsync(string connectionString)
    {
        var builder = new Npgsql.NpgsqlConnectionStringBuilder(connectionString);
        var database = builder.Database;
        builder.Database = "postgres";

        await using var connection = new Npgsql.NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE \"{database}\"";
        try
        {
            await command.ExecuteNonQueryAsync();
        }
        catch (Npgsql.PostgresException ex) when (ex.SqlState == "42P04")
        {
        }
    }
}

internal sealed class StaticAuditContextAccessor(AuditContext context) : IAuditContextAccessor
{
    public AuditContext GetCurrent() => context;
}

[AuditableOperations.Attributes.Audited]
internal sealed class IntegrationOrder
{
    public Guid Id { get; set; }

    public string Status { get; set; } = string.Empty;

    [AuditableOperations.Attributes.AuditRedact]
    public string InternalNote { get; set; } = string.Empty;
}

internal sealed class IntegrationDbContext : DbContext
{
    public IntegrationDbContext(DbContextOptions<IntegrationDbContext> options)
        : base(options)
    {
    }

    public DbSet<IntegrationOrder> Orders => Set<IntegrationOrder>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<IntegrationOrder>(entity =>
        {
            entity.Property(x => x.Status).HasMaxLength(64);
        });
    }
}
