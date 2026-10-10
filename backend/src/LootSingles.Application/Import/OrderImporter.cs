using LootSingles.Application.Persistence;
using LootSingles.Domain.Orders;
using Microsoft.Extensions.Logging;

namespace LootSingles.Application.Import;

/// <summary>
/// One order's unit of work, shared by every import source: validate, check for a duplicate,
/// create, run the optional per-order hook, save, handle the unique-violation race and
/// persistence failure, and record the <see cref="ImportOrderResult"/> on the attempt.
/// </summary>
public sealed class OrderImporter(IImportPersistence persistence, ILogger<OrderImporter> logger)
{
    /// <param name="beforeSave">
    /// Runs on the new order after it is created and before it is added and saved (the PDF path
    /// attaches the order's packing slip here). Null for sources with nothing to add.
    /// </param>
    /// <returns>The result recorded on <paramref name="attempt"/>.</returns>
    public async Task<ImportOrderResult> ImportAsync(
        ImportAttempt attempt,
        OrderCandidate candidate,
        Action<Order>? beforeSave,
        CancellationToken cancellationToken
    )
    {
        var result = new ImportOrderResult
        {
            ImportAttemptId = attempt.Id,
            SourceOrderIdentifier = candidate.SourceOrderIdentifier,
            Outcome = ImportOutcome.Rejected,
        };
        attempt.ImportOrderResults.Add(result);

        var validationFailure = OrderCandidateValidator.Validate(candidate);
        if (validationFailure is not null)
        {
            Reject(result, validationFailure.Value.Type, validationFailure.Value.Message);
            return result;
        }

        var orderIdentifier = candidate.SourceOrderIdentifier!;
        if (await persistence.OrderExistsAsync(orderIdentifier, cancellationToken))
        {
            Reject(
                result,
                FailureType.DuplicateOrder,
                $"Order '{orderIdentifier}' was already imported and was left unchanged."
            );
            return result;
        }

        var order = CreateOrder(orderIdentifier, candidate);
        result.Outcome = ImportOutcome.Succeeded;
        beforeSave?.Invoke(order);
        persistence.AddOrder(order);
        try
        {
            await persistence.SaveChangesAsync(cancellationToken);
            result.ResultingOrderId = order.Id;
        }
        catch (UniqueConstraintViolationException)
        {
            persistence.DiscardOrder(order);
            Reject(
                result,
                FailureType.DuplicateOrder,
                $"Order '{orderIdentifier}' was already imported by a concurrent operation."
            );
        }
        catch (OrderPersistenceException)
        {
            persistence.DiscardOrder(order);
            logger.LogError(
                "Import attempt {ImportId} could not persist order {OrderId}: {FailureType}.",
                attempt.Id,
                orderIdentifier,
                FailureType.PersistenceFailure
            );
            Reject(
                result,
                FailureType.PersistenceFailure,
                $"Order '{orderIdentifier}' could not be saved; no order or product lines were retained. Retry the import or contact support if the problem continues."
            );
        }

        return result;
    }

    // The validator has already guaranteed the fields the non-null assertions below rely on.
    // CollectorNumber is still non-nullable on OrderLine; T014 relaxes it for API lines.
    private static Order CreateOrder(string orderIdentifier, OrderCandidate candidate) =>
        new()
        {
            TcgplayerOrderId = orderIdentifier,
            Status = OrderStatus.Ready,
            ImportedAt = DateTimeOffset.UtcNow,
            OrderLines = candidate
                .Lines.Select(line => new OrderLine
                {
                    RawDescription = line.RawDescription,
                    ProductLine = line.ProductLine!,
                    ProductName = line.ProductName!,
                    Set = line.Set!,
                    CollectorNumber = line.CollectorNumber!,
                    Rarity = line.Rarity,
                    Condition = line.Condition!,
                    Variant = line.Variant,
                    Quantity = line.Quantity!.Value,
                })
                .ToList(),
        };

    private static void Reject(ImportOrderResult result, FailureType type, string message)
    {
        result.Outcome = ImportOutcome.Rejected;
        result.FailureCode = type;
        result.FailureMessage = message;
        result.ResultingOrderId = null;
    }
}
