using System.Security.Claims;
using LootSingles.Application.Orders;
using LootSingles.Application.Picking;
using LootSingles.Domain.Employees;
using LootSingles.Domain.Orders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LootSingles.Api.Controllers;

[ApiController]
[Route("api/orders")]
[Authorize]
public sealed class OrdersController(
    OrdersService ordersService,
    OrderClaimService orderClaimService,
    PickingService pickingService
) : ControllerBase
{
    [HttpPost("{orderId:int}/lines/{lineId:int}/pick")]
    public async Task<IActionResult> Pick(
        int orderId,
        int lineId,
        CancellationToken cancellationToken
    )
    {
        var result = await pickingService.RecordPickedAsync(
            orderId,
            lineId,
            ActorEmployeeId(),
            cancellationToken
        );

        return ToPickingResponse(result);
    }

    [HttpPost("{orderId:int}/lines/{lineId:int}/report-issue")]
    public async Task<IActionResult> ReportIssue(
        int orderId,
        int lineId,
        [FromBody] ReportIssueRequest request,
        CancellationToken cancellationToken
    )
    {
        var result = await pickingService.ReportIssueAsync(
            orderId,
            lineId,
            ActorEmployeeId(),
            request.IssueType,
            request.RequiredQuantity,
            request.FoundQuantity,
            request.Note,
            cancellationToken
        );

        return ToPickingResponse(result);
    }

    private IActionResult ToPickingResponse(PickingResult result) =>
        result.Outcome switch
        {
            // The detail the recording transaction committed (branch review BR-002/BR-003). Card
            // images are not re-resolved here: a pick cannot change them, and the client keeps the
            // ones it already loaded.
            PickingOutcome.Success => Ok(ToDetailResponse(result.Order!)),
            PickingOutcome.OrderNotFound => NotFound(new { error = "order_not_found" }),
            PickingOutcome.LineNotFound => NotFound(new { error = "line_not_found" }),
            PickingOutcome.NotYourClaim => Conflict(new { error = "not_your_claim" }),
            PickingOutcome.InvalidIssueType => BadRequest(new { error = "invalid_issue_type" }),
            PickingOutcome.InvalidIssueDetails => BadRequest(
                new { error = "invalid_issue_details" }
            ),
            _ => throw new InvalidOperationException(
                $"Unexpected outcome {result.Outcome} for recording a pick outcome."
            ),
        };

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

    [HttpPost("{orderId:int}/force-release")]
    [Authorize(Roles = nameof(EmployeeRole.ManagerAdmin))]
    public async Task<IActionResult> ForceRelease(int orderId, CancellationToken cancellationToken)
    {
        var result = await orderClaimService.ForceReleaseAsync(
            orderId,
            ActorEmployeeId(),
            cancellationToken
        );

        return result.Outcome switch
        {
            OrderClaimOutcome.Success => Ok(ToClaimResponse(result.Order!)),
            OrderClaimOutcome.OrderNotFound => NotFound(new { error = "order_not_found" }),
            OrderClaimOutcome.OrderNotClaimed => Conflict(new { error = "order_not_claimed" }),
            _ => throw new InvalidOperationException(
                $"Unexpected outcome {result.Outcome} for ForceRelease."
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

        return Ok(ToDetailResponse(order));
    }

    private static OrderDetailResponse ToDetailResponse(OrderDetail order) =>
        new(
            order.OrderId,
            order.TcgplayerOrderId,
            order.Status,
            order
                .Lines.Select(line => new OrderLineDetailResponse(
                    line.Id,
                    line.PickOutcome,
                    line.ProductName,
                    line.ProductLine,
                    line.Set,
                    line.CollectorNumber,
                    line.Rarity,
                    line.Variant,
                    line.Condition,
                    line.Quantity,
                    line.ImageUrl,
                    line.CurrentIssue is null
                        ? null
                        : new PickingIssueResponse(
                            line.CurrentIssue.IssueType,
                            line.CurrentIssue.RequiredQuantity,
                            line.CurrentIssue.FoundQuantity,
                            line.CurrentIssue.Note,
                            line.CurrentIssue.ReportedByEmployeeName,
                            line.CurrentIssue.ReportedAt
                        )
                ))
                .ToList(),
            order.ClaimedByEmployeeId,
            order.ClaimedByEmployeeName
        );
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
    int Id,
    PickOutcome? PickOutcome,
    string ProductName,
    string ProductLine,
    string Set,
    string CollectorNumber,
    string? Rarity,
    string? Variant,
    string Condition,
    int Quantity,
    string? ImageUrl,
    PickingIssueResponse? CurrentIssue
);

public sealed record PickingIssueResponse(
    PickingIssueType IssueType,
    int? RequiredQuantity,
    int? FoundQuantity,
    string? Note,
    string? ReportedByEmployeeName,
    DateTimeOffset ReportedAt
);

public sealed record ReportIssueRequest(
    PickingIssueType IssueType,
    int? RequiredQuantity,
    int? FoundQuantity,
    string? Note
);
