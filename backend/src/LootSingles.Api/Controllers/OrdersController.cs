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

    [HttpGet("{orderId:int}")]
    public async Task<IActionResult> GetById(
        int orderId,
        CancellationToken cancellationToken
    )
    {
        var order = await ordersService.GetByIdAsync(orderId, cancellationToken);

        if (order is null)
        {
            return NotFound(new { error = "order_not_found" });
        }

        return Ok(
            new OrderDetailResponse(
                order.OrderId,
                order.TcgplayerOrderId,
                order
                    .Lines.Select(line => new OrderLineDetailResponse(
                        line.ProductName,
                        line.Set,
                        line.Variant,
                        line.Condition,
                        line.Quantity
                    ))
                    .ToList()
            )
        );
    }
}

public sealed record OrderResponse(
    int OrderId,
    string TcgplayerOrderId,
    OrderStatus Status,
    DateTimeOffset ImportedAt
);

public sealed record OrderDetailResponse(
    int OrderId,
    string TcgplayerOrderId,
    IReadOnlyList<OrderLineDetailResponse> Lines
);

public sealed record OrderLineDetailResponse(
    string ProductName,
    string Set,
    string? Variant,
    string Condition,
    int Quantity
);
