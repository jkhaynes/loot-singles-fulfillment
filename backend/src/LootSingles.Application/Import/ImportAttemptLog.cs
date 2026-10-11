using System.Text;
using LootSingles.Domain.Orders;
using Microsoft.Extensions.Logging;

namespace LootSingles.Application.Import;

/// <summary>The single completion log entry for an import attempt, shared by every import source.</summary>
public static class ImportAttemptLog
{
    /// <param name="callCount">
    /// TCGplayer calls this attempt made (the limiter's total at the end minus at the start),
    /// logged as <c>{CallCount}</c>. Null for sources that make no TCGplayer calls (PDF).
    /// </param>
    public static void LogCompletion(ILogger logger, ImportAttempt attempt, int? callCount = null)
    {
        var detected = attempt.ImportOrderResults.Count;
        var succeeded = attempt.ImportOrderResults.Count(result =>
            result.Outcome == ImportOutcome.Succeeded
        );
        // On an API import an already-imported order is a normal outcome (open orders come back on
        // every press until they ship), so it is reported as a count, not a failure (spec 020
        // FR-025). PDF imports keep 006 FR-004 exactly: duplicates are failures in the breakdown.
        var apiSource = attempt.Source == OrderImportSource.TcgplayerApi;
        var alreadyImported = apiSource
            ? attempt.ImportOrderResults.Count(result =>
                result.Outcome == ImportOutcome.Rejected
                && result.FailureCode == FailureType.DuplicateOrder
            )
            : 0;
        var failed =
            attempt.ImportOrderResults.Count(result => result.Outcome == ImportOutcome.Rejected)
            - alreadyImported;

        if (attempt.AttemptFailureCode is not null && detected == 0)
        {
            logger.LogWarning(
                "Import attempt {ImportId} ({Source}) failed before any orders could be evaluated: {AttemptFailureType}. Detected {OrdersDetected}, succeeded {OrdersSucceeded}, failed {OrdersFailed}. CallCount={CallCount}.",
                attempt.Id,
                attempt.Source,
                attempt.AttemptFailureCode,
                detected,
                succeeded,
                failed,
                callCount
            );
            return;
        }

        if (failed == 0 && attempt.AttemptFailureCode is null)
        {
            if (apiSource)
            {
                logger.LogInformation(
                    "Import attempt {ImportId} ({Source}) completed successfully. Detected {OrdersDetected}, succeeded {OrdersSucceeded}, failed {OrdersFailed}, already imported {OrdersAlreadyImported}. CallCount={CallCount}.",
                    attempt.Id,
                    attempt.Source,
                    detected,
                    succeeded,
                    failed,
                    alreadyImported,
                    callCount
                );
                return;
            }

            logger.LogInformation(
                "Import attempt {ImportId} ({Source}) completed successfully. Detected {OrdersDetected}, succeeded {OrdersSucceeded}, failed {OrdersFailed}. CallCount={CallCount}.",
                attempt.Id,
                attempt.Source,
                detected,
                succeeded,
                failed,
                callCount
            );
            return;
        }

        // Built dynamically (not a static ILogger template) because the per-FailureType breakdown
        // has variable cardinality; do not collapse this back to a static template.
        var template = new StringBuilder(
            "Import attempt {ImportId} ({Source}) completed with failures. Detected {OrdersDetected}, succeeded {OrdersSucceeded}, failed {OrdersFailed}. CallCount={CallCount}."
        );
        var args = new List<object?>
        {
            attempt.Id,
            attempt.Source,
            detected,
            succeeded,
            failed,
            callCount,
        };

        if (apiSource)
        {
            template.Append(" AlreadyImported={OrdersAlreadyImported}.");
            args.Add(alreadyImported);
        }

        if (attempt.AttemptFailureCode is { } attemptFailureType)
        {
            template.Append(" AttemptFailureType={AttemptFailureType}.");
            args.Add(attemptFailureType);
        }

        var breakdown = attempt
            .ImportOrderResults.Where(result =>
                result.Outcome == ImportOutcome.Rejected
                && result.FailureCode is not null
                && !(apiSource && result.FailureCode == FailureType.DuplicateOrder)
            )
            .GroupBy(result => result.FailureCode!.Value)
            .OrderBy(group => group.Key);
        foreach (var group in breakdown)
        {
            template.Append($" {group.Key}: {{{group.Key}Count}} [{{{group.Key}Ids}}]");
            args.Add(group.Count());
            args.Add(
                string.Join(
                    ",",
                    group.Select(result =>
                        string.IsNullOrWhiteSpace(result.SourceOrderIdentifier)
                            ? "(missing)"
                            : result.SourceOrderIdentifier
                    )
                )
            );
        }

        logger.LogWarning(template.ToString(), args.ToArray());
    }
}
