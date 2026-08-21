using Microsoft.EntityFrameworkCore;
using LootSingles.Domain.Orders;
using LootSingles.Application.Import;

namespace LootSingles.Infrastructure.Persistence;

/// <summary>
/// EF Core DbContext for the Loot Singles fulfillment application.
/// Provides access to all domain entities and coordinates entity type configurations.
/// </summary>
public class LootSinglesDbContext : DbContext, IImportPersistence
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LootSinglesDbContext"/> class.
    /// </summary>
    /// <param name="options">The options to be used by the DbContext.</param>
    public LootSinglesDbContext(DbContextOptions<LootSinglesDbContext> options) : base(options) { }

    /// <summary>
    /// DbSet for Order entities.
    /// </summary>
    public DbSet<Order> Orders => Set<Order>();

    /// <summary>
    /// DbSet for OrderLine entities.
    /// </summary>
    public DbSet<OrderLine> OrderLines => Set<OrderLine>();

    /// <summary>
    /// DbSet for ImportAttempt entities.
    /// </summary>
    public DbSet<ImportAttempt> ImportAttempts => Set<ImportAttempt>();

    /// <summary>
    /// DbSet for ImportOrderResult entities.
    /// </summary>
    public DbSet<ImportOrderResult> ImportOrderResults => Set<ImportOrderResult>();

    public void AddImportAttempt(ImportAttempt attempt) => ImportAttempts.Add(attempt);

    public void AddOrder(Order order) => Orders.Add(order);

    public void DiscardOrder(Order order)
    {
        foreach (var line in order.OrderLines.ToList()) Entry(line).State = EntityState.Detached;
        Entry(order).State = EntityState.Detached;
    }

    public Task<bool> OrderExistsAsync(string tcgplayerOrderId, CancellationToken cancellationToken) =>
        Orders.AnyAsync(order => order.TcgplayerOrderId == tcgplayerOrderId, cancellationToken);

    async Task IImportPersistence.SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            var details = exception.ToString();
            var duplicate = details.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase)
                || details.Contains("duplicate", StringComparison.OrdinalIgnoreCase)
                || details.Contains("IX_Orders_TcgplayerOrderId", StringComparison.OrdinalIgnoreCase);
            throw new OrderPersistenceException(
                duplicate ? "The order identifier already exists." : "The order could not be persisted atomically.",
                duplicate,
                exception);
        }
    }

    /// <summary>
    /// Configures the model using the Fluent API.
    /// Applies entity type configurations from the assembly automatically.
    /// </summary>
    /// <param name="modelBuilder">The model builder.</param>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(LootSinglesDbContext).Assembly);
    }
}
