using System.Security.Claims;
using LootSingles.Application.Orders;
using LootSingles.Domain.Orders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LootSingles.Api.Controllers;

[ApiController]
[Route("api/orders")]
[Authorize]
public sealed class OrdersController(
    OrdersService ordersService,
    OrderClaimService orderClaimService
) : ControllerBase
{
    [HttpPost("pick-next")]
    public async Task<IActionResult> PickNext(CancellationToken cancellationToken)
    {
        var result = await orderClaimService.PickNextAsync(ActorEmployeeId(), cancellationToken);

        return result.Outcome switch
        {
            OrderClaimOutcome.Success => Ok(ToClaimResponse(result.Order!)),
            OrderClaimOutcome.NoOrdersAvailable => Conflict(new { error = "no_orders_available" }),
            OrderClaimOutcome.EmployeeHasActiveClaim => Conflict(
                new
                {
                    error = "employee_has_active_claim",
                    claimedOrderId = result.ConflictingOrderId,
                }
            ),
            _ => throw new InvalidOperationException(
                $"Unexpected outcome {result.Outcome} for Pick Next Order."
            ),
        };
    }

    [HttpPost("{orderId:int}/claim")]
    public async Task<IActionResult> Claim(int orderId, CancellationToken cancellationToken)
    {
        var result = await orderClaimService.ClaimAsync(
            orderId,
            ActorEmployeeId(),
            cancellationToken
        );

        return result.Outcome switch
        {
            OrderClaimOutcome.Success => Ok(ToClaimResponse(result.Order!)),
            OrderClaimOutcome.OrderNotFound => NotFound(new { error = "order_not_found" }),
            OrderClaimOutcome.AlreadyClaimed => Conflict(
                new
                {
                    error = "order_already_claimed",
                    claimedByEmployeeId = result.Order!.ClaimedByEmployeeId,
                    claimedByEmployeeName = result.Order.ClaimedByEmployee?.DisplayName,
                }
            ),
            OrderClaimOutcome.EmployeeHasActiveClaim => Conflict(
                new
                {
                    error = "employee_has_active_claim",
                    claimedOrderId = result.ConflictingOrderId,
                }
            ),
            _ => throw new InvalidOperationException(
                $"Unexpected outcome {result.Outcome} for Claim."
            ),
        };
    }

    [HttpPost("{orderId:int}/release")]
    public async Task<IActionResult> Release(int orderId, CancellationToken cancellationToken)
    {
        var result = await orderClaimService.ReleaseAsync(
            orderId,
            ActorEmployeeId(),
            cancellationToken
        );

        return result.Outcome switch
        {
            OrderClaimOutcome.Success => Ok(ToClaimResponse(result.Order!)),
            OrderClaimOutcome.OrderNotFound => NotFound(new { error = "order_not_found" }),
            OrderClaimOutcome.NotYourClaim => Conflict(new { error = "not_your_claim" }),
            _ => throw new InvalidOperationException(
                $"Unexpected outcome {result.Outcome} for Release."
            ),
        };
    }

    private static OrderClaimResponse ToClaimResponse(Order order) =>
        new(
            order.Id,
            order.TcgplayerOrderId,
            order.Status,
            order.ClaimedByEmployeeId,
            order.ClaimedByEmployee?.DisplayName
        );

    private int ActorEmployeeId() => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

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
                    order.ImportedAt,
                    order.ClaimedByEmployeeId,
                    order.ClaimedByEmployeeName
                ))
                .ToList()
        );
    }

    [HttpGet("{orderId:int}")]
    public async Task<IActionResult> GetById(int orderId, CancellationToken cancellationToken)
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
                order.Status,
                order
                    .Lines.Select(line => new OrderLineDetailResponse(
                        line.ProductName,
                        line.ProductLine,
                        line.Set,
                        line.CollectorNumber,
                        line.Rarity,
                        line.Variant,
                        line.Condition,
                        line.Quantity,
                        line.ImageUrl
                    ))
                    .ToList(),
                order.ClaimedByEmployeeId,
                order.ClaimedByEmployeeName
            )
        );
    }
}

public sealed record OrderResponse(
    int OrderId,
    string TcgplayerOrderId,
    OrderStatus Status,
    DateTimeOffset ImportedAt,
    int? ClaimedByEmployeeId,
    string? ClaimedByEmployeeName
);

public sealed record OrderDetailResponse(
    int OrderId,
    string TcgplayerOrderId,
    OrderStatus Status,
    IReadOnlyList<OrderLineDetailResponse> Lines,
    int? ClaimedByEmployeeId,
    string? ClaimedByEmployeeName
);

public sealed record OrderClaimResponse(
    int OrderId,
    string TcgplayerOrderId,
    OrderStatus Status,
    int? ClaimedByEmployeeId,
    string? ClaimedByEmployeeName
);

public sealed record OrderLineDetailResponse(
    string ProductName,
    string ProductLine,
    string Set,
    string CollectorNumber,
    string? Rarity,
    string? Variant,
    string Condition,
    int Quantity,
    string? ImageUrl
);
