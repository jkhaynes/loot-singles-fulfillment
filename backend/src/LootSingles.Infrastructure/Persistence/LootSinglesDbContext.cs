using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using LootSingles.Domain.Orders;
using LootSingles.Domain.Employees;
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

    /// <summary>
    /// DbSet for Employee entities.
    /// </summary>
    public DbSet<Employee> Employees => Set<Employee>();

    /// <summary>
    /// DbSet for EmployeeAuditEvent entities.
    /// </summary>
    public DbSet<EmployeeAuditEvent> EmployeeAuditEvents => Set<EmployeeAuditEvent>();

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
            var duplicate = IsDuplicateKeyViolation(exception);
            throw new OrderPersistenceException(
                duplicate ? "The order identifier already exists." : "The order could not be persisted atomically.",
                duplicate,
                exception);
        }
    }

    private static bool IsDuplicateKeyViolation(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException sqlException
                && sqlException.Errors.Cast<SqlError>().Any(error => error.Number is 2601 or 2627))
            {
                return true;
            }

            if (current is DbException dbException
                && current.GetType().FullName == "Microsoft.Data.Sqlite.SqliteException"
                && current.GetType().GetProperty("SqliteErrorCode")?.GetValue(dbException) is 19)
            {
                return true;
            }
        }

        return false;
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
