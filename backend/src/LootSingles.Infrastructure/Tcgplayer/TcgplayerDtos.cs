using System.Text.Json.Serialization;

namespace LootSingles.Infrastructure.Tcgplayer;

// TCGplayer response DTOs (data-model.md, "TCGplayer adapter types"). Each declares ONLY the fields
// the import reads. There is deliberately no customer, shippingAddress, email, name or orderValue
// property anywhere, so those values are never bound into objects (FR-017). Every field is
// nullable: a missing value must surface as missing, never as a default that looks real.

/// <summary>One row of <c>GET /stores/{storeKey}/orders/{orderNumbers}</c>.</summary>
public sealed record TcgplayerOrderDetails
{
    [JsonPropertyName("orderNumber")]
    public string? OrderNumber { get; init; }

    /// <summary>Total units in the order: the sum of line quantities (live-confirmed 2026-10-09).</summary>
    [JsonPropertyName("productCount")]
    public int? ProductCount { get; init; }
}

/// <summary>One row of <c>GET /stores/{storeKey}/orders/{orderNumber}/items?includeItemDetails=true</c>.</summary>
public sealed record TcgplayerOrderItem
{
    [JsonPropertyName("skuId")]
    public int? SkuId { get; init; }

    [JsonPropertyName("categoryName")]
    public string? CategoryName { get; init; }

    [JsonPropertyName("productName")]
    public string? ProductName { get; init; }

    [JsonPropertyName("groupName")]
    public string? GroupName { get; init; }

    [JsonPropertyName("condition")]
    public string? Condition { get; init; }

    [JsonPropertyName("printing")]
    public string? Printing { get; init; }

    [JsonPropertyName("isFoil")]
    public bool? IsFoil { get; init; }

    [JsonPropertyName("language")]
    public string? Language { get; init; }

    [JsonPropertyName("rarity")]
    public string? Rarity { get; init; }

    [JsonPropertyName("quantity")]
    public int? Quantity { get; init; }

    [JsonPropertyName("productImageUrl")]
    public string? ProductImageUrl { get; init; }
}

/// <summary>One row of <c>GET /catalog/skus/{skuIds}</c>.</summary>
public sealed record TcgplayerSku
{
    [JsonPropertyName("skuId")]
    public int? SkuId { get; init; }

    [JsonPropertyName("productId")]
    public int? ProductId { get; init; }
}

/// <summary>One row of <c>GET /catalog/products/{productIds}?getExtendedFields=true</c>.</summary>
public sealed record TcgplayerProduct
{
    [JsonPropertyName("productId")]
    public int? ProductId { get; init; }

    [JsonPropertyName("imageUrl")]
    public string? ImageUrl { get; init; }

    [JsonPropertyName("extendedData")]
    public IReadOnlyList<TcgplayerExtendedData>? ExtendedData { get; init; }
}

/// <summary>A named catalog value, for example <c>Number</c> or <c>Rarity</c>.</summary>
public sealed record TcgplayerExtendedData
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("value")]
    public string? Value { get; init; }
}

/// <summary>
/// The envelope every TCGplayer API response shares. <c>totalItems</c> appears only on paged
/// endpoints: on order search it counts orders, on order items it counts lines. Its
/// <c>success</c> and <c>errors</c> members are deliberately not bound: an id absent from
/// <c>results</c> is not found, and typing them would fail a response over their shape.
/// </summary>
public sealed record TcgplayerResponse<T>
{
    [JsonPropertyName("results")]
    public IReadOnlyList<T>? Results { get; init; }

    [JsonPropertyName("totalItems")]
    public int? TotalItems { get; init; }
}

/// <summary>
/// One row of <c>GET /stores/self</c>. The live member name for the store key is unverified
/// (ruling R28): the documentation calls it SellerKey, while the owner's live probe read
/// <c>storeKey</c>. Both are bound, and the client reads JSON with case-insensitive member names,
/// so <c>SellerKey</c> and <c>StoreKey</c> match too.
/// </summary>
public sealed record TcgplayerStoreSelf
{
    [JsonPropertyName("storeKey")]
    public string? StoreKey { get; init; }

    [JsonPropertyName("sellerKey")]
    public string? SellerKey { get; init; }

    /// <summary>The store key the store-scoped paths take: the first non-blank of the two.</summary>
    [JsonIgnore]
    public string? Key =>
        !string.IsNullOrWhiteSpace(StoreKey) ? StoreKey
        : !string.IsNullOrWhiteSpace(SellerKey) ? SellerKey
        : null;
}

/// <summary>
/// One row of <c>GET /stores/{storeKey}/orders/manifest</c>: the id lists the configured open
/// order names resolve in (FR-004). The manifest's other lists are not read.
/// </summary>
public sealed record TcgplayerOrderManifest
{
    [JsonPropertyName("orderStatusTypes")]
    public IReadOnlyList<TcgplayerManifestType>? OrderStatusTypes { get; init; }

    [JsonPropertyName("orderPickupStatusTypes")]
    public IReadOnlyList<TcgplayerManifestType>? OrderPickupStatusTypes { get; init; }

    [JsonPropertyName("orderTypes")]
    public IReadOnlyList<TcgplayerManifestType>? OrderTypes { get; init; }
}

/// <summary>A manifest entry, for example the order status <c>Ready To Ship</c>.</summary>
public sealed record TcgplayerManifestType
{
    [JsonPropertyName("id")]
    public int? Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }
}

/// <summary>Every item of one order, gathered from all pages, with the line count TCGplayer reported.</summary>
/// <param name="Items">The items, in the order TCGplayer returned them.</param>
/// <param name="TotalItems">The items endpoint's <c>totalItems</c>, which counts lines.</param>
public sealed record TcgplayerOrderItems(IReadOnlyList<TcgplayerOrderItem> Items, int TotalItems);
