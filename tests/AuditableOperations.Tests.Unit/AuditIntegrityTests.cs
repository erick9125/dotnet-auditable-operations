using AuditableOperations.Abstractions;
using AuditableOperations.Context;
using AuditableOperations.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace AuditableOperations.Tests.Unit;

/// <summary>
/// Regression tests for the P0 audit-integrity findings.
/// </summary>
public sealed class RedactionCannotBeBypassedTests
{
    /// <summary>
    /// No configuration may turn <c>[AuditRedact]</c> off, so every non-default combination of the
    /// remaining options must still redact. A global switch that bypassed the attribute previously
    /// wrote secrets to the audit store in clear text.
    /// </summary>
    [Fact]
    public async Task Explicit_AuditRedact_survives_every_non_default_option_combination()
    {
        await using var harness = await TestHarness.CreateAsync(configure: services =>
            services.Configure<AuditableOperationsOptions>(options =>
            {
                options.RequireAuditedAttribute = false;
                options.IgnoreConcurrencyTokens = false;
                options.IgnoreShadowProperties = false;
                options.CaptureUser = false;
                options.CaptureTenant = false;
                options.SinkFailureBehavior = SinkFailureBehavior.Throw;
            }));

        harness.Db.WorkOrders.Add(new WorkOrder
        {
            Status = "Pending",
            InternalComment = "SUPER-SECRET-TOKEN"
        });
        await harness.Db.SaveChangesAsync();

        var change = harness.Sink.Records
            .SelectMany(x => x.Changes)
            .Single(x => x.Property == nameof(WorkOrder.InternalComment));

        change.IsRedacted.Should().BeTrue();
        change.CurrentValue.Should().Be("***");
        harness.Sink.Records.Should().NotContain(record =>
            record.Changes.Any(c => Equals(c.CurrentValue, "SUPER-SECRET-TOKEN")));
    }

    [Fact]
    public async Task Redacted_placeholder_is_configurable()
    {
        await using var harness = await TestHarness.CreateAsync(configure: services =>
            services.Configure<AuditableOperationsOptions>(
                options => options.RedactedPlaceholder = "[REDACTED]"));

        harness.Db.WorkOrders.Add(new WorkOrder { Status = "Pending", InternalComment = "secret" });
        await harness.Db.SaveChangesAsync();

        harness.Sink.Records.Single()
            .Changes.Single(x => x.Property == nameof(WorkOrder.InternalComment))
            .CurrentValue.Should().Be("[REDACTED]");
    }
}

public sealed class FieldLengthTests
{
    [Fact]
    public void Source_and_correlation_id_are_truncated_to_the_persisted_column_width()
    {
        var httpContext = new DefaultHttpContext
        {
            TraceIdentifier = new string('c', 400)
        };
        httpContext.Request.Method = "POST";
        httpContext.Request.Path = "/api/" + new string('x', 900);

        var accessor = new HttpAuditContextAccessor(
            new HttpContextAccessor { HttpContext = httpContext },
            Options.Create(new AuditableOperationsOptions()));

        var context = accessor.GetCurrent();

        context.Source!.Length.Should().BeLessThanOrEqualTo(AuditFieldLimits.Source);
        context.CorrelationId!.Length.Should().BeLessThanOrEqualTo(AuditFieldLimits.CorrelationId);
    }

    [Fact]
    public async Task Oversized_context_values_are_truncated_before_reaching_the_sink()
    {
        await using var harness = await TestHarness.CreateAsync(configure: services =>
        {
            services.RemoveAll<IAuditContextAccessor>();
            services.AddSingleton<IAuditContextAccessor>(new StaticAuditContextAccessor(new AuditContext
            {
                UserId = new string('u', 500),
                TenantId = new string('t', 500),
                CorrelationId = new string('c', 500),
                Source = new string('s', 2000)
            }));
        });

        harness.Db.WorkOrders.Add(new WorkOrder { Status = "Pending" });
        await harness.Db.SaveChangesAsync();

        var record = harness.Sink.Records.Single();
        record.UserId!.Length.Should().BeLessThanOrEqualTo(AuditFieldLimits.UserId);
        record.TenantId!.Length.Should().BeLessThanOrEqualTo(AuditFieldLimits.TenantId);
        record.CorrelationId!.Length.Should().BeLessThanOrEqualTo(AuditFieldLimits.CorrelationId);
        record.Source!.Length.Should().BeLessThanOrEqualTo(AuditFieldLimits.Source);
    }

    [Fact]
    public async Task Oversized_composite_entity_id_is_truncated_before_reaching_the_sink()
    {
        await using var harness = await TestHarness.CreateAsync();

        harness.Db.LongKeyed.Add(new LongKeyed
        {
            Left = new string('a', 100),
            Right = new string('b', 100),
            Payload = "x"
        });
        await harness.Db.SaveChangesAsync();

        var record = harness.Sink.Records.Single();
        record.EntityId.Should().NotBeEmpty();
        record.EntityId.Length.Should().BeLessThanOrEqualTo(AuditFieldLimits.EntityId);
    }
}

public sealed class OwnedTypeAuditTests
{
    [Fact]
    public async Task Changing_only_an_owned_type_property_still_produces_an_audit_record()
    {
        await using var harness = await TestHarness.CreateAsync();

        var invoice = new Invoice
        {
            Number = "INV-1",
            Address = new BillingAddress { City = "Santiago", Street = "secret-street" }
        };
        harness.Db.Invoices.Add(invoice);
        await harness.Db.SaveChangesAsync();
        harness.Sink.Clear();

        invoice.Address.City = "Valparaiso";
        await harness.Db.SaveChangesAsync();

        var record = harness.Sink.Records.Should().ContainSingle().Subject;
        record.Action.Should().Be(nameof(AuditAction.Updated));
        record.EntityId.Should().Be(invoice.Id.ToString());
        record.Changes.Should().ContainSingle();
        record.Changes.Single().Property.Should().Be($"{nameof(Invoice.Address)}.{nameof(BillingAddress.City)}");
        record.Changes.Single().PreviousValue.Should().Be("Santiago");
        record.Changes.Single().CurrentValue.Should().Be("Valparaiso");
    }

    [Fact]
    public async Task Owned_type_properties_are_captured_on_create_and_respect_redaction()
    {
        await using var harness = await TestHarness.CreateAsync();

        harness.Db.Invoices.Add(new Invoice
        {
            Number = "INV-2",
            Address = new BillingAddress { City = "Santiago", Street = "secret-street" }
        });
        await harness.Db.SaveChangesAsync();

        var changes = harness.Sink.Records.Single().Changes;
        changes.Should().Contain(x =>
            x.Property == "Address.City" && Equals(x.CurrentValue, "Santiago"));
        changes.Should().Contain(x =>
            x.Property == "Address.Street" && x.IsRedacted && Equals(x.CurrentValue, "***"));
        changes.Should().NotContain(x => Equals(x.CurrentValue, "secret-street"));
    }

    [Fact]
    public async Task Owned_entries_do_not_produce_standalone_records()
    {
        await using var harness = await TestHarness.CreateAsync(configure: services =>
            services.Configure<AuditableOperationsOptions>(
                options => options.RequireAuditedAttribute = false));

        harness.Db.Invoices.Add(new Invoice
        {
            Number = "INV-3",
            Address = new BillingAddress { City = "Santiago" }
        });
        await harness.Db.SaveChangesAsync();

        harness.Sink.Records
            .Should().OnlyContain(x => x.EntityType.EndsWith(nameof(Invoice), StringComparison.Ordinal));
    }
}

public sealed class SinkFailureBehaviorTests
{
    [Fact]
    public async Task Sink_failure_does_not_break_the_business_operation_by_default()
    {
        await using var harness = await TestHarness.CreateAsync(configure: services =>
        {
            services.RemoveAll<IAuditSink>();
            services.AddSingleton<IAuditSink>(new ThrowingAuditSink());
        });

        harness.Db.WorkOrders.Add(new WorkOrder { Status = "Pending" });

        var act = async () => await harness.Db.SaveChangesAsync();

        await act.Should().NotThrowAsync();
        (await harness.Db.WorkOrders.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Sink_failure_propagates_when_configured_to_throw()
    {
        await using var harness = await TestHarness.CreateAsync(configure: services =>
        {
            services.RemoveAll<IAuditSink>();
            services.AddSingleton<IAuditSink>(new ThrowingAuditSink());
            services.Configure<AuditableOperationsOptions>(
                options => options.SinkFailureBehavior = SinkFailureBehavior.Throw);
        });

        harness.Db.WorkOrders.Add(new WorkOrder { Status = "Pending" });

        var act = async () => await harness.Db.SaveChangesAsync();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("sink is down");
    }
}

internal sealed class ThrowingAuditSink : IAuditSink
{
    public Task WriteAsync(
        IReadOnlyCollection<AuditRecord> records,
        CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("sink is down");
}
