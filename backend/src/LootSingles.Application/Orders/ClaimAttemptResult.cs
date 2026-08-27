using LootSingles.Domain.Orders;

namespace LootSingles.Application.Orders;

/// <summary>
/// The outcome of a single conditional compare-and-swap attempt against a specific order id's
/// claim state (013-order-claiming, research.md §1) — shared by claim, release, and force-release,
/// since all four are the same "conditional update; report what actually happened" shape.
/// <see cref="Succeeded"/> true means <see cref="Order"/> is the order's fresh post-update state.
/// <see cref="Succeeded"/> false with a null <see cref="Order"/> means no order with that id exists;
/// false with a non-null <see cref="Order"/> means the precondition didn't hold — e.g. for a claim
/// attempt, already claimed by someone else (whose identity is on
/// <see cref="Domain.Orders.Order.ClaimedByEmployee"/>); for a release attempt, not currently held
/// by the caller.
/// </summary>
public sealed record ClaimAttemptResult(bool Succeeded, Order? Order);
