using LootSingles.Application.Import;
using LootSingles.Domain.Employees;
using LootSingles.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace LootSingles.Infrastructure.Persistence;

/// <summary>
/// EF Core DbContext for the Loot Singles fulfillment application.
/// Provides access to all domain entities and coordinates entity type configurations.
/// Persistence logic specific to a feature (e.g. import attempt/order mutation and duplicate-key
/// translation) belongs in a dedicated repository over this context (see
/// <see cref="ImportRepository"/>, <see cref="EmployeeRepository"/>, <see cref="DashboardRepository"/>),
/// not on the DbContext itself.
/// </summary>
public class LootSinglesDbContext : DbContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LootSinglesDbContext"/> class.
    /// </summary>
    /// <param name="options">The options to be used by the DbContext.</param>
    public LootSinglesDbContext(DbContextOptions<LootSinglesDbContext> options)
        : base(options) { }

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

    /// <summary>
    /// DbSet for Employee entities.
    /// </summary>
    public DbSet<Employee> Employees => Set<Employee>();

    /// <summary>
    /// DbSet for EmployeeAuditEvent entities.
    /// </summary>
    public DbSet<EmployeeAuditEvent> EmployeeAuditEvents => Set<EmployeeAuditEvent>();

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
