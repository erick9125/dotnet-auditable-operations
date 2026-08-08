using AuditableOperations.Attributes;
using Microsoft.EntityFrameworkCore;

namespace AuditableOperations.Tests.Unit;

internal sealed class TestDbContext : DbContext
{
    public TestDbContext(DbContextOptions<TestDbContext> options)
        : base(options)
    {
    }

    public DbSet<WorkOrder> WorkOrders => Set<WorkOrder>();

    public DbSet<CacheEntry> CacheEntries => Set<CacheEntry>();

    public DbSet<Invoice> Invoices => Set<Invoice>();

    public DbSet<LongKeyed> LongKeyed => Set<LongKeyed>();

    public DbSet<Ticket> Tickets => Set<Ticket>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Invoice>().OwnsOne(x => x.Address);
        modelBuilder.Entity<LongKeyed>().HasKey(x => new { x.Left, x.Right });
    }
}

[Audited]
internal sealed class Invoice
{
    public Guid Id { get; set; }

    public string Number { get; set; } = string.Empty;

    public BillingAddress Address { get; set; } = new();
}

internal sealed class BillingAddress
{
    public string City { get; set; } = string.Empty;

    [AuditRedact]
    public string Street { get; set; } = string.Empty;
}

/// <summary>Entity with a database-generated identity key, not a client-generated GUID.</summary>
[Audited]
internal sealed class Ticket
{
    public int Id { get; set; }

    public string Subject { get; set; } = string.Empty;
}

[Audited]
internal sealed class LongKeyed
{
    public string Left { get; set; } = string.Empty;

    public string Right { get; set; } = string.Empty;

    public string Payload { get; set; } = string.Empty;
}

[Audited]
internal sealed class WorkOrder
{
    public Guid Id { get; set; }

    public string Status { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    [AuditRedact]
    public string InternalComment { get; set; } = string.Empty;

    public byte[]? Attachment { get; set; }

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
