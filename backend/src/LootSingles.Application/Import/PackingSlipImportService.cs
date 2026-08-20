using System.Runtime.CompilerServices;
using LootSingles.Domain.Orders;

namespace LootSingles.Application.Import;

public sealed class PackingSlipImportService(
    IPackingSlipParser parser,
    IImportPersistence persistence) : IPackingSlipImportService
{
    public async IAsyncEnumerable<ImportProgressUpdate> ImportAsync(
        Stream packingSlipPdf,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(packingSlipPdf);

        var parsed = parser.Parse(packingSlipPdf);
        var attempt = new ImportAttempt { StartedAt = DateTimeOffset.UtcNow };
        persistence.AddImportAttempt(attempt);

        var processed = 0;
        var succeeded = 0;

        foreach (var block in parsed.OrderBlocks)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var order = new Order
            {
                TcgplayerOrderId = block.OrderIdentifier
                    ?? throw new FormatException("A valid import requires an order identifier."),
                Status = OrderStatus.Ready,
                ImportedAt = DateTimeOffset.UtcNow,
                OrderLines = block.ProductLines.Select(OrderLineExtractor.Extract).ToList(),
            };
            var result = new ImportOrderResult
            {
                ImportAttemptId = attempt.Id,
                SourceOrderIdentifier = block.OrderIdentifier,
                Outcome = ImportOutcome.Succeeded,
            };

            attempt.ImportOrderResults.Add(result);
            persistence.AddOrder(order);
            await persistence.SaveChangesAsync(cancellationToken);
            result.ResultingOrderId = order.Id;
            processed++;
            succeeded++;

            yield return new ImportProgressUpdate(
                parsed.OrderBlocks.Count,
                processed,
                succeeded,
                0,
                false,
                attempt);
        }

        attempt.CompletedAt = DateTimeOffset.UtcNow;
        await persistence.SaveChangesAsync(cancellationToken);

        yield return new ImportProgressUpdate(
            parsed.OrderBlocks.Count,
            processed,
            succeeded,
            0,
            true,
            attempt);
    }
}
