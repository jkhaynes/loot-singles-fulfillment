using System.Text.RegularExpressions;
using LootSingles.Infrastructure.Tcgplayer.Synthetic;

namespace LootSingles.UnitTests.Tcgplayer;

/// <summary>
/// 021 T005 (research R4, SC-003): the synthetic TCGplayer data the Development stub serves never
/// names a real TCGplayer host, so a local browser cannot request TCGplayer's CDN. Every image URL
/// is on a reserved <c>.test</c> or <c>.invalid</c> host and fails to load, which shows the
/// no-image placeholder ("no image is better than the wrong image").
/// </summary>
public sealed partial class SyntheticDataTests
{
    private const string ResourcePrefix = "LootSingles.Infrastructure.Tcgplayer.Synthetic.Data.";

    private static readonly string[] Names = typeof(SyntheticTcgplayerHandler)
        .Assembly.GetManifestResourceNames()
        .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal))
        .Select(name => name[ResourcePrefix.Length..])
        .ToArray();

    [Fact]
    public void EmbeddedResources_ArePresent()
    {
        Assert.Contains("products.json", Names);
        Assert.Contains("order-details.json", Names);
    }

    [Fact]
    public void NoEmbeddedResource_NamesARealTcgplayerHost()
    {
        foreach (var name in Names)
        {
            var text = SyntheticTcgplayerHandler.ReadData(name);
            Assert.DoesNotContain("tcgplayer.com", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("tcgplayer-cdn", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void EveryImageUrlHost_IsReservedForTesting()
    {
        var hosts = Names
            .SelectMany(name => UrlPattern().Matches(SyntheticTcgplayerHandler.ReadData(name)))
            .Select(match => new Uri(match.Value).Host)
            .ToList();

        Assert.NotEmpty(hosts);
        Assert.All(
            hosts,
            host =>
                Assert.True(
                    host.EndsWith(".test", StringComparison.OrdinalIgnoreCase)
                        || host.EndsWith(".invalid", StringComparison.OrdinalIgnoreCase),
                    $"Host '{host}' is not a reserved .test or .invalid name."
                )
        );
    }

    [GeneratedRegex(@"https?://[^""\s]+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlPattern();
}
