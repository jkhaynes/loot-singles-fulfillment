using LootSingles.Application.Packing;
using LootSingles.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace LootSingles.Infrastructure.Persistence;

public sealed class PackingRepository(LootSinglesDbContext context) : IPackingRepository
{
    public async Task<LabelContent?> GetLabelContentAsync(
        int orderId,
        CancellationToken cancellationToken
    )
    {
        // Only the lines are included: the label needs their quantities, outcomes and recorders,
        // and nothing else about the order. Notably this never touches the slip, so a label read
        // cannot pull customer data into a picker-facing payload (FR-039).
        var order = await context
            .Orders.AsNoTracking()
            .Include(candidate => candidate.OrderLines)
            .SingleOrDefaultAsync(candidate => candidate.Id == orderId, cancellationToken);

        return order is null
            ? null
            : LabelContent.From(order, await NamesForAsync(order, cancellationToken));
    }

    public async Task<PackingView?> ResolveAsync(
        PackingCode code,
        CancellationToken cancellationToken
    )
    {
        if (!code.IsResolvable)
        {
            return null;
        }

        var order = await context
            .Orders.AsNoTracking()
            .Include(candidate => candidate.OrderLines)
            .SingleOrDefaultAsync(
                candidate =>
                    (code.OrderId != null && candidate.Id == code.OrderId)
                    || (
                        code.TcgplayerOrderId != null
                        && candidate.TcgplayerOrderId == code.TcgplayerOrderId
                    ),
                cancellationToken
            );

        if (order is null)
        {
            return null;
        }

        // Existence only — the bytes stay in the database until something asks for the slip.
        var hasSlip = await context.OrderPackingSlips.AnyAsync(
            slip => slip.OrderId == order.Id,
            cancellationToken
        );

        return ToView(order, await NamesForAsync(order, cancellationToken), hasSlip);
    }

    public async Task<IReadOnlyList<PackingView>> GetAwaitingPackingAsync(
        CancellationToken cancellationToken
    )
    {
        var orders = await context
            .Orders.AsNoTracking()
            .Include(order => order.OrderLines)
            .Where(order => order.Status == OrderStatus.Picked && order.PackedAt == null)
            .OrderBy(order => order.ImportedAt)
            .ToListAsync(cancellationToken);

        var slipOwners = await context
            .OrderPackingSlips.AsNoTracking()
            .Where(slip => orders.Select(order => order.Id).Contains(slip.OrderId))
            .Select(slip => slip.OrderId)
            .ToListAsync(cancellationToken);

        var names = await NamesForAsync(
            orders.SelectMany(order => order.OrderLines),
            cancellationToken
        );

        return orders.Select(order => ToView(order, names, slipOwners.Contains(order.Id))).ToList();
    }

    public async Task<PackOutcome> MarkPackedAsync(
        int orderId,
        int actorEmployeeId,
        CancellationToken cancellationToken
    )
    {
        var order = await context
            .Orders.AsNoTracking()
            .Include(candidate => candidate.OrderLines)
            .SingleOrDefaultAsync(candidate => candidate.Id == orderId, cancellationToken);

        if (order is null)
        {
            return PackOutcome.OrderNotFound;
        }

        if (order.PackedAt is not null)
        {
            return PackOutcome.AlreadyPacked;
        }

        if (order.OrderLines.Any(line => line.PickOutcome == PickOutcome.HasIssue))
        {
            return PackOutcome.HasUnresolvedIssue;
        }

        if (order.Status != OrderStatus.Picked)
        {
            return PackOutcome.NotAwaitingPacking;
        }

        // The checks above produce a useful answer; this write is what makes it true. PackedAt is
        // part of the condition, so two simultaneous attempts cannot both succeed — the loser
        // updates no rows and is told the order is already packed (FR-033).
        var rowsAffected = await context
            .Orders.Where(candidate => candidate.Id == orderId && candidate.PackedAt == null)
            .ExecuteUpdateAsync(
                setters =>
                    setters
                        .SetProperty(candidate => candidate.PackedAt, DateTimeOffset.UtcNow)
                        .SetProperty(candidate => candidate.PackedByEmployeeId, actorEmployeeId)
                        .SetProperty(candidate => candidate.Status, OrderStatus.Packed),
                cancellationToken
            );

        return rowsAffected == 1 ? PackOutcome.Success : PackOutcome.AlreadyPacked;
    }

    public async Task<PackingSlipResult> GetPackingSlipAsync(
        int orderId,
        int actorEmployeeId,
        CancellationToken cancellationToken
    )
    {
        if (!await context.Orders.AnyAsync(order => order.Id == orderId, cancellationToken))
        {
            return new PackingSlipResult(PackingSlipOutcome.OrderNotFound);
        }

        var content = await context
            .OrderPackingSlips.AsNoTracking()
            .Where(slip => slip.OrderId == orderId)
            .Select(slip => slip.Content)
            .SingleOrDefaultAsync(cancellationToken);

        if (content is null)
        {
            // Normal, not an error: every order imported before this feature is in this state,
            // as is any order whose slip could not be sliced (FR-022).
            return new PackingSlipResult(PackingSlipOutcome.Unavailable);
        }

        // Written before the bytes are handed over, so the record cannot be skipped by a caller
        // that fails partway through reading them (FR-038).
        context.PackingSlipAccesses.Add(
            new PackingSlipAccess
            {
                OrderId = orderId,
                EmployeeId = actorEmployeeId,
                RetrievedAt = DateTimeOffset.UtcNow,
            }
        );
        await context.SaveChangesAsync(cancellationToken);

        return new PackingSlipResult(PackingSlipOutcome.Success, content);
    }

    private static PackingView ToView(
        Order order,
        IReadOnlyDictionary<int, string> names,
        bool hasPackingSlip
    )
    {
        var label = LabelContent.From(order, names);
        var (canPack, blockedReason) = Packability(order, label);

        return new PackingView(
            OrderId: order.Id,
            TcgplayerOrderId: order.TcgplayerOrderId,
            CardCount: label.CardCount,
            PickedBy: label.PickedBy,
            PickedAt: label.PickedAt,
            Status: order.Status.ToString(),
            CanPack: canPack,
            BlockedReason: blockedReason,
            UnresolvedProducts: label.UnresolvedProducts,
            HasPackingSlip: hasPackingSlip
        );
    }

    private static (bool CanPack, string? BlockedReason) Packability(
        Order order,
        LabelContent label
    ) =>
        order.PackedAt is not null ? (false, "This order has already been packed.")
        : label.IsHeld
            ? (false, "A manager still has to decide what happens to one of its products.")
        : order.Status != OrderStatus.Picked ? (false, "This order has not finished picking yet.")
        : (true, null);

    private Task<Dictionary<int, string>> NamesForAsync(
        Order order,
        CancellationToken cancellationToken
    ) => NamesForAsync(order.OrderLines, cancellationToken);

    /// <summary>
    /// Display names for whoever recorded an outcome. Resolved separately rather than through a
    /// navigation, which keeps <see cref="LabelContent.From"/> a pure derivation that unit tests
    /// can exercise without a database.
    /// </summary>
    private async Task<Dictionary<int, string>> NamesForAsync(
        IEnumerable<OrderLine> lines,
        CancellationToken cancellationToken
    )
    {
        var ids = lines
            .Where(line => line.PickOutcomeRecordedByEmployeeId is not null)
            .Select(line => line.PickOutcomeRecordedByEmployeeId!.Value)
            .Distinct()
            .ToList();

        if (ids.Count == 0)
        {
            return [];
        }

        return await context
            .Employees.AsNoTracking()
            .Where(employee => ids.Contains(employee.Id))
            .ToDictionaryAsync(
                employee => employee.Id,
                employee => employee.DisplayName,
                cancellationToken
            );
    }
}
