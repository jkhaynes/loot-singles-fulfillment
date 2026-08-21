using System.Runtime.CompilerServices;
using LootSingles.Application.Persistence;
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
        var attempt = new ImportAttempt { StartedAt = DateTimeOffset.UtcNow };
        persistence.AddImportAttempt(attempt);

        ParsedPackingSlip? parsed = null;
        string? unreadableMessage = null;
        await using var parseEnumerator = parser.ParseAsync(packingSlipPdf, cancellationToken)
            .GetAsyncEnumerator(cancellationToken);
        while (true)
        {
            PackingSlipParseUpdate? parseUpdate = null;
            bool hasNext;
            try
            {
                hasNext = await parseEnumerator.MoveNextAsync();
                if (hasNext)
                {
                    parseUpdate = parseEnumerator.Current;
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                unreadableMessage = $"The supplied file could not be read as a packing-slip PDF: {exception.Message}";
                break;
            }

            if (!hasNext)
            {
                break;
            }

            if (parseUpdate!.IsComplete)
            {
                parsed = parseUpdate.PackingSlip;
                break;
            }

            yield return Update(parseUpdate.OrdersDetected, 0, 0, 0, false, attempt);
        }

        if (parsed is null)
        {
            attempt.AttemptFailureCode = FailureType.UnreadablePdf;
            attempt.AttemptFailureMessage = unreadableMessage;
            await CompleteAttemptAsync(attempt, cancellationToken);
            yield return Update(0, 0, 0, 0, true, attempt);
            yield break;
        }

        if (parsed.OrderBlocks.Count == 0)
        {
            attempt.AttemptFailureCode = FailureType.UnreadablePdf;
            attempt.AttemptFailureMessage =
                "The supplied PDF does not contain any recognizable order pages from a packing slip.";
            await CompleteAttemptAsync(attempt, cancellationToken);
            yield return Update(0, 0, 0, 0, true, attempt);
            yield break;
        }

        if (HasSummaryMismatch(parsed, out var mismatchMessage))
        {
            attempt.AttemptFailureCode = FailureType.SummaryMismatch;
            attempt.AttemptFailureMessage = mismatchMessage;
            await CompleteAttemptAsync(attempt, cancellationToken);
            yield return Update(parsed.OrderBlocks.Count, 0, 0, 0, true, attempt);
            yield break;
        }

        var processed = 0;
        var succeeded = 0;
        var failed = 0;
        foreach (var block in parsed.OrderBlocks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = new ImportOrderResult
            {
                ImportAttemptId = attempt.Id,
                SourceOrderIdentifier = block.OrderIdentifier,
                Outcome = ImportOutcome.Rejected,
            };
            attempt.ImportOrderResults.Add(result);

            var (lineResults, validationFailure) = ValidateBlock(block);
            if (validationFailure is not null)
            {
                Reject(result, validationFailure.Value.Type, validationFailure.Value.Message);
                failed++;
            }
            else if (await persistence.OrderExistsAsync(block.OrderIdentifier!, cancellationToken))
            {
                Reject(result, FailureType.DuplicateOrder,
                    $"Order '{block.OrderIdentifier}' was already imported and was left unchanged.");
                failed++;
            }
            else
            {
                var order = CreateOrder(block, lineResults);
                result.Outcome = ImportOutcome.Succeeded;
                persistence.AddOrder(order);
                try
                {
                    await persistence.SaveChangesAsync(cancellationToken);
                    result.ResultingOrderId = order.Id;
                    succeeded++;
                }
                catch (UniqueConstraintViolationException)
                {
                    persistence.DiscardOrder(order);
                    Reject(result, FailureType.DuplicateOrder,
                        $"Order '{block.OrderIdentifier}' was already imported by a concurrent operation.");
                    failed++;
                }
                catch (OrderPersistenceException)
                {
                    persistence.DiscardOrder(order);
                    Reject(result, FailureType.PersistenceFailure,
                        $"Order '{block.OrderIdentifier}' could not be saved; no order or product lines were retained. Retry the import or contact support if the problem continues.");
                    failed++;
                }
            }

            processed++;
            await persistence.SaveChangesAsync(cancellationToken);
            yield return Update(parsed.OrderBlocks.Count, processed, succeeded, failed, false, attempt);
        }

        await CompleteAttemptAsync(attempt, cancellationToken);
        yield return Update(parsed.OrderBlocks.Count, processed, succeeded, failed, true, attempt);
    }

    private async Task CompleteAttemptAsync(ImportAttempt attempt, CancellationToken cancellationToken)
    {
        attempt.CompletedAt = DateTimeOffset.UtcNow;
        await persistence.SaveChangesAsync(cancellationToken);
    }

    private static (List<OrderLineValidationResult> LineResults, (FailureType Type, string Message)? Failure) ValidateBlock(
        RawOrderBlock block)
    {
        var lineResults = new List<OrderLineValidationResult>();
        if (string.IsNullOrWhiteSpace(block.OrderIdentifier))
            return (lineResults, (FailureType.MissingOrderIdentifier, "An order page is missing its order identifier."));
        if (block.ProductLines.Count == 0)
            return (lineResults, (FailureType.NoProductLines, $"Order '{block.OrderIdentifier}' contains no product lines."));

        foreach (var line in block.ProductLines)
        {
            var validation = OrderLineValidator.Validate(line);
            lineResults.Add(validation);
            if (!validation.IsValid)
                return (lineResults, (validation.FailureType!.Value, $"Order '{block.OrderIdentifier}': {validation.FailureMessage}"));
        }
        return (lineResults, null);
    }

    private static Order CreateOrder(RawOrderBlock block, List<OrderLineValidationResult> lineResults) => new()
    {
        TcgplayerOrderId = block.OrderIdentifier!,
        Status = OrderStatus.Ready,
        ImportedAt = DateTimeOffset.UtcNow,
        OrderLines = lineResults.Select(result => result.OrderLine!).ToList(),
    };

    private static void Reject(ImportOrderResult result, FailureType type, string message)
    {
        result.Outcome = ImportOutcome.Rejected;
        result.FailureCode = type;
        result.FailureMessage = message;
        result.ResultingOrderId = null;
    }

    private static bool HasSummaryMismatch(ParsedPackingSlip parsed, out string? message)
    {
        message = null;
        if (!parsed.SummaryPageFound) return false;

        var parsedIds = parsed.OrderBlocks.Select(block => block.OrderIdentifier)
            .Where(identifier => !string.IsNullOrWhiteSpace(identifier))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var summaryIds = parsed.SummaryOrderIdentifiers.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (parsedIds.SetEquals(summaryIds)) return false;

        message = $"Packing-slip summary mismatch. Parsed orders: [{string.Join(", ", parsedIds)}]; "
            + $"summary orders: [{string.Join(", ", summaryIds)}].";
        return true;
    }

    private static ImportProgressUpdate Update(
        int detected, int processed, int succeeded, int failed, bool complete, ImportAttempt attempt) =>
        new(detected, processed, succeeded, failed, complete, attempt);
}
