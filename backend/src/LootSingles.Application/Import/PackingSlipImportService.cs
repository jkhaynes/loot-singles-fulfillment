using System.Runtime.CompilerServices;
using LootSingles.Application.Persistence;
using LootSingles.Domain.Orders;
using Microsoft.Extensions.Logging;

namespace LootSingles.Application.Import;

public sealed class PackingSlipImportService(
    IPackingSlipParser parser,
    IPackingSlipSlicer slicer,
    OrderImporter orderImporter,
    IImportPersistence persistence,
    ILogger<PackingSlipImportService> logger
) : IPackingSlipImportService
{
    private const string UnreadablePdfMessage =
        "The supplied PDF could not be read as a TCGplayer packing slip.";

    public async IAsyncEnumerable<ImportProgressUpdate> ImportAsync(
        Stream packingSlipPdf,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(packingSlipPdf);

        // The document is read twice — parsed for order data, then sliced for each order's
        // slip. Rewinding costs nothing when the stream supports it, which an uploaded form
        // file's does; anything else is buffered, bounded by the 25MB upload cap the
        // controller already enforces (research.md §3).
        var documentBytes = await ReadAllBytesAsync(packingSlipPdf, cancellationToken);

        var attempt = new ImportAttempt
        {
            StartedAt = DateTimeOffset.UtcNow,
            Source = OrderImportSource.PackingSlipPdf,
        };
        persistence.AddImportAttempt(attempt);
        // Save now so attempt.Id is assigned before any log statement can reference it.
        await persistence.SaveChangesAsync(cancellationToken);

        ParsedPackingSlip? parsed = null;
        string? unreadableMessage = null;
        await using var parseEnumerator = parser
            .ParseAsync(new MemoryStream(documentBytes, writable: false), cancellationToken)
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
                unreadableMessage = UnreadablePdfMessage;
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
            ImportAttemptLog.LogCompletion(logger, attempt);
            yield return Update(0, 0, 0, 0, true, attempt);
            yield break;
        }

        if (parsed.OrderBlocks.Count == 0)
        {
            attempt.AttemptFailureCode = FailureType.UnreadablePdf;
            attempt.AttemptFailureMessage =
                "The supplied PDF does not contain any recognizable order pages from a packing slip.";
            await CompleteAttemptAsync(attempt, cancellationToken);
            ImportAttemptLog.LogCompletion(logger, attempt);
            yield return Update(0, 0, 0, 0, true, attempt);
            yield break;
        }

        if (HasSummaryMismatch(parsed, out var mismatchMessage))
        {
            attempt.AttemptFailureCode = FailureType.SummaryMismatch;
            attempt.AttemptFailureMessage = mismatchMessage;
        }

        var processed = 0;
        var succeeded = 0;
        var failed = 0;
        var withoutPackingSlip = 0;
        foreach (var block in parsed.OrderBlocks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = OrderLineExtractor.Extract(block);
            var result = await orderImporter.ImportAsync(
                attempt,
                candidate,
                OrderImportSource.PackingSlipPdf,
                order =>
                {
                    if (!TryAttachPackingSlip(order, documentBytes, block))
                    {
                        withoutPackingSlip++;
                    }
                },
                cancellationToken
            );
            if (result.Outcome == ImportOutcome.Succeeded)
            {
                succeeded++;
            }
            else
            {
                failed++;
            }

            processed++;
            await persistence.SaveChangesAsync(cancellationToken);
            yield return Update(
                parsed.OrderBlocks.Count,
                processed,
                succeeded,
                failed,
                false,
                attempt
            );
        }

        await CompleteAttemptAsync(attempt, cancellationToken);
        ImportAttemptLog.LogCompletion(logger, attempt);
        LogPackingSlipFailures(attempt, withoutPackingSlip, succeeded);
        yield return Update(parsed.OrderBlocks.Count, processed, succeeded, failed, true, attempt);
    }

    private async Task CompleteAttemptAsync(
        ImportAttempt attempt,
        CancellationToken cancellationToken
    )
    {
        attempt.CompletedAt = DateTimeOffset.UtcNow;
        await persistence.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Slices this order's pages out of the batch and attaches them. Returns false when no slip
    /// could be produced, which the caller counts — this must not log per order.
    /// </summary>
    /// <remarks>
    /// <b>This step may never fail its caller (FR-021).</b> §26 and Constitution V bias this
    /// pipeline toward rejecting questionable data, and it would be easy to apply that here —
    /// but §26's concern is order-data integrity, and a missing slip is not order corruption.
    /// The order's lines parsed successfully and are authoritative; the slip is a convenience
    /// for a workflow that happens later, and the packing desk already treats its absence as a
    /// normal state (FR-022).
    /// </remarks>
    private bool TryAttachPackingSlip(Order order, byte[] documentBytes, RawOrderBlock block)
    {
        var slip = slicer.Slice(documentBytes, block.PageNumbers);

        if (slip is null)
        {
            return false;
        }

        order.PackingSlip = new OrderPackingSlip
        {
            Content = slip,
            StoredAt = DateTimeOffset.UtcNow,
        };

        return true;
    }

    /// <summary>
    /// One attempt-level entry for slips that could not be produced, never one per order.
    /// </summary>
    /// <remarks>
    /// A 200-order batch whose slicing fails would otherwise emit 200 warnings — the
    /// per-loop-iteration logging the constitution's observability rule forbids, and useless
    /// besides. An operator wants to know that slips stopped being produced and roughly how
    /// widely, which is one number.
    /// </remarks>
    private void LogPackingSlipFailures(
        ImportAttempt attempt,
        int withoutPackingSlip,
        int succeeded
    )
    {
        if (withoutPackingSlip == 0)
        {
            return;
        }

        logger.LogWarning(
            "Import attempt {ImportAttemptId}: {WithoutPackingSlipCount} of {SucceededCount} imported orders were stored without a packing slip.",
            attempt.Id,
            withoutPackingSlip,
            succeeded
        );
    }

    private static async Task<byte[]> ReadAllBytesAsync(
        Stream stream,
        CancellationToken cancellationToken
    )
    {
        if (stream is MemoryStream alreadyBuffered)
        {
            return alreadyBuffered.ToArray();
        }

        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }

    private static bool HasSummaryMismatch(ParsedPackingSlip parsed, out string? message)
    {
        message = null;
        if (!parsed.SummaryPageFound)
            return false;

        var parsedIds = parsed
            .OrderBlocks.Select(block => block.OrderIdentifier)
            .Where(identifier => !string.IsNullOrWhiteSpace(identifier))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var summaryIds = parsed.SummaryOrderIdentifiers.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (parsedIds.SetEquals(summaryIds))
            return false;

        message =
            $"Packing-slip summary mismatch. Parsed orders: [{string.Join(", ", parsedIds)}]; "
            + $"summary orders: [{string.Join(", ", summaryIds)}].";
        return true;
    }

    private static ImportProgressUpdate Update(
        int detected,
        int processed,
        int succeeded,
        int failed,
        bool complete,
        ImportAttempt attempt
    ) => new(detected, processed, succeeded, failed, complete, attempt);
}
