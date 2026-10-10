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

    [JsonPropertyName("orderStatusTypeId")]
    public int? OrderStatusTypeId { get; init; }

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
