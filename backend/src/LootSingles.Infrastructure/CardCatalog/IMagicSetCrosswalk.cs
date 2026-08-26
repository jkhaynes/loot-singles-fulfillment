namespace LootSingles.Infrastructure.CardCatalog;

public interface IMagicSetCrosswalk
{
    bool TryGetScryfallSetCodes(string tcgplayerSetName, out IReadOnlyList<string> setCodes);
}
