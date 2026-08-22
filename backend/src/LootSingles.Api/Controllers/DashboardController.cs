using LootSingles.Application.Dashboard;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LootSingles.Api.Controllers;

/// <summary>
/// The Dashboard's live data (contracts/dashboard-api.md, US2). Ready-only for now — the other
/// three sections have no backing data yet and are rendered as static placeholders entirely on
/// the frontend (data-model.md).
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

        return Ok(
            new DashboardResponse(
                new ReadySectionResponse(
                    readyOrders.Count,
                    readyOrders
                        .Select(order => new OrderSummaryResponse(
                            order.OrderId,
                            order.TcgplayerOrderId,
                            order.ProductCount,
                            order.TotalQuantity
                        ))
                        .ToList()
                )
            )
        );
    }
}

public sealed record DashboardResponse(ReadySectionResponse Ready);

public sealed record ReadySectionResponse(int Count, IReadOnlyList<OrderSummaryResponse> Orders);

public sealed record OrderSummaryResponse(
    int OrderId,
    string TcgplayerOrderId,
    int ProductCount,
    int TotalQuantity
);
