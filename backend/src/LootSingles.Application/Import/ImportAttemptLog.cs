using System.Text;
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
        var failed = attempt.ImportOrderResults.Count(result =>
            result.Outcome == ImportOutcome.Rejected
        );

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

        if (attempt.AttemptFailureCode is { } attemptFailureType)
        {
            template.Append(" AttemptFailureType={AttemptFailureType}.");
            args.Add(attemptFailureType);
        }

        var breakdown = attempt
            .ImportOrderResults.Where(result =>
                result.Outcome == ImportOutcome.Rejected && result.FailureCode is not null
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
