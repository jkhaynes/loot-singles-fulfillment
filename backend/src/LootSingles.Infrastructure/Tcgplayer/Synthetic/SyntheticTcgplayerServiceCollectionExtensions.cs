using Microsoft.Extensions.DependencyInjection;

namespace LootSingles.Infrastructure.Tcgplayer.Synthetic;

public static class SyntheticTcgplayerServiceCollectionExtensions
{
    /// <summary>
    /// Makes the synthetic fixtures the PRIMARY handler of the TCGplayer client, so the real auth,
    /// rate-limit, timeout and User-Agent handlers still run in front of it and no request can leave
    /// the process. Offers the eight orders that import cleanly (SYN-0001..0005 and 0008..0010).
    /// </summary>
    public static IHttpClientBuilder AddSyntheticTcgplayer(this IHttpClientBuilder builder) =>
        builder.ConfigurePrimaryHttpMessageHandler(() =>
            new SyntheticTcgplayerHandler
            {
                OpenOrderNumbers =
                [
                    .. new[] { 1, 2, 3, 4, 5, 8, 9, 10 }.Select(
                        SyntheticTcgplayerHandler.OrderNumber
                    ),
                ],
            }
        );
}
