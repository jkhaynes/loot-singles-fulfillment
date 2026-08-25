namespace LootSingles.UnitTests.CardCatalog;

/// <summary>
/// Hand-rolled <see cref="IHttpClientFactory"/> stub (research.md §9 - no mocking library in this
/// codebase) that always returns the same pre-built <see cref="HttpClient"/>, regardless of the
/// requested client name.
/// </summary>
public sealed class SingleClientHttpClientFactory(HttpClient httpClient) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => httpClient;
}
