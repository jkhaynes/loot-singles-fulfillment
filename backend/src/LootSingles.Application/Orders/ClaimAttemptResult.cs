using LootSingles.Domain.Orders;

namespace LootSingles.Application.Orders;

/// <summary>
/// The outcome of a single conditional claim attempt against a specific order id
/// (013-order-claiming, research.md §1). <see cref="Succeeded"/> true means <see cref="Order"/> is
/// the freshly claimed order. <see cref="Succeeded"/> false with a null <see cref="Order"/> means no
/// order with that id exists; false with a non-null <see cref="Order"/> means it is already claimed
/// by someone else (whose identity is on <see cref="Domain.Orders.Order.ClaimedByEmployee"/>).
/// </summary>
public sealed record ClaimAttemptResult(bool Succeeded, Order? Order);
