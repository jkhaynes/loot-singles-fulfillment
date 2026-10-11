using Microsoft.Extensions.Configuration;

namespace LootSingles.Infrastructure.Tcgplayer;

/// <summary>
/// Settings for the TCGplayer API import (feature 020, contracts/configuration.md). Bound by hand
/// and validated at startup, like <c>LockoutOptions</c>. The three secrets only ever come from
/// user secrets or deployment configuration, never from a checked-in appsettings file.
/// </summary>
public sealed class TcgplayerOptions
{
    public const string SectionName = "Tcgplayer";

    /// <summary>
    /// Stage and production share one set of keys under a 300-calls-per-minute agreement limit, so
    /// each process may be configured up to half of it.
    /// </summary>
    public const int MaxCallsPerMinute = 150;

    public string? PublicKey { get; init; }
    public string? PrivateKey { get; init; }
    public string? AccessToken { get; init; }
    public string? StoreKey { get; init; }

    /// <summary>Order status names that make a shipped order open (FR-004).</summary>
    public IReadOnlyList<string> OpenOrderStatuses { get; init; } = ["Processing", "Ready To Ship"];

    /// <summary>Pickup status names that make an in-store pickup order open (FR-004).</summary>
    public IReadOnlyList<string> OpenPickupStatuses { get; init; } = ["Received"];

    /// <summary>Order type names that can be open (FR-004); Direct orders are not.</summary>
    public IReadOnlyList<string> OrderTypes { get; init; } = ["Normal"];
    public int CallsPerMinute { get; init; } = 120;
    public int PageSize { get; init; } = 50;
    public string CollectorNumberField { get; init; } = "Number";
    public string RarityField { get; init; } = "Rarity";
    public string BaseUrl { get; init; } = "https://api.tcgplayer.com/";
    public string ApiVersion { get; init; } = "v1.39.0";

    /// <summary>
    /// True only when all three secrets are present. When false the app still starts and PDF import
    /// keeps working; "Get new orders" reports not configured without calling TCGplayer.
    /// </summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(PublicKey)
        && !string.IsNullOrWhiteSpace(PrivateKey)
        && !string.IsNullOrWhiteSpace(AccessToken);

    /// <summary>Binds the <c>Tcgplayer</c> section and throws if a value is invalid.</summary>
    public static TcgplayerOptions FromConfiguration(IConfiguration configuration)
    {
        var section = configuration.GetSection(SectionName);
        var defaults = new TcgplayerOptions();

        var options = new TcgplayerOptions
        {
            PublicKey = section[nameof(PublicKey)],
            PrivateKey = section[nameof(PrivateKey)],
            AccessToken = section[nameof(AccessToken)],
            StoreKey = NullIfBlank(section[nameof(StoreKey)]),
            OpenOrderStatuses = ReadNames(
                section,
                nameof(OpenOrderStatuses),
                defaults.OpenOrderStatuses
            ),
            OpenPickupStatuses = ReadNames(
                section,
                nameof(OpenPickupStatuses),
                defaults.OpenPickupStatuses
            ),
            OrderTypes = ReadNames(section, nameof(OrderTypes), defaults.OrderTypes),
            CallsPerMinute = ReadInt(section, nameof(CallsPerMinute), defaults.CallsPerMinute),
            PageSize = ReadInt(section, nameof(PageSize), defaults.PageSize),
            CollectorNumberField =
                NullIfBlank(section[nameof(CollectorNumberField)]) ?? defaults.CollectorNumberField,
            RarityField = NullIfBlank(section[nameof(RarityField)]) ?? defaults.RarityField,
            BaseUrl = NullIfBlank(section[nameof(BaseUrl)]) ?? defaults.BaseUrl,
            ApiVersion = NullIfBlank(section[nameof(ApiVersion)]) ?? defaults.ApiVersion,
        };

        if (options.CallsPerMinute < 1 || options.CallsPerMinute > MaxCallsPerMinute)
        {
            throw new InvalidOperationException(
                $"Tcgplayer:CallsPerMinute must be between 1 and {MaxCallsPerMinute}."
            );
        }

        // Paging asks for PageSize at a time; below one it could never make progress.
        if (options.PageSize < 1)
        {
            throw new InvalidOperationException("Tcgplayer:PageSize must be at least 1.");
        }

        return options;
    }

    // A list key that is absent takes its default. One that is present must name something: an
    // empty filter could mean "every order", which is never what an import wants.
    private static IReadOnlyList<string> ReadNames(
        IConfigurationSection section,
        string key,
        IReadOnlyList<string> fallback
    )
    {
        var list = section.GetSection(key);
        var entries = list.GetChildren().ToList();
        if (entries.Count == 0)
        {
            // A single value (Tcgplayer__OpenOrderStatuses=... without __0) is not a list. Taking
            // the defaults here would hide that the setting had no effect.
            return string.IsNullOrWhiteSpace(list.Value)
                ? fallback
                : throw new InvalidOperationException(
                    $"Tcgplayer:{key} is a list. Set each name as its own entry, for example "
                        + $"Tcgplayer__{key}__0 (or Tcgplayer:{key}:0 in a JSON file)."
                );
        }

        var names = entries
            .Select(child => child.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToList();
        return names.Count > 0
            ? names
            : throw new InvalidOperationException($"Tcgplayer:{key} must name at least one value.");
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private static int ReadInt(IConfigurationSection section, string key, int fallback)
    {
        var raw = section[key];
        if (string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }

        return int.TryParse(raw, out var parsed)
            ? parsed
            : throw new InvalidOperationException($"Tcgplayer:{key} must be a whole number.");
    }
}
