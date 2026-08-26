using LootSingles.Infrastructure.CardCatalog;

namespace LootSingles.UnitTests.CardCatalog;

public sealed class HyphenatedSetNameNormalizerTests
{
    [Fact]
    public void NormalizeCandidates_FirstCandidateIsTheOriginalSetName()
    {
        var candidates = HyphenatedSetNameNormalizer.NormalizeCandidates(
            "Universes Beyond: The Lord of the Rings: Tales of Middle- earth"
        );

        Assert.Equal(
            "Universes Beyond: The Lord of the Rings: Tales of Middle- earth",
            candidates[0]
        );
    }

    [Fact]
    public void NormalizeCandidates_MidWordHyphenLineWrap_AddsCollapsedCandidate()
    {
        var candidates = HyphenatedSetNameNormalizer.NormalizeCandidates(
            "Universes Beyond: The Lord of the Rings: Tales of Middle- earth"
        );

        Assert.Contains(
            "Universes Beyond: The Lord of the Rings: Tales of Middle-earth",
            candidates
        );
    }

    [Fact]
    public void NormalizeCandidates_SpacedDashSeparator_DoesNotCollapse()
    {
        var candidates = HyphenatedSetNameNormalizer.NormalizeCandidates("Foo - Bar");

        Assert.Single(candidates);
        Assert.Equal("Foo - Bar", candidates[0]);
    }

    [Fact]
    public void NormalizeCandidates_NoHyphen_ReturnsOnlyTheOriginal()
    {
        var candidates = HyphenatedSetNameNormalizer.NormalizeCandidates("Secret Lair Drop Series");

        Assert.Single(candidates);
        Assert.Equal("Secret Lair Drop Series", candidates[0]);
    }
}
