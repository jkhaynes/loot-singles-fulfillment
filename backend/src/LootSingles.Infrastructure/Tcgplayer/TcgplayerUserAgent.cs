using System.Reflection;

namespace LootSingles.Infrastructure.Tcgplayer;

/// <summary>
/// The User-Agent every TCGplayer request carries (FR-022, research.md §11): the business, the
/// application and its accurate version. The version is the assembly's InformationalVersion,
/// stamped at publish time, with any SourceLink "+hash" suffix trimmed.
/// </summary>
public static class TcgplayerUserAgent
{
    public static string Value { get; } =
        For(
            typeof(TcgplayerUserAgent)
                .Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion
                ?? "0.0.0"
        );

    public static string For(string informationalVersion) =>
        $"LootSinglesFulfillment/{informationalVersion.Split('+')[0]} (Loot Investments LLC)";
}
