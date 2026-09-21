using System.Security.Claims;
using LootSingles.Application.Orders;
using LootSingles.Application.Packing;
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
    PickingService pickingService,
    IPackingRepository packingRepository,
    ILogger<OrdersController> logger
) : ControllerBase
{
    /// <summary>
    /// What goes on this order label (FR-009 through FR-017). Every value is derived from the
    /// order, so the label cannot disagree with what it identifies and a reprint matches the
    /// original. Carries no customer data of any kind (FR-015).
    /// </summary>
    /// <summary>
    /// Records an order as packed (FR-027, FR-032). Terminal — this feature provides no
    /// inverse, per PRD §20.1.
    /// </summary>
    [HttpPost("{orderId:int}/packed")]
    public async Task<IActionResult> MarkPacked(int orderId, CancellationToken cancellationToken)
    {
        var outcome = await packingRepository.MarkPackedAsync(
            orderId,
            ActorEmployeeId(),
            cancellationToken
        );

        if (outcome == PackOutcome.Success)
        {
            logger.LogInformation(
                "Employee {EmployeeId} recorded order {OrderId} as packed.",
                ActorEmployeeId(),
                orderId
            );
        }

        return outcome switch
        {
            PackOutcome.Success => Ok(
                await packingRepository.ResolveAsync(
                    new PackingCode(orderId, null),
                    cancellationToken
                )
            ),
            PackOutcome.OrderNotFound => NotFound(new { error = "order_not_found" }),
            PackOutcome.AlreadyPacked => Conflict(new { error = "order_already_packed" }),
            // Named products, not a bare conflict: a packer has to be able to tell whether it
            // is theirs to fix, and it is not (FR-028).
            PackOutcome.HasUnresolvedIssue => Conflict(
                new
                {
                    error = "order_has_unresolved_issue",
                    unresolvedProducts = (
                        await packingRepository.GetLabelContentAsync(orderId, cancellationToken)
                    )?.UnresolvedProducts
                        ?? [],
                }
            ),
            PackOutcome.NotAwaitingPacking => Conflict(
                new { error = "order_not_awaiting_packing" }
            ),
            _ => throw new InvalidOperationException(
                $"Unexpected outcome {outcome} for MarkPacked."
            ),
        };
    }

    /// <summary>
    /// An order's stored packing slip, for printing at the bench.
    /// <para>
    /// <b>The only endpoint in this application that returns customer personal information.</b>
    /// Every retrieval is recorded with the employee and the time (FR-038), and no picking
    /// surface links here (FR-039).
    /// </para>
    /// </summary>
    [HttpGet("{orderId:int}/packing-slip")]
    public async Task<IActionResult> PackingSlip(int orderId, CancellationToken cancellationToken)
    {
        var result = await packingRepository.GetPackingSlipAsync(
            orderId,
            ActorEmployeeId(),
            cancellationToken
        );

        switch (result.Outcome)
        {
            case PackingSlipOutcome.OrderNotFound:
                return NotFound(new { error = "order_not_found" });

            case PackingSlipOutcome.Unavailable:
                // Distinct from order_not_found so the desk can say which is true. Every order
                // imported before this feature is in exactly this state (FR-022).
                return NotFound(new { error = "packing_slip_unavailable" });

            default:
                // Logged alongside the durable record: the order and the employee, never the
                // slip itself, which the constitution forbids putting in a log.
                logger.LogInformation(
                    "Employee {EmployeeId} retrieved the packing slip for order {OrderId}.",
                    ActorEmployeeId(),
                    orderId
                );

                return File(result.Content!, "application/pdf");
        }
    }

    [HttpGet("{orderId:int}/label")]
    public async Task<IActionResult> Label(int orderId, CancellationToken cancellationToken)
    {
        var label = await packingRepository.GetLabelContentAsync(orderId, cancellationToken);

        if (label is null)
        {
            return NotFound(new { error = "order_not_found" });
        }

        // The guard is whether picking has happened, not whether the order reached Picked status:
        // a held order is NeedsAttention and must still print a hold label (FR-013).
        if (!label.HasStarted)
        {
            return Conflict(new { error = "order_not_started" });
        }

        return Ok(
            new
            {
                orderId = label.OrderId,
                tcgplayerOrderId = label.TcgplayerOrderId,
                cardCount = label.CardCount,
                pickedBy = label
                    .PickedBy.Select(person => new
                    {
                        employeeId = person.EmployeeId,
                        displayName = person.DisplayName,
                    })
                    .ToList(),
                pickedAt = label.PickedAt,
                isHeld = label.IsHeld,
                unresolvedProducts = label.UnresolvedProducts,
                setAsideCount = label.SetAsideCount,
                shipsShort = label.ShipsShort,
            }
        );
    }

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
            OrderClaimOutcome.OrderAlreadyPacked => Conflict(
                new { error = "order_already_packed" }
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
