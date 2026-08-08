using System.Security.Claims;
using AuditableOperations.Abstractions;
using AuditableOperations.DependencyInjection;
using AuditableOperations.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AuditableOperations.Tests.Unit;

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

internal sealed class StaticAuditContextAccessor(AuditContext context) : IAuditContextAccessor
{
    public AuditContext GetCurrent() => context;
}
