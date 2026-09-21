using LootSingles.Application.Packing;
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
        // and nothing else about the order. Notably this never touches OrderPackingSlips, so a
        // label read cannot pull customer data into a picker-facing payload (FR-039).
        var order = await context
            .Orders.AsNoTracking()
            .Include(candidate => candidate.OrderLines)
            .SingleOrDefaultAsync(candidate => candidate.Id == orderId, cancellationToken);

        if (order is null)
        {
            return null;
        }

        var contributorIds = order
            .OrderLines.Where(line => line.PickOutcomeRecordedByEmployeeId is not null)
            .Select(line => line.PickOutcomeRecordedByEmployeeId!.Value)
            .Distinct()
            .ToList();

        // Resolved separately rather than through a navigation, which keeps LabelContent.From a
        // pure derivation that unit tests can exercise without a database.
        var displayNames =
            contributorIds.Count == 0
                ? []
                : await context
                    .Employees.AsNoTracking()
                    .Where(employee => contributorIds.Contains(employee.Id))
                    .ToDictionaryAsync(
                        employee => employee.Id,
                        employee => employee.DisplayName,
                        cancellationToken
                    );

        return LabelContent.From(order, displayNames);
    }
}
