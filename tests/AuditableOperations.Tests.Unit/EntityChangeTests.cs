using AuditableOperations.Models;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

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
        record.EntityType.Should().Be(typeof(WorkOrder).FullName);
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
