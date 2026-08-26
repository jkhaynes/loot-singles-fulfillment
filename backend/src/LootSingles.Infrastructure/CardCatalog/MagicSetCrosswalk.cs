using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LootSingles.Infrastructure.CardCatalog;

public sealed class MagicSetCrosswalk : IMagicSetCrosswalk
{
    private const string EmbeddedResourceName =
        "LootSingles.Infrastructure.CardCatalog.Data.tcgplayer-magic-to-scryfall-set-codes.json";

    private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _setCodesByName;

    private MagicSetCrosswalk(IReadOnlyDictionary<string, IReadOnlyList<string>> setCodesByName) =>
        _setCodesByName = setCodesByName;

    public static MagicSetCrosswalk LoadEmbedded()
    {
        using var stream =
            typeof(MagicSetCrosswalk).Assembly.GetManifestResourceStream(EmbeddedResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded Magic set crosswalk resource '{EmbeddedResourceName}' was not found."
            );
        return Load(stream);
    }

    public static MagicSetCrosswalk Load(Stream stream)
    {
        CrosswalkDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<CrosswalkDocument>(stream);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                "The Magic set crosswalk JSON is invalid.",
                exception
            );
        }

        if (document?.Mappings is null)
        {
            throw new InvalidOperationException(
                "The Magic set crosswalk must contain a 'mappings' object."
            );
        }

        var lookup = new Dictionary<string, IReadOnlyList<string>>(
            StringComparer.OrdinalIgnoreCase
        );
        foreach (var (displayName, mapping) in document.Mappings)
        {
            if (string.IsNullOrWhiteSpace(mapping?.NormalizedTcgplayerSetName))
            {
                throw new InvalidOperationException(
                    $"Magic set crosswalk mapping '{displayName}' must contain a nonblank 'normalizedTcgplayerSetName'."
                );
            }

            var normalizedName = Normalize(mapping.NormalizedTcgplayerSetName);
            var codes = mapping.ScryfallSetCodes ?? [];
            if (codes.Any(string.IsNullOrWhiteSpace))
            {
                throw new InvalidOperationException(
                    $"Magic set crosswalk mapping '{displayName}' must contain only nonblank Scryfall codes."
                );
            }

            var distinctCodes = codes.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (!lookup.TryAdd(normalizedName, distinctCodes))
            {
                throw new InvalidOperationException(
                    $"Magic set crosswalk contains duplicate normalized name '{normalizedName}'."
                );
            }
        }

        return new MagicSetCrosswalk(lookup);
    }

    public bool TryGetScryfallSetCodes(string tcgplayerSetName, out IReadOnlyList<string> setCodes)
    {
        if (_setCodesByName.TryGetValue(Normalize(tcgplayerSetName), out var found))
        {
            setCodes = found;
            return true;
        }

        setCodes = [];
        return false;
    }

    internal static string Normalize(string value)
    {
        var normalized = value
            .Normalize(NormalizationForm.FormKC)
            .Replace('\u2018', '\'')
            .Replace('\u2019', '\'');
        var result = new StringBuilder(normalized.Length);
        var pendingSpace = false;

        foreach (var character in normalized.Trim())
        {
            if (char.IsWhiteSpace(character))
            {
                pendingSpace = result.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                result.Append(' ');
                pendingSpace = false;
            }

            result.Append(character);
        }

        return result.ToString();
    }

    private sealed record CrosswalkDocument(
        [property: JsonPropertyName("mappings")]
            IReadOnlyDictionary<string, CrosswalkMapping?>? Mappings
    );

    private sealed record CrosswalkMapping(
        [property: JsonPropertyName("normalizedTcgplayerSetName")]
            string? NormalizedTcgplayerSetName,
        [property: JsonPropertyName("scryfallSetCodes")] IReadOnlyList<string>? ScryfallSetCodes
    );
}
