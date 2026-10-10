using System.Runtime.CompilerServices;
using LootSingles.Domain.Orders;
using Microsoft.Extensions.Logging;

namespace LootSingles.Application.Import;

/// <summary>
/// The "Get new orders" import (research.md §3, §10): lists the store's open orders, reports the
/// ones already imported as duplicates without fetching them, fetches the rest in batches of
/// <see cref="ITcgplayerOrderFeed.PageSize"/>, and runs each through <see cref="OrderImporter"/>.
/// It streams the same <see cref="ImportProgressUpdate"/> shape as
/// <see cref="PackingSlipImportService"/>, so the controller can stream either source.
/// </summary>
/// <remarks>
/// A <see cref="TcgplayerFeedException"/> ends the attempt with the matching attempt-wide
/// <see cref="FailureType"/>; orders already committed stay, and pressing again resumes (FR-013).
/// Cancellation stops before the next order and leaves the attempt incomplete, as for PDF import.
/// Logging is the one completion entry per attempt (FR-025); nothing is logged per order.
/// </remarks>
public sealed class TcgplayerApiImportService(
    ITcgplayerOrderFeed feed,
    OrderImporter orderImporter,
    IImportPersistence persistence,
    ILogger<TcgplayerApiImportService> logger
)
{
    // contracts/import-api.md, "Already imported". The order's items are not fetched again.
    private const string AlreadyImportedMessage = "Already imported";

    public async IAsyncEnumerable<ImportProgressUpdate> ImportAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        var callCountAtStart = feed.CallCount;
        var attempt = new ImportAttempt
        {
            StartedAt = DateTimeOffset.UtcNow,
            Source = OrderImportSource.TcgplayerApi,
        };
        persistence.AddImportAttempt(attempt);
        // Save now so attempt.Id is assigned before any result or log statement references it.
        await persistence.SaveChangesAsync(cancellationToken);

        IReadOnlyList<string> openOrders;
        try
        {
            openOrders = await feed.GetOpenOrderNumbersAsync(cancellationToken);
        }
        catch (TcgplayerFeedException exception)
        {
            Fail(attempt, exception);
            openOrders = [];
        }

        var detected = openOrders.Count;
        var processed = 0;
        var succeeded = 0;
        var failed = 0;

        var newOrders = new List<string>();
        foreach (var orderNumber in openOrders)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!await persistence.OrderExistsAsync(orderNumber, cancellationToken))
            {
                newOrders.Add(orderNumber);
                continue;
            }

            attempt.ImportOrderResults.Add(
                new ImportOrderResult
                {
                    ImportAttemptId = attempt.Id,
                    SourceOrderIdentifier = orderNumber,
                    Outcome = ImportOutcome.Rejected,
                    FailureCode = FailureType.DuplicateOrder,
                    FailureMessage = AlreadyImportedMessage,
                }
            );
            failed++;
            processed++;
            await persistence.SaveChangesAsync(cancellationToken);
            yield return new(detected, processed, succeeded, failed, false, attempt);
        }

        foreach (var batch in newOrders.Chunk(feed.PageSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<OrderCandidate> candidates;
            try
            {
                candidates = await feed.GetOrdersAsync(batch, cancellationToken);
            }
            catch (TcgplayerFeedException exception)
            {
                Fail(attempt, exception);
                break;
            }

            foreach (var candidate in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = await orderImporter.ImportAsync(
                    attempt,
                    candidate,
                    OrderImportSource.TcgplayerApi,
                    beforeSave: null,
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
                yield return new(detected, processed, succeeded, failed, false, attempt);
            }
        }

        attempt.CompletedAt = DateTimeOffset.UtcNow;
        await persistence.SaveChangesAsync(cancellationToken);
        ImportAttemptLog.LogCompletion(
            logger,
            attempt,
            checked((int)(feed.CallCount - callCountAtStart))
        );
        yield return new(detected, processed, succeeded, failed, true, attempt);
    }

    /// <summary>
    /// Sets the attempt-wide failure for a feed failure. The service branches on the typed
    /// failure, never on message text (Principle XII). Only a malformed response has a message
    /// specific enough to show; the feed guarantees it names no body, token or key.
    /// </summary>
    private static void Fail(ImportAttempt attempt, TcgplayerFeedException exception)
    {
        (attempt.AttemptFailureCode, attempt.AttemptFailureMessage) = exception.Failure switch
        {
            TcgplayerFeedFailure.NotConfigured => (
                FailureType.TcgplayerNotConfigured,
                "Getting orders from TCGplayer isn't set up here. Use packing-slip upload instead."
            ),
            TcgplayerFeedFailure.Unavailable => (
                FailureType.TcgplayerUnavailable,
                "Couldn't reach TCGplayer. Orders already imported are kept. Try again in a few minutes, or upload a packing slip."
            ),
            TcgplayerFeedFailure.AccessRefused => (
                FailureType.TcgplayerAccessRefused,
                "TCGplayer refused the store's connection. A manager needs to check the TCGplayer API setup. You can upload a packing slip meanwhile."
            ),
            TcgplayerFeedFailure.ResponseInvalid => (
                FailureType.TcgplayerResponseInvalid,
                exception.Message
            ),
            _ => throw new ArgumentOutOfRangeException(
                nameof(exception),
                exception.Failure,
                "Unknown TCGplayer feed failure."
            ),
        };
    }
}
