using System.Security.Cryptography;
using LootSingles.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace LootSingles.IntegrationTests.Import;

public class PiiRetentionTests
{
    private static readonly string[] SourceCustomerPii =
    [
        "Sample Buyer 01",
        "1001 EXAMPLE ST",
        "SAMPLE CITY, MI 00001",
        "Sample Buyer 02",
        "1002 EXAMPLE ST",
        "SAMPLE CITY, MI 00002",
    ];

    [Fact]
    public async Task ImportAsync_DoesNotPersistSourceCustomerPiiInOrdersOrLines()
    {
        await using var context = ImportTestSupport.CreateInMemoryContext();
        var service = ImportTestSupport.CreateService(context);

        await ImportTestSupport.ImportFixtureAsync(service, "valid-multi-order-batch.pdf");

        var orders = await context.Orders.Include(order => order.OrderLines).ToListAsync();
        var retainedText = string.Join('\n', orders.SelectMany(GetPersistedText));

        Assert.NotEmpty(orders);
        Assert.All(SourceCustomerPii, pii =>
            Assert.DoesNotContain(pii, retainedText, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("duplicate-product-line-same-order.pdf")]
    [InlineData("corrupted-file.pdf")]
    public async Task ImportAsync_DoesNotCopySourcePdfIntoProcessTempStorage(string fixtureName)
    {
        var sourceBytes = await ReadFixtureBytesAsync(fixtureName);
        var sourceHash = Convert.ToHexString(SHA256.HashData(sourceBytes));
        var matchesBefore = FindFilesWithHash(Path.GetTempPath(), sourceBytes.Length, sourceHash);

        await using var context = ImportTestSupport.CreateInMemoryContext();
        var service = ImportTestSupport.CreateService(context);
        await using var source = new MemoryStream(sourceBytes, writable: false);
        await foreach (var _ in service.ImportAsync(source))
        {
        }

        var matchesAfter = FindFilesWithHash(Path.GetTempPath(), sourceBytes.Length, sourceHash);
        Assert.Empty(matchesAfter.Except(matchesBefore, StringComparer.OrdinalIgnoreCase));
    }

    private static IEnumerable<string?> GetPersistedText(Order order) =>
        new[] { order.TcgplayerOrderId }
            .Concat(order.OrderLines.SelectMany(line => new[]
            {
                line.RawDescription,
                line.ProductLine,
                line.ProductName,
                line.Set,
                line.CollectorNumber,
                line.Rarity,
                line.Condition,
                line.Variant,
            }));

    private static async Task<byte[]> ReadFixtureBytesAsync(string fixtureName)
    {
        await using var fixture = ImportTestSupport.OpenFixture(fixtureName);
        using var buffer = new MemoryStream();
        await fixture.CopyToAsync(buffer);
        return buffer.ToArray();
    }

    private static HashSet<string> FindFilesWithHash(string root, int expectedLength, string expectedHash)
    {
        var matches = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in EnumerateFilesSafely(root))
        {
            try
            {
                var file = new FileInfo(path);
                if (file.Length != expectedLength)
                {
                    continue;
                }

                using var stream = file.OpenRead();
                if (Convert.ToHexString(SHA256.HashData(stream)) == expectedHash)
                {
                    matches.Add(file.FullName);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return matches;
    }

    private static IEnumerable<string> EnumerateFilesSafely(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.TryPop(out var directory))
        {
            string[] files;
            string[] directories;
            try
            {
                files = Directory.GetFiles(directory);
                directories = Directory.GetDirectories(directory);
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var file in files)
            {
                yield return file;
            }

            foreach (var child in directories)
            {
                pending.Push(child);
            }
        }
    }
}
