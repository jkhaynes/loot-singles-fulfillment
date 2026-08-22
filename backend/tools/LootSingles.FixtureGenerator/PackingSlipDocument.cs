using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace LootSingles.FixtureGenerator;

public sealed record ProductLineSpec(
    string QuantityText,
    string RawDescription,
    string UnitPrice,
    string ExtPrice
);

public sealed record OrderPageSpec(
    string? OrderIdentifier,
    IReadOnlyList<ProductLineSpec> Lines,
    string ShipToName = "Fixture Customer",
    string ShipToAddress = "100 Example Lane"
);

/// <summary>
/// Builds packing-slip PDF fixtures matching the layout PdfPigPackingSlipParser expects:
/// one page per order (Ship To block, "Order Number:" line, a Quantity/Description/Price/Total
/// Price table with a trailing "&lt;N&gt; Total" row), followed by one summary page listing every
/// non-null order identifier as its own word token.
/// </summary>
public sealed class PackingSlipDocument(
    IReadOnlyList<OrderPageSpec> orders,
    bool includeSummaryPage = true
) : IDocument
{
    public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

    public DocumentSettings GetSettings() => DocumentSettings.Default;

    public void Compose(IDocumentContainer container)
    {
        foreach (var order in orders)
        {
            container.Page(page => ComposeOrderPage(page, order));
        }

        if (includeSummaryPage)
        {
            container.Page(page => ComposeSummaryPage(page, orders));
        }
    }

    private static void ComposeOrderPage(PageDescriptor page, OrderPageSpec order)
    {
        page.Size(PageSizes.Letter);
        page.Margin(36);
        page.DefaultTextStyle(style => style.FontSize(9).FontFamily(Fonts.Arial));

        page.Content()
            .Column(column =>
            {
                column.Spacing(6);
                column.Item().Text($"Ship To: {order.ShipToName}");
                column.Item().Text(order.ShipToAddress);
                column
                    .Item()
                    .PaddingTop(15)
                    .Text(text =>
                    {
                        text.Span("Order Number: ");
                        if (!string.IsNullOrEmpty(order.OrderIdentifier))
                        {
                            text.Span(order.OrderIdentifier);
                        }
                    });

                column
                    .Item()
                    .PaddingTop(15)
                    .Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.ConstantColumn(50);
                            columns.ConstantColumn(370);
                            columns.ConstantColumn(60);
                            columns.ConstantColumn(60);
                        });

                        table.Header(header =>
                        {
                            header.Cell().Text("Quantity");
                            header.Cell().Text("Description");
                            header.Cell().Text("Price");
                            header.Cell().Text("Total Price");
                        });

                        foreach (var line in order.Lines)
                        {
                            table.Cell().Text(line.QuantityText);
                            table.Cell().Text(line.RawDescription);
                            table.Cell().Text(line.UnitPrice);
                            table.Cell().Text(line.ExtPrice);
                        }

                        var totalQuantity = order.Lines.Sum(line =>
                            int.TryParse(line.QuantityText, out var qty) ? Math.Max(qty, 0) : 0
                        );
                        table.Cell().Text(totalQuantity.ToString());
                        table.Cell().Text("Total");
                        table.Cell().Text(string.Empty);
                        table.Cell().Text(string.Empty);
                    });
            });
    }

    private static void ComposeSummaryPage(PageDescriptor page, IReadOnlyList<OrderPageSpec> orders)
    {
        page.Size(PageSizes.Letter);
        page.Margin(36);
        page.DefaultTextStyle(style => style.FontSize(9).FontFamily(Fonts.Arial));

        page.Content()
            .Column(column =>
            {
                column.Spacing(4);
                foreach (
                    var identifier in orders
                        .Select(order => order.OrderIdentifier)
                        .Where(identifier => !string.IsNullOrEmpty(identifier))
                )
                {
                    column.Item().Text(identifier!);
                }
            });
    }
}
