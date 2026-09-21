using System.Security.Claims;
using LootSingles.Application.Dashboard;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LootSingles.Api.Controllers;

/// <summary>
/// The Dashboard's live data (contracts/dashboard-api.md; 015-pick-completion US3) — one section
/// per order status, each derived from real order state.
/// </summary>
[ApiController]
[Route("api/dashboard")]
[Authorize]
public sealed class DashboardController(DashboardService dashboardService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var readyOrders = await dashboardService.GetReadyOrderSummariesAsync(cancellationToken);
        var inProgressOrders = await dashboardService.GetInProgressOrderSummariesAsync(
            cancellationToken
        );
        var needsAttentionOrders = await dashboardService.GetNeedsAttentionOrderSummariesAsync(
            cancellationToken
        );
        var pickedOrders = await dashboardService.GetPickedOrderSummariesAsync(cancellationToken);
        // Only ever the signed-in employee's own order: no other employee's identity is added to
        // this payload (Constitution VII, PRD §27).
        var activeClaim = await dashboardService.GetActiveClaimAsync(
            int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!),
            cancellationToken
        );

        return Ok(
            new DashboardResponse(
                ToSection(readyOrders),
                ToSection(inProgressOrders),
                new NeedsAttentionSectionResponse(
                    needsAttentionOrders.Count,
                    needsAttentionOrders
                        .Select(order => new NeedsAttentionOrderSummaryResponse(
                            order.OrderId,
                            order.TcgplayerOrderId,
                            order.ProductCount,
                            order.TotalQuantity,
                            order.FlaggedProductNames
                        ))
                        .ToList()
                ),
                ToSection(pickedOrders),
                activeClaim is null
                    ? null
                    : new OrderSummaryResponse(
                        activeClaim.OrderId,
                        activeClaim.TcgplayerOrderId,
                        activeClaim.ProductCount,
                        activeClaim.TotalQuantity
                    )
            )
        );
    }

    private static OrderSectionResponse ToSection(IReadOnlyList<OrderSummary> orders) =>
        new(
            orders.Count,
            orders
                .Select(order => new OrderSummaryResponse(
                    order.OrderId,
                    order.TcgplayerOrderId,
                    order.ProductCount,
                    order.TotalQuantity
                ))
                .ToList()
        );
}

public sealed record DashboardResponse(
    OrderSectionResponse Ready,
    OrderSectionResponse InProgress,
    NeedsAttentionSectionResponse NeedsAttention,
    OrderSectionResponse Picked,
    /// <summary>
    /// The order the signed-in employee already holds, or <c>null</c>. Never another
    /// employee's, and never carrying another employee's identity.
    /// </summary>
    OrderSummaryResponse? ActiveClaim
);

public sealed record OrderSectionResponse(int Count, IReadOnlyList<OrderSummaryResponse> Orders);

public sealed record NeedsAttentionSectionResponse(
    int Count,
    IReadOnlyList<NeedsAttentionOrderSummaryResponse> Orders
);

public sealed record OrderSummaryResponse(
    int OrderId,
    string TcgplayerOrderId,
    int ProductCount,
    int TotalQuantity
);

public sealed record NeedsAttentionOrderSummaryResponse(
    int OrderId,
    string TcgplayerOrderId,
    int ProductCount,
    int TotalQuantity,
    IReadOnlyList<string> FlaggedProductNames
);
