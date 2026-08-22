using LootSingles.Application.Orders;
using LootSingles.Domain.Orders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LootSingles.Api.Controllers;

[ApiController]
[Route("api/orders")]
[Authorize]
public sealed class OrdersController(OrdersService ordersService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var orders = await ordersService.GetAllAsync(cancellationToken);

        return Ok(
            orders
                .Select(order => new OrderResponse(
                    order.OrderId,
                    order.TcgplayerOrderId,
                    order.Status,
                    order.ImportedAt
                ))
                .ToList()
        );
    }
}

public sealed record OrderResponse(
    int OrderId,
    string TcgplayerOrderId,
    OrderStatus Status,
    DateTimeOffset ImportedAt
);
