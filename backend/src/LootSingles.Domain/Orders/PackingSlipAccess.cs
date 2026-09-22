namespace LootSingles.Domain.Orders;

/// <summary>
/// One row per retrieval of an order's packing slip (017-pick-completion-handoff FR-038).
/// <para>
/// A slip is the only customer personal information this application stores, so who opened one and
/// when has to be answerable after the fact (SC-010). The constitution confines production logging
/// to <c>ILogger&lt;T&gt;</c> writing to console/stdout only, which is ephemeral on the hosted
/// environment — so a log line alone cannot answer that question weeks later. This row is what
/// makes the record durable; the log line is kept for operational visibility (research.md §5).
/// </para>
/// <para>
/// Append-only: nothing in this feature edits or deletes a row. It holds no customer data — it
/// records that an access happened, never what the slip contained.
/// </para>
/// </summary>
public class PackingSlipAccess
{
    /// <summary>
    /// Primary key, auto-incremented identity.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Whose slip was retrieved.
    /// </summary>
    public int OrderId { get; set; }

    /// <summary>
    /// Which employee retrieved it.
    /// </summary>
    public int EmployeeId { get; set; }

    /// <summary>
    /// When the retrieval happened.
    /// </summary>
    public DateTimeOffset RetrievedAt { get; set; }

    /// <summary>
    /// Navigation to the order whose slip was retrieved.
    /// </summary>
    public Order? Order { get; set; }

    /// <summary>
    /// Navigation to the retrieving employee.
    /// </summary>
    public Employees.Employee? Employee { get; set; }
}
