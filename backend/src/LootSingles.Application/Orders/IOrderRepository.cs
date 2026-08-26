using LootSingles.Domain.Orders;

namespace LootSingles.Application.Orders;

public interface IOrderRepository
{
    Task<IReadOnlyList<OrderListItem>> GetAllAsync(CancellationToken cancellationToken);

    Task<OrderDetail?> GetByIdAsync(int orderId, CancellationToken cancellationToken);

    /// <summary>
    /// Atomically claims the oldest Ready, unclaimed order for <paramref name="actorEmployeeId"/>,
    /// or returns null if none are available (013-order-claiming FR-001, research.md §1-2).
    /// </summary>
    /// <exception cref="LootSingles.Application.Persistence.UniqueConstraintViolationException">
    /// The employee already holds another active claim (research.md §3 race backstop).
    /// </exception>
    Task<Order?> ClaimNextAvailableAsync(int actorEmployeeId, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the id of the order currently claimed by <paramref name="employeeId"/>, or null if
    /// they hold no active claim (013-order-claiming FR-009).
    /// </summary>
    Task<int?> GetActiveClaimedOrderIdAsync(int employeeId, CancellationToken cancellationToken);

    /// <summary>
    /// Atomically claims a caller-chosen order for <paramref name="actorEmployeeId"/> if it is
    /// still Ready and unclaimed (013-order-claiming FR-002-004, research.md §1).
    /// </summary>
    /// <exception cref="LootSingles.Application.Persistence.UniqueConstraintViolationException">
    /// The employee already holds another active claim (research.md §3 race backstop).
    /// </exception>
    Task<ClaimAttemptResult> ClaimSpecificAsync(
        int orderId,
        int actorEmployeeId,
        CancellationToken cancellationToken
    );
}
