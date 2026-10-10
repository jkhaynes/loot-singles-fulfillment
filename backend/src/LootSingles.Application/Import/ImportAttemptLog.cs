using System.Text;
using Microsoft.Extensions.Logging;

namespace LootSingles.Application.Import;

/// <summary>The single completion log entry for an import attempt, shared by every import source.</summary>
public static class ImportAttemptLog
{
    public static void LogCompletion(ILogger logger, ImportAttempt attempt)
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
                "Import attempt {ImportId} failed before any orders could be evaluated: {AttemptFailureType}. Detected {OrdersDetected}, succeeded {OrdersSucceeded}, failed {OrdersFailed}.",
                attempt.Id,
                attempt.AttemptFailureCode,
                detected,
                succeeded,
                failed
            );
            return;
        }

        if (failed == 0 && attempt.AttemptFailureCode is null)
        {
            logger.LogInformation(
                "Import attempt {ImportId} completed successfully. Detected {OrdersDetected}, succeeded {OrdersSucceeded}, failed {OrdersFailed}.",
                attempt.Id,
                detected,
                succeeded,
                failed
            );
            return;
        }

        // Built dynamically (not a static ILogger template) because the per-FailureType breakdown
        // has variable cardinality; do not collapse this back to a static template.
        var template = new StringBuilder(
            "Import attempt {ImportId} completed with failures. Detected {OrdersDetected}, succeeded {OrdersSucceeded}, failed {OrdersFailed}."
        );
        var args = new List<object?> { attempt.Id, detected, succeeded, failed };

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
