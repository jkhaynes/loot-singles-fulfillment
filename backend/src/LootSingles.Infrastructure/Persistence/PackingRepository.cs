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
        // Projected rather than materialised. An earlier version included every awaiting
        // order's OrderLines and counted them in memory, which pulled thousands of rows to
        // render a handful of card counts and contradicted the plan this was built from. The
        // counts are computed in SQL, and the list is bounded (BR-002).
        var rows = await context
            .Orders.AsNoTracking()
            .Where(order => order.Status == OrderStatus.Picked && order.PackedAt == null)
            .OrderBy(order => order.ImportedAt)
            .Take(PackingLimits.AwaitingPageSize)
            .Select(order => new
            {
                order.Id,
                order.TcgplayerOrderId,
                CardCount = order
                    .OrderLines.Where(line => line.PickOutcome == PickOutcome.Picked)
                    .Sum(line => line.Quantity),
                PickedAt = order
                    .OrderLines.Where(line => line.PickOutcomeRecordedAt != null)
                    .Max(line => line.PickOutcomeRecordedAt),
                ContributorIds = order
                    .OrderLines.Where(line => line.PickOutcomeRecordedByEmployeeId != null)
                    .Select(line => line.PickOutcomeRecordedByEmployeeId!.Value)
                    .Distinct()
                    .ToList(),
                HasPackingSlip = context.OrderPackingSlips.Any(slip => slip.OrderId == order.Id),
            })
            .ToListAsync(cancellationToken);

        var names = await NamesForIdsAsync(
            rows.SelectMany(row => row.ContributorIds).Distinct().ToList(),
            cancellationToken
        );

        return rows.Select(row => new PackingView(
                OrderId: row.Id,
                TcgplayerOrderId: row.TcgplayerOrderId,
                CardCount: row.CardCount,
                PickedBy: row.ContributorIds.Select(id => new LabelContributor(
                        id,
                        names.TryGetValue(id, out var name) ? name : "Unknown"
                    ))
                    .ToList(),
                PickedAt: row.PickedAt,
                // Every row here is picked, unpacked and free of unresolved issues by
                // construction — the filter above is exactly the packable condition.
                Status: nameof(OrderStatus.Picked),
                CanPack: true,
                BlockedReason: null,
                UnresolvedProducts: [],
                HasPackingSlip: row.HasPackingSlip
            ))
            .ToList();
    }

    public async Task<PackOutcome> MarkPackedAsync(
        int orderId,
        int actorEmployeeId,
        CancellationToken cancellationToken
    )
    {
        // Write first, explain afterwards.
        //
        // An earlier version checked the order out of the database and then wrote if the checks
        // passed, which left a window between them. Feature 015 deliberately keeps a picked order
        // re-claimable so a picker can revise a line, so "a picker reports an issue while a packer
        // scans the same sleeve" is a supported workflow — and it landed in that window on the
        // first attempt every time, packing an order whose line was unresolved. Because Packed
        // short-circuits the status derivation, nothing afterwards corrected it (FR-034,
        // Constitution VI).
        //
        // Every condition that decides whether this order may be packed now lives in the WHERE
        // clause, so the database evaluates them against the same rows it updates.
        var rowsAffected = await context
            .Orders.Where(candidate =>
                candidate.Id == orderId
                && candidate.PackedAt == null
                && candidate.Status == OrderStatus.Picked
                && !candidate.OrderLines.Any(line => line.PickOutcome == PickOutcome.HasIssue)
            )
            .ExecuteUpdateAsync(
                setters =>
                    setters
                        .SetProperty(candidate => candidate.PackedAt, DateTimeOffset.UtcNow)
                        .SetProperty(candidate => candidate.PackedByEmployeeId, actorEmployeeId)
                        .SetProperty(candidate => candidate.Status, OrderStatus.Packed),
                cancellationToken
            );

        if (rowsAffected == 1)
        {
            return PackOutcome.Success;
        }

        // Nothing was packed. Read now to say why — this read cannot race anything, because the
        // decision has already been made and the answer is only used to word the refusal.
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

        return PackOutcome.NotAwaitingPacking;
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
        var (canPack, blockedReason) = Packability(order);

        return new PackingView(
            OrderId: order.Id,
            TcgplayerOrderId: order.TcgplayerOrderId,
            CardCount: label.CardCount,
            PickedBy: label.PickedBy,
            PickedAt: label.PickedAt,
            Status: order.Status.ToString(),
            CanPack: canPack,
            BlockedReason: blockedReason,
            // Reported issues only. The label's list also names products nobody looked at, but the
            // desk tells those apart: an order still being picked says so, and lists nothing.
            UnresolvedProducts: order
                .OrderLines.Where(line => line.PickOutcome == PickOutcome.HasIssue)
                .Select(line => line.ProductName)
                .ToList(),
            HasPackingSlip: hasPackingSlip
        );
    }

    // NeedsAttention is derived from a reported issue on any line. The label's IsHeld is wider and
    // also covers products nobody looked at, which here are "not finished picking", not a manager's.
    private static (bool CanPack, string? BlockedReason) Packability(Order order) =>
        order.PackedAt is not null ? (false, "This order has already been packed.")
        : order.Status == OrderStatus.NeedsAttention
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
    ) =>
        await NamesForIdsAsync(
            lines
                .Where(line => line.PickOutcomeRecordedByEmployeeId is not null)
                .Select(line => line.PickOutcomeRecordedByEmployeeId!.Value)
                .Distinct()
                .ToList(),
            cancellationToken
        );

    private async Task<Dictionary<int, string>> NamesForIdsAsync(
        IReadOnlyCollection<int> ids,
        CancellationToken cancellationToken
    )
    {
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
