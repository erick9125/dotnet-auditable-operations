using AuditableOperations.DependencyInjection;
using AuditableOperations.Models;
using AuditableOperations.Redaction;
using AuditableOperations.Sinks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AuditableOperations.Tests.Unit;

public sealed class DefaultValueFormatterTests
{
    private readonly DefaultValueFormatter _formatter = new();

    [Fact]
    public void Null_stays_null() => _formatter.Format(null).Should().BeNull();

    [Fact]
    public void Enums_are_written_as_their_name()
        => _formatter.Format(AuditAction.Updated).Should().Be("Updated");

    [Theory]
    [InlineData(42)]
    [InlineData(42L)]
    [InlineData(4.2)]
    [InlineData("text")]
    [InlineData(true)]
    [InlineData('c')]
    public void Scalars_pass_through_unchanged(object value)
        => _formatter.Format(value).Should().Be(value);

    [Fact]
    public void Decimals_and_temporal_types_pass_through_unchanged()
    {
        _formatter.Format(4.2m).Should().Be(4.2m);
        var moment = new DateTimeOffset(2026, 8, 7, 10, 0, 0, TimeSpan.Zero);
        _formatter.Format(moment).Should().Be(moment);
        _formatter.Format(TimeSpan.FromMinutes(3)).Should().Be(TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void Small_binary_values_become_base64()
    {
        byte[] payload = [1, 2, 3];
        _formatter.Format(payload).Should().Be(Convert.ToBase64String(payload));
    }

    [Fact]
    public void Large_binary_values_are_capped_so_a_blob_cannot_flood_the_trail()
    {
        var payload = new byte[DefaultValueFormatter.MaxBinaryLength * 4];
        var formatted = _formatter.Format(payload).Should().BeOfType<string>().Subject;

        formatted.Should().Contain(AuditFieldLimits.TruncationMarker);
        formatted.Should().Contain(payload.Length.ToString());
        formatted.Length.Should().BeLessThan(Convert.ToBase64String(payload).Length);
    }

    [Fact]
    public void Collections_are_projected_element_by_element()
    {
        var formatted = _formatter.Format(new[] { 1, 2, 3 })
            .Should().BeAssignableTo<List<object?>>().Subject;

        formatted.Should().BeEquivalentTo([1, 2, 3]);
    }

    [Fact]
    public void Nesting_stops_at_the_depth_limit_instead_of_recursing_forever()
    {
        object nested = new List<int> { 1 };
        for (var i = 0; i < DefaultValueFormatter.MaxCollectionDepth + 2; i++)
        {
            nested = new List<object> { nested };
        }

        var current = _formatter.Format(nested);
        var depth = 0;
        while (current is List<object?> { Count: > 0 } list)
        {
            current = list[0];
            depth++;
        }

        depth.Should().Be(DefaultValueFormatter.MaxCollectionDepth);
        current.Should().BeOfType<string>("the depth limit falls back to ToString");
    }

    [Fact]
    public void Unknown_complex_types_are_serialized_as_json()
        => _formatter.Format(new { Name = "x" }).Should().Be("""{"Name":"x"}""");
}

public sealed class OptionsValidationTests
{
    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void An_empty_redaction_placeholder_is_rejected(string? placeholder)
    {
        var act = () => Resolve(options => options.RedactedPlaceholder = placeholder!);

        act.Should().Throw<OptionsValidationException>()
            .WithMessage($"*{nameof(AuditableOperationsOptions.RedactedPlaceholder)}*");
    }

    [Fact]
    public void A_negative_owned_type_depth_is_rejected()
    {
        var act = () => Resolve(options => options.MaxOwnedTypeDepth = -1);

        act.Should().Throw<OptionsValidationException>()
            .WithMessage($"*{nameof(AuditableOperationsOptions.MaxOwnedTypeDepth)}*");
    }

    [Fact]
    public void An_undefined_sink_failure_behavior_is_rejected()
    {
        var act = () => Resolve(options => options.SinkFailureBehavior = (SinkFailureBehavior)99);

        act.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public void Defaults_are_valid()
    {
        var options = Resolve(_ => { });

        options.RedactedPlaceholder.Should().Be("***");
        options.SinkFailureBehavior.Should().Be(SinkFailureBehavior.LogAndContinue);
    }

    private static AuditableOperationsOptions Resolve(Action<AuditableOperationsOptions> configure)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuditableOperations(configure);

        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<AuditableOperationsOptions>>().Value;
    }
}

/// <summary>
/// Characterization tests for value comparison. EF's own <c>IsModified</c> already filters the
/// binary case below; these pin the behavior so a future change to the comparison path is visible.
/// </summary>
public sealed class ValueComparisonTests
{
    [Fact]
    public async Task Reassigning_a_binary_property_with_identical_content_is_not_a_change()
    {
        await using var harness = await TestHarness.CreateAsync();

        var order = new WorkOrder { Status = "Pending", Attachment = [1, 2, 3] };
        harness.Db.WorkOrders.Add(order);
        await harness.Db.SaveChangesAsync();
        harness.Sink.Clear();

        order.Attachment = [1, 2, 3]; // same content, different instance
        order.Status = "Approved";
        await harness.Db.SaveChangesAsync();

        var changes = harness.Sink.Records.Single().Changes;
        changes.Should().ContainSingle()
            .Which.Property.Should().Be(nameof(WorkOrder.Status));
    }

    [Fact]
    public async Task Genuinely_changing_a_binary_property_is_recorded()
    {
        await using var harness = await TestHarness.CreateAsync();

        var order = new WorkOrder { Status = "Pending", Attachment = [1, 2, 3] };
        harness.Db.WorkOrders.Add(order);
        await harness.Db.SaveChangesAsync();
        harness.Sink.Clear();

        order.Attachment = [9, 9, 9];
        await harness.Db.SaveChangesAsync();

        harness.Sink.Records.Single().Changes
            .Should().ContainSingle(x => x.Property == nameof(WorkOrder.Attachment));
    }
}

public sealed class StoredChangeFormatTests
{
    private static AuditRecord SampleRecord() => new()
    {
        Id = Guid.CreateVersion7(),
        Action = nameof(AuditAction.Updated),
        EntityType = "Sample",
        EntityId = "1",
        OccurredAt = DateTimeOffset.UtcNow,
        Changes =
        [
            new AuditPropertyChange
            {
                Property = "Status",
                PreviousValue = "Pending",
                CurrentValue = "Approved"
            }
        ]
    };

    [Fact]
    public void Changes_are_stored_as_camel_case_json_as_documented()
    {
        var json = AuditEntryEntity.FromRecord(SampleRecord()).ChangesJson;

        json.Should().Contain("\"property\"").And.Contain("\"previousValue\"");
        json.Should().Contain("\"currentValue\"").And.Contain("\"isRedacted\"");
        json.Should().NotContain("\"Property\"");
    }

    [Fact]
    public void A_stored_entry_round_trips_back_into_a_record()
    {
        var original = SampleRecord();

        var restored = AuditEntryEntity.FromRecord(original).ToRecord();

        restored.Id.Should().Be(original.Id);
        restored.Action.Should().Be(original.Action);
        restored.EntityId.Should().Be(original.EntityId);
        restored.Changes.Should().ContainSingle()
            .Which.Property.Should().Be("Status");

        // Values are declared as object, so they come back as JsonElement, not string.
        restored.Changes.Single().CurrentValue!.ToString().Should().Be("Approved");
    }
}
