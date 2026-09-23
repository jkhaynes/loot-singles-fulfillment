using LootSingles.Application.Import;
using LootSingles.Domain.Employees;
using LootSingles.Domain.Orders;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
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
public class LootSinglesDbContext : DbContext, IDataProtectionKeyContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LootSinglesDbContext"/> class.
    /// </summary>
    /// <param name="options">The options to be used by the DbContext.</param>
    public LootSinglesDbContext(DbContextOptions<LootSinglesDbContext> options)
        : base(options) { }

    /// <summary>
    /// The ASP.NET Core Data Protection key ring, which encrypts the session cookie (019 FR-026).
    ///
    /// Owned by the framework, never read or written by application code. It lives here because the
    /// container scales to zero when idle: with the default in-memory key ring, every quiet spell
    /// would invalidate every signed-in session, so a picker taking a break would come back signed
    /// out. Each environment has its own database and therefore its own key ring, which is the
    /// intended isolation — a cookie issued by stage is not valid in production.
    ///
    /// The <c>Xml</c> column holds key material. It MUST NOT be logged, echoed in diagnostics, or
    /// returned by any endpoint; protection at rest is the database's own encryption.
    /// </summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

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
    /// DbSet for PickingIssue entities (015-pick-completion).
    /// </summary>
    public DbSet<PickingIssue> PickingIssues => Set<PickingIssue>();

    /// <summary>
    /// DbSet for OrderPackingSlip entities (017-pick-completion-handoff).
    /// Queried only by the packing workflow — an order never loads its slip incidentally, which is
    /// what keeps slip bytes off every picking path.
    /// </summary>
    public DbSet<OrderPackingSlip> OrderPackingSlips => Set<OrderPackingSlip>();

    /// <summary>
    /// DbSet for PackingSlipAccess entities (017-pick-completion-handoff).
    /// </summary>
    public DbSet<PackingSlipAccess> PackingSlipAccesses => Set<PackingSlipAccess>();

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
