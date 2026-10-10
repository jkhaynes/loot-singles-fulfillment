using System.Text.Json;
using LootSingles.Application.Import;
using LootSingles.Infrastructure.Tcgplayer;

namespace LootSingles.UnitTests.Tcgplayer;

/// <summary>
/// research.md section 7 (field mapping), section 5 (product count is units) and section 14
/// (live-confirmed wording). Driven by the synthetic fixtures in LootSingles.Fixtures/Tcgplayer,
/// plus small inline items for the edge rules.
/// </summary>
public sealed class TcgplayerOrderTranslatorTests
{
    private static readonly TcgplayerOptions Options = new();

    // ---- Fixture loading -------------------------------------------------------------------

    private static string FixturePath(string file) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "Tcgplayer", file);

    private static (List<T> Results, int? TotalItems) LoadEnvelope<T>(string file)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(FixturePath(file)));
        var root = document.RootElement;
        var results = root.GetProperty("results").Deserialize<List<T>>()!;
        int? totalItems = root.TryGetProperty("totalItems", out var total)
            ? total.GetInt32()
            : null;
        return (results, totalItems);
    }

    private static readonly IReadOnlyDictionary<int, TcgplayerSku> Skus =
        LoadEnvelope<TcgplayerSku>("skus.json").Results.ToDictionary(sku => sku.SkuId);

    private static readonly IReadOnlyDictionary<int, TcgplayerProduct> Products =
        LoadEnvelope<TcgplayerProduct>("products.json").Results.ToDictionary(p => p.ProductId);

    private static TcgplayerOrderDetails Details(string orderNumber) =>
        LoadEnvelope<TcgplayerOrderDetails>("order-details.json")
            .Results.Single(order => order.OrderNumber == orderNumber);

    private static OrderCandidate TranslateFixture(string orderNumber, params string[] itemFiles)
    {
        var items = new List<TcgplayerOrderItem>();
        int? totalItems = null;
        foreach (var file in itemFiles)
        {
            var page = LoadEnvelope<TcgplayerOrderItem>(file);
            items.AddRange(page.Results);
            totalItems = page.TotalItems;
        }

        return TcgplayerOrderTranslator.Translate(
            Details(orderNumber),
            items,
            totalItems!.Value,
            Skus,
            Products,
            Options
        );
    }

    // ---- Inline helpers --------------------------------------------------------------------

    private static TcgplayerOrderItem Item(
        string? condition = "Near Mint",
        string? printing = "Normal",
        bool? isFoil = false,
        string? rarity = "Rare",
        int? quantity = 1,
        int? skuId = 7000001,
        string? productImageUrl = "https://img.example.test/synthetic/inline.jpg",
        string? productName = "Synthetic Dragonling"
    ) =>
        new()
        {
            SkuId = skuId,
            CategoryName = "Synthetic TCG",
            ProductName = productName,
            GroupName = "Synthetic Set Alpha",
            Condition = condition,
            Printing = printing,
            IsFoil = isFoil,
            Language = "English",
            Rarity = rarity,
            Quantity = quantity,
            ProductImageUrl = productImageUrl,
        };

    private static OrderCandidate TranslateOne(
        TcgplayerOrderItem item,
        IReadOnlyDictionary<int, TcgplayerProduct>? products = null,
        TcgplayerOptions? options = null
    ) =>
        TcgplayerOrderTranslator.Translate(
            new TcgplayerOrderDetails
            {
                OrderNumber = "SYN-9000-A1",
                OrderStatusTypeId = 2,
                ProductCount = item.Quantity,
            },
            [item],
            1,
            Skus,
            products ?? Products,
            options ?? Options
        );

    private static OrderLineCandidate OnlyLine(OrderCandidate candidate) =>
        Assert.Single(candidate.Lines);

    private static IReadOnlyDictionary<int, TcgplayerProduct> ProductWith(
        string? imageUrl,
        params (string Name, string Value)[] extendedData
    ) =>
        new Dictionary<int, TcgplayerProduct>
        {
            [8000001] = new()
            {
                ProductId = 8000001,
                ImageUrl = imageUrl,
                ExtendedData = extendedData
                    .Select(entry => new TcgplayerExtendedData
                    {
                        Name = entry.Name,
                        Value = entry.Value,
                    })
                    .ToList(),
            },
        };

    // ---- Pass-through fields ---------------------------------------------------------------

    [Fact]
    public void Normal_line_maps_every_field_and_passes_names_through_verbatim()
    {
        var candidate = TranslateFixture("SYN-0001-A1", "items-normal.json");

        Assert.Equal("SYN-0001-A1", candidate.SourceOrderIdentifier);
        Assert.Null(candidate.RejectedBySource);
        var line = OnlyLine(candidate);
        Assert.Equal("Synthetic TCG", line.ProductLine);
        Assert.Equal("Synthetic Dragonling", line.ProductName);
        Assert.Equal("Synthetic Set Alpha", line.Set);
        Assert.Equal("#001/100", line.CollectorNumber);
        Assert.Equal("Rare", line.Rarity);
        Assert.Equal("Near Mint", line.Condition);
        Assert.Null(line.Variant);
        Assert.Equal("English", line.Language);
        Assert.Equal("https://img.example.test/synthetic/7000001.jpg", line.ImageUrl);
        Assert.Equal(1, line.Quantity);
        Assert.Null(OrderCandidateValidator.Validate(candidate));
    }

    [Fact]
    public void Names_are_not_trimmed_or_reformatted()
    {
        var item = Item() with
        {
            CategoryName = "Synthetic  TCG",
            ProductName = "Synthetic Dragonling (Borderless)",
            GroupName = "Synthetic: Set - Alpha",
        };

        var line = OnlyLine(TranslateOne(item));

        Assert.Equal("Synthetic  TCG", line.ProductLine);
        Assert.Equal("Synthetic Dragonling (Borderless)", line.ProductName);
        Assert.Equal("Synthetic: Set - Alpha", line.Set);
    }

    // ---- Condition and variant -------------------------------------------------------------

    [Fact]
    public void Foil_reported_three_ways_gives_Foil_exactly_once()
    {
        // Live data (research.md section 14): condition "Near Mint Foil", printing "Foil", isFoil.
        var line = OnlyLine(TranslateFixture("SYN-0003-A1", "items-foil.json"));

        Assert.Equal("Near Mint", line.Condition);
        Assert.Equal("Foil", line.Variant);
    }

    [Fact]
    public void Condition_suffix_goes_through_the_condition_parser()
    {
        var line = OnlyLine(TranslateOne(Item(condition: "Near Mint Foil", isFoil: true)));

        Assert.Equal("Near Mint", line.Condition);
        Assert.Equal("Foil", line.Variant);
    }

    [Fact]
    public void Printing_matching_the_suffix_in_another_case_is_not_repeated()
    {
        var line = OnlyLine(
            TranslateOne(Item(condition: "Lightly Played Foil", printing: "foil", isFoil: true))
        );

        Assert.Equal("Lightly Played", line.Condition);
        Assert.Equal("Foil", line.Variant);
    }

    [Fact]
    public void Unknown_condition_gives_null_condition_which_the_validator_rejects()
    {
        var candidate = TranslateOne(Item(condition: "Mint-ish"));

        Assert.Null(OnlyLine(candidate).Condition);
        Assert.Null(candidate.RejectedBySource);
        var failure = OrderCandidateValidator.Validate(candidate);
        Assert.NotNull(failure);
        Assert.Equal(FailureType.MissingCondition, failure.Value.Type);
    }

    [Fact]
    public void Missing_condition_gives_null_condition_which_the_validator_rejects()
    {
        var candidate = TranslateOne(Item(condition: null));

        Assert.Null(OnlyLine(candidate).Condition);
        Assert.Equal(
            FailureType.MissingCondition,
            OrderCandidateValidator.Validate(candidate)?.Type
        );
    }

    [Fact]
    public void Normal_printing_is_dropped()
    {
        var line = OnlyLine(
            TranslateFixture("SYN-0008-A1", "items-normal-printing-lightly-played.json")
        );

        Assert.Equal("Lightly Played", line.Condition);
        Assert.Null(line.Variant);
    }

    [Fact]
    public void Other_printing_is_appended_as_the_variant()
    {
        var line = OnlyLine(TranslateOne(Item(printing: "1st Edition")));

        Assert.Equal("Near Mint", line.Condition);
        Assert.Equal("1st Edition", line.Variant);
    }

    [Fact]
    public void Printing_is_appended_after_a_different_condition_suffix()
    {
        var line = OnlyLine(
            TranslateOne(Item(condition: "Near Mint Foil", printing: "1st Edition", isFoil: true))
        );

        Assert.Equal("Foil, 1st Edition", line.Variant);
    }

    [Fact]
    public void IsFoil_with_no_foil_text_adds_Foil()
    {
        var line = OnlyLine(TranslateOne(Item(printing: "Normal", isFoil: true)));

        Assert.Equal("Near Mint", line.Condition);
        Assert.Equal("Foil", line.Variant);
    }

    [Fact]
    public void IsFoil_with_a_non_foil_printing_appends_Foil_after_it()
    {
        var line = OnlyLine(TranslateOne(Item(printing: "1st Edition", isFoil: true)));

        Assert.Equal("1st Edition, Foil", line.Variant);
    }

    [Fact]
    public void IsFoil_does_not_add_Foil_when_the_printing_already_names_a_foil()
    {
        var line = OnlyLine(TranslateOne(Item(printing: "Reverse Holofoil", isFoil: true)));

        Assert.Equal("Reverse Holofoil", line.Variant);
    }

    // ---- Collector number, rarity, image, language -----------------------------------------

    [Fact]
    public void Catalog_Number_becomes_a_hash_prefixed_collector_number()
    {
        var products = ProductWith(null, ("Number", "067/086"));

        Assert.Equal("#067/086", OnlyLine(TranslateOne(Item(), products)).CollectorNumber);
    }

    [Fact]
    public void Missing_catalog_Number_gives_null_collector_number()
    {
        var products = ProductWith(null, ("Rarity", "Rare"));

        Assert.Null(OnlyLine(TranslateOne(Item(), products)).CollectorNumber);
    }

    [Fact]
    public void Blank_catalog_Number_gives_null_collector_number()
    {
        var products = ProductWith(null, ("Number", "  "));

        Assert.Null(OnlyLine(TranslateOne(Item(), products)).CollectorNumber);
    }

    [Fact]
    public void Extended_data_names_come_from_the_options()
    {
        var products = ProductWith(null, ("CardNo", "042/100"), ("Tier", "Mythic"));
        var options = new TcgplayerOptions
        {
            CollectorNumberField = "CardNo",
            RarityField = "Tier",
        };

        var line = OnlyLine(TranslateOne(Item(rarity: null), products, options));

        Assert.Equal("#042/100", line.CollectorNumber);
        Assert.Equal("Mythic", line.Rarity);
    }

    [Fact]
    public void Sealed_line_has_no_collector_number_and_the_order_is_not_rejected_by_the_source()
    {
        var candidate = TranslateFixture("SYN-0009-A1", "items-sealed.json");

        Assert.Null(candidate.RejectedBySource);
        var line = OnlyLine(candidate);
        Assert.Null(line.CollectorNumber);
        Assert.Equal("Synthetic Booster Box", line.ProductName);
        Assert.Equal("https://img.example.test/synthetic/7000013.jpg", line.ImageUrl);
        Assert.Equal(1, line.Quantity);
    }

    [Fact]
    public void Line_without_a_number_passes_validation()
    {
        // A line with no catalog number imports like any other (FR-008, spec Edge Cases).
        var products = ProductWith("https://img.example.test/synthetic/box.jpg");

        var candidate = TranslateOne(Item(productImageUrl: null, rarity: null), products);

        Assert.Null(OnlyLine(candidate).CollectorNumber);
        Assert.Null(OrderCandidateValidator.Validate(candidate));
    }

    [Fact]
    public void Item_rarity_wins_over_the_catalog()
    {
        var line = OnlyLine(TranslateOne(Item(rarity: "Mythic")));

        Assert.Equal("Mythic", line.Rarity);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Rarity_falls_back_to_the_catalog_Rarity(string? itemRarity)
    {
        var line = OnlyLine(TranslateOne(Item(rarity: itemRarity)));

        Assert.Equal("Rare", line.Rarity);
    }

    [Fact]
    public void No_rarity_anywhere_gives_null()
    {
        var products = ProductWith(null, ("Number", "001/100"));

        Assert.Null(OnlyLine(TranslateOne(Item(rarity: null), products)).Rarity);
    }

    [Fact]
    public void Language_comes_from_the_item()
    {
        var line = OnlyLine(TranslateFixture("SYN-0004-A1", "items-non-english.json"));

        Assert.Equal("Japanese", line.Language);
        Assert.Equal("Synthetic Set Beta", line.Set);
    }

    [Fact]
    public void Image_falls_back_to_the_product_image()
    {
        var line = OnlyLine(TranslateOne(Item(productImageUrl: null)));

        Assert.Equal("https://img.example.test/synthetic/product-8000001.jpg", line.ImageUrl);
    }

    [Fact]
    public void Blank_item_image_falls_back_to_the_product_image()
    {
        var line = OnlyLine(TranslateOne(Item(productImageUrl: " ")));

        Assert.Equal("https://img.example.test/synthetic/product-8000001.jpg", line.ImageUrl);
    }

    [Fact]
    public void No_image_anywhere_gives_null()
    {
        var products = ProductWith(null, ("Number", "001/100"));

        Assert.Null(OnlyLine(TranslateOne(Item(productImageUrl: null), products)).ImageUrl);
    }

    [Fact]
    public void Unknown_sku_or_product_leaves_no_number_but_keeps_item_data()
    {
        var candidate = TranslateFixture("SYN-0010-A1", "items-catalog-not-found.json");

        Assert.Null(candidate.RejectedBySource);
        Assert.Equal(2, candidate.Lines.Count);
        foreach (var line in candidate.Lines)
        {
            Assert.Null(line.CollectorNumber);
            Assert.Equal("Rare", line.Rarity);
            Assert.NotNull(line.ImageUrl);
        }
        Assert.Null(OrderCandidateValidator.Validate(candidate));
    }

    [Fact]
    public void Missing_sku_id_leaves_no_catalog_data()
    {
        var line = OnlyLine(TranslateOne(Item(skuId: null, productImageUrl: null, rarity: null)));

        Assert.Null(line.CollectorNumber);
        Assert.Null(line.ImageUrl);
        Assert.Null(line.Rarity);
    }

    // ---- RawDescription --------------------------------------------------------------------

    [Fact]
    public void RawDescription_is_composed_from_the_mapped_fields()
    {
        var line = OnlyLine(TranslateFixture("SYN-0003-A1", "items-foil.json"));

        Assert.Equal(
            "Synthetic TCG - Synthetic Set Alpha - Synthetic Phoenix - #004/100 - Rare - Near Mint - Foil - English",
            line.RawDescription
        );
    }

    [Fact]
    public void RawDescription_skips_missing_fields()
    {
        var products = ProductWith(null);

        var line = OnlyLine(TranslateOne(Item(rarity: null) with { Language = null }, products));

        Assert.Equal(
            "Synthetic TCG - Synthetic Set Alpha - Synthetic Dragonling - Near Mint",
            line.RawDescription
        );
    }

    // ---- Quantity and completeness ---------------------------------------------------------

    [Fact]
    public void Quantities_greater_than_one_are_kept()
    {
        var candidate = TranslateFixture("SYN-0002-A1", "items-quantity-greater-than-one.json");

        Assert.Null(candidate.RejectedBySource);
        Assert.Equal([1, 2], candidate.Lines.Select(line => line.Quantity));
        Assert.Null(OrderCandidateValidator.Validate(candidate));
    }

    [Fact]
    public void All_pages_of_items_make_a_complete_order()
    {
        var candidate = TranslateFixture(
            "SYN-0005-A1",
            "items-paged-page1.json",
            "items-paged-page2.json"
        );

        Assert.Null(candidate.RejectedBySource);
        Assert.Equal(3, candidate.Lines.Count);
    }

    [Fact]
    public void Fewer_lines_than_totalItems_rejects_the_order_as_incomplete()
    {
        var candidate = TranslateFixture("SYN-0005-A1", "items-paged-page1.json");

        Assert.Equal(
            (
                FailureType.IncompleteOrder,
                "Order 'SYN-0005-A1': TCGplayer reported 3 lines but 2 were retrieved."
            ),
            candidate.RejectedBySource
        );
    }

    [Fact]
    public void More_lines_than_totalItems_rejects_the_order_as_incomplete()
    {
        var items = LoadEnvelope<TcgplayerOrderItem>("items-paged-page1.json").Results;

        var candidate = TcgplayerOrderTranslator.Translate(
            new TcgplayerOrderDetails { OrderNumber = "SYN-9001-A1", ProductCount = 2 },
            items,
            1,
            Skus,
            Products,
            Options
        );

        Assert.Equal(FailureType.IncompleteOrder, candidate.RejectedBySource?.Type);
    }

    [Fact]
    public void Quantities_not_matching_productCount_reject_the_order_as_incomplete()
    {
        var candidate = TranslateFixture("SYN-0006-A1", "items-count-mismatch.json");

        Assert.Equal(
            (
                FailureType.IncompleteOrder,
                "Order 'SYN-0006-A1': line quantities add up to 2, but TCGplayer reports a product count of 5."
            ),
            candidate.RejectedBySource
        );
        Assert.Equal(candidate.RejectedBySource, OrderCandidateValidator.Validate(candidate));
    }

    [Fact]
    public void Missing_productCount_rejects_the_order_as_incomplete()
    {
        var candidate = TcgplayerOrderTranslator.Translate(
            new TcgplayerOrderDetails { OrderNumber = "SYN-9002-A1", ProductCount = null },
            [Item()],
            1,
            Skus,
            Products,
            Options
        );

        Assert.Equal(
            (
                FailureType.IncompleteOrder,
                "Order 'SYN-9002-A1': TCGplayer reported no product count to check the lines against."
            ),
            candidate.RejectedBySource
        );
    }

    [Fact]
    public void Missing_quantity_is_left_to_the_validator()
    {
        var candidate = TcgplayerOrderTranslator.Translate(
            new TcgplayerOrderDetails { OrderNumber = "SYN-9003-A1", ProductCount = 1 },
            [Item(quantity: null)],
            1,
            Skus,
            Products,
            Options
        );

        Assert.Null(candidate.RejectedBySource);
        Assert.Null(OnlyLine(candidate).Quantity);
        Assert.Equal(
            FailureType.InvalidQuantity,
            OrderCandidateValidator.Validate(candidate)?.Type
        );
    }

    [Fact]
    public void Missing_product_name_is_left_to_the_validator()
    {
        var candidate = TranslateFixture("SYN-0007-A1", "items-missing-product-name.json");

        Assert.Null(candidate.RejectedBySource);
        Assert.Null(OnlyLine(candidate).ProductName);
        Assert.Equal(
            FailureType.MissingProductName,
            OrderCandidateValidator.Validate(candidate)?.Type
        );
    }

    [Fact]
    public void Missing_order_number_is_left_to_the_validator()
    {
        var candidate = TcgplayerOrderTranslator.Translate(
            new TcgplayerOrderDetails { OrderNumber = null, ProductCount = 5 },
            [Item()],
            1,
            Skus,
            Products,
            Options
        );

        Assert.Null(candidate.RejectedBySource);
        Assert.Equal(
            FailureType.MissingOrderIdentifier,
            OrderCandidateValidator.Validate(candidate)?.Type
        );
    }
}
