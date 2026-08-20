using System.Text.RegularExpressions;
using LootSingles.Application.Import;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace LootSingles.Infrastructure.Import;

public sealed partial class PdfPigPackingSlipParser : IPackingSlipParser
{
    public ParsedPackingSlip Parse(Stream packingSlipPdf)
    {
        ArgumentNullException.ThrowIfNull(packingSlipPdf);

        using var document = PdfDocument.Open(packingSlipPdf);
        var orderBlocks = new List<RawOrderBlock>();
        var summaryOrderIdentifiers = new List<string>();
        var summaryPageFound = false;

        foreach (var page in document.GetPages())
        {
            var words = page.GetWords().ToList();
            var lines = GroupIntoLines(words);
            var tableHeader = FindTableHeader(lines);

            if (tableHeader is not null)
            {
                orderBlocks.Add(new RawOrderBlock
                {
                    OrderIdentifier = ExtractOrderIdentifier(lines),
                    ProductLines = ExtractProductLines(words, lines, tableHeader),
                });
                continue;
            }

            var identifiers = words
                .Select(word => word.Text.Trim())
                .Where(text => SummaryOrderIdentifierPattern().IsMatch(text))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (identifiers.Count > 0)
            {
                summaryPageFound = true;
                summaryOrderIdentifiers.AddRange(identifiers);
            }
        }

        return new ParsedPackingSlip
        {
            OrderBlocks = orderBlocks,
            SummaryPageFound = summaryPageFound,
            SummaryOrderIdentifiers = summaryOrderIdentifiers,
        };
    }

    private static string? ExtractOrderIdentifier(IReadOnlyList<TextLine> lines)
    {
        var anchorLine = lines
            .Where(line => ContainsSequence(line.Words, "Order", "Number:"))
            .OrderByDescending(line => line.Bottom)
            .FirstOrDefault();

        if (anchorLine is null)
        {
            return null;
        }

        var numberLabelIndex = anchorLine.Words.FindIndex(
            word => word.Text.Equals("Number:", StringComparison.OrdinalIgnoreCase));
        var identifier = string.Join(
            " ",
            anchorLine.Words.Skip(numberLabelIndex + 1).Select(word => word.Text));

        return string.IsNullOrWhiteSpace(identifier) ? null : identifier;
    }

    private static IReadOnlyList<RawProductLine> ExtractProductLines(
        IReadOnlyList<Word> words,
        IReadOnlyList<TextLine> lines,
        TextLine tableHeader)
    {
        var quantityHeader = tableHeader.Words.Single(word => Normalize(word.Text) == "quantity");
        var descriptionHeader = tableHeader.Words.Single(word => Normalize(word.Text) == "description");
        var priceHeader = tableHeader.Words.First(word => Normalize(word.Text) == "price");

        var totalLine = lines
            .Where(line => line.Bottom < tableHeader.Bottom)
            .Where(line => line.Words.Count <= 4)
            .Where(line => line.Words.Any(word =>
                Normalize(word.Text) == "total"
                && Math.Abs(word.BoundingBox.Left - descriptionHeader.BoundingBox.Left) < 8))
            .OrderByDescending(line => line.Bottom)
            .FirstOrDefault();

        if (totalLine is null)
        {
            return Array.Empty<RawProductLine>();
        }

        var quantityWords = words
            .Where(word => word.BoundingBox.Left >= quantityHeader.BoundingBox.Left - 8)
            .Where(word => word.BoundingBox.Right < descriptionHeader.BoundingBox.Left - 4)
            .Where(word => word.BoundingBox.Bottom < quantityHeader.BoundingBox.Bottom - 2)
            .Where(word => word.BoundingBox.Bottom > totalLine.Bottom + 2)
            .OrderByDescending(word => word.BoundingBox.Bottom)
            .ToList();

        var productLines = new List<RawProductLine>();
        for (var index = 0; index < quantityWords.Count; index++)
        {
            var quantity = quantityWords[index];
            var upperBound = index == 0
                ? descriptionHeader.BoundingBox.Bottom - 2
                : (quantityWords[index - 1].BoundingBox.Bottom + quantity.BoundingBox.Bottom) / 2;
            var lowerNeighbor = index + 1 < quantityWords.Count
                ? quantityWords[index + 1].BoundingBox.Bottom
                : totalLine.Bottom;
            var lowerBound = (quantity.BoundingBox.Bottom + lowerNeighbor) / 2;

            var descriptionWords = words
                .Where(word => word.BoundingBox.Left >= descriptionHeader.BoundingBox.Left - 2)
                .Where(word => word.BoundingBox.Left < priceHeader.BoundingBox.Left - 2)
                .Where(word => word.BoundingBox.Bottom < upperBound)
                .Where(word => word.BoundingBox.Bottom > lowerBound)
                .ToList();

            productLines.Add(new RawProductLine
            {
                QuantityText = quantity.Text,
                RawDescription = JoinInReadingOrder(descriptionWords),
            });
        }

        return productLines;
    }

    private static TextLine? FindTableHeader(IEnumerable<TextLine> lines) => lines.SingleOrDefault(
        line => line.Words.Any(word => Normalize(word.Text) == "quantity")
            && line.Words.Any(word => Normalize(word.Text) == "description")
            && line.Words.Count(word => Normalize(word.Text) == "price") >= 2);

    private static List<TextLine> GroupIntoLines(IEnumerable<Word> words) => words
        .GroupBy(word => Math.Round(word.BoundingBox.Bottom / 3) * 3)
        .Select(group => new TextLine(
            group.Key,
            group.OrderBy(word => word.BoundingBox.Left).ToList()))
        .OrderByDescending(line => line.Bottom)
        .ToList();

    private static string JoinInReadingOrder(IEnumerable<Word> words) => string.Join(
        " ",
        GroupIntoLines(words).SelectMany(line => line.Words).Select(word => word.Text));

    private static bool ContainsSequence(IReadOnlyList<Word> words, string first, string second) => words
        .Zip(
            words.Skip(1),
            (left, right) => left.Text.Equals(first, StringComparison.OrdinalIgnoreCase)
                && right.Text.Equals(second, StringComparison.OrdinalIgnoreCase))
        .Any(matches => matches);

    private static string Normalize(string text) => text.Trim().TrimEnd(':').ToLowerInvariant();

    [GeneratedRegex("^[A-Za-z0-9]+(?:-[A-Za-z0-9]+){2,}$", RegexOptions.CultureInvariant)]
    private static partial Regex SummaryOrderIdentifierPattern();

    private sealed record TextLine(double Bottom, List<Word> Words);
}
