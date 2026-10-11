using LootSingles.Application.Import;

namespace LootSingles.UnitTests.Import;

/// <summary>
/// Runs one slip line through the extractor and the shared validator, as the importer does, so
/// line-level tests can assert on a single outcome.
/// </summary>
internal sealed record LineOutcome(
    OrderLineCandidate? OrderLine,
    FailureType? FailureType,
    string? FailureMessage
)
{
    public bool IsValid => FailureType is null;

    public static LineOutcome Run(RawProductLine source)
    {
        var candidate = OrderLineExtractor.Extract(
            new RawOrderBlock
            {
                OrderIdentifier = "SYN-0001",
                ProductLines = [source],
                PageNumbers = [1],
            }
        );
        return OrderCandidateValidator.Validate(candidate) is { } failure
            ? new LineOutcome(null, failure.Type, failure.Message)
            : new LineOutcome(candidate.Lines[0], null, null);
    }
}
