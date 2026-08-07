using System.Security.Claims;
using AuditableOperations.Attributes;
using AuditableOperations.DependencyInjection;
using AuditableOperations.EntityFramework;
using AuditableOperations.Sinks;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var appConnection = builder.Configuration.GetConnectionString("App")
    ?? "Host=localhost;Port=5432;Database=auditable_ops_app;Username=postgres;Password=postgres";
var auditConnection = builder.Configuration.GetConnectionString("Audit")
    ?? "Host=localhost;Port=5432;Database=auditable_ops_audit;Username=postgres;Password=postgres";

builder.Services.AddAuditableOperations(options =>
{
    options.EnableEntityChanges = true;
    options.CaptureUser = true;
    options.CaptureTenant = true;
    options.RedactSensitiveValues = true;
});

builder.Services.AddHttpAuditContext();
builder.Services.AddDatabaseAuditSink(options => options.UseNpgsql(auditConnection));

builder.Services.AddDbContext<AppDbContext>((sp, options) =>
{
    options
        .UseNpgsql(appConnection)
        .AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>());
});

builder.Services.AddOpenApi();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var appDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await appDb.Database.EnsureCreatedAsync();

    var auditDb = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
    await auditDb.Database.EnsureCreatedAsync();
}

app.Use(async (context, next) =>
{
    if (context.User.Identity?.IsAuthenticated != true)
    {
        var identity = new ClaimsIdentity(
        [
            new Claim("sub", "user-42"),
            new Claim("tenant_id", "company-7")
        ], "Demo");
        context.User = new ClaimsPrincipal(identity);
    }

    await next();
});

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapPost("/orders", async (CreateOrderRequest request, AppDbContext db) =>
{
    var order = new Order
    {
        Status = request.Status,
        Total = request.Total,
        InternalNote = request.InternalNote
    };

    db.Orders.Add(order);
    await db.SaveChangesAsync();
    return Results.Created($"/orders/{order.Id}", order);
});

app.MapPut("/orders/{id:guid}", async (Guid id, UpdateOrderRequest request, AppDbContext db) =>
{
    var order = await db.Orders.FindAsync(id);
    if (order is null)
    {
        return Results.NotFound();
    }

    order.Status = request.Status;
    order.Total = request.Total;
    order.InternalNote = request.InternalNote;
    await db.SaveChangesAsync();
    return Results.Ok(order);
});

app.MapDelete("/orders/{id:guid}", async (Guid id, AppDbContext db) =>
{
    var order = await db.Orders.FindAsync(id);
    if (order is null)
    {
        return Results.NotFound();
    }

    db.Orders.Remove(order);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

app.MapGet("/orders/{id:guid}", async (Guid id, AppDbContext db) =>
{
    var order = await db.Orders.FindAsync(id);
    return order is null ? Results.NotFound() : Results.Ok(order);
});

app.MapGet("/audit", async (AuditDbContext auditDb) =>
{
    var entries = await auditDb.AuditEntries
        .OrderByDescending(x => x.OccurredAt)
        .Take(50)
        .ToListAsync();

    return Results.Ok(entries.Select(x => x.ToRecord()));
});

app.Run();

public partial class Program;

[Audited]
public sealed class Order
{
    public Guid Id { get; set; }

    public string Status { get; set; } = string.Empty;

    public decimal Total { get; set; }

    [AuditRedact]
    public string InternalNote { get; set; } = string.Empty;

    [AuditIgnore]
    public DateTime CacheUpdatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<Order> Orders => Set<Order>();
}

public sealed record CreateOrderRequest(string Status, decimal Total, string InternalNote);

public sealed record UpdateOrderRequest(string Status, decimal Total, string InternalNote);
