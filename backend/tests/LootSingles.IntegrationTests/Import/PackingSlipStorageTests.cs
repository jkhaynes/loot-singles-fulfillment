using LootSingles.Application.Import;
using LootSingles.Domain.Orders;
using LootSingles.Infrastructure.Import;
using LootSingles.Infrastructure.Persistence;
using LootSingles.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace LootSingles.IntegrationTests.Import;

/// <summary>
/// 017-pick-completion-handoff T035 — the regression plan.md calls most likely.
///
/// <para>
/// PRD §26 and Constitution V bias this pipeline toward rejecting questionable data, and applying
/// that reflexively to packing slips would reject orders whose own data parsed perfectly. §26's
/// concern is order-data integrity; a missing slip is not order corruption. So slicing is a
/// subordinate step that records its failure and gets out of the way (FR-021).
/// </para>
/// </summary>
[Collection(SqlServerTestCollection.Name)]
public sealed class PackingSlipStorageTests(SqlServerContainerFixture fixture)
{
    [Fact]
    public async Task Import_StoresOnePackingSlipPerOrder()
    {
        await using var lease = await fixture.CreateDatabaseLeaseAsync();
        await using var context = lease.CreateDbContext();
        var service = NewService(context, new PdfPigPackingSlipSlicer());

        await RunImportAsync(service, "valid-multi-order-batch.pdf");

        var slips = await context
            .OrderPackingSlips.AsNoTracking()
            .Include(slip => slip.Order)
            .ToListAsync();

        Assert.NotEmpty(slips);
        Assert.All(slips, slip => Assert.NotEmpty(slip.Content));
        // One slip per order, and each smaller than the batch it came from — the whole point of
        // slicing rather than retaining the batch document (FR-019, FR-020).
        var orderCount = await context.Orders.CountAsync();
        Assert.Equal(orderCount, slips.Count);
    }

    [Fact]
    public async Task Import_SlipThatCannotBeSliced_StillImportsItsOrder()
    {
        await using var lease = await fixture.CreateDatabaseLeaseAsync();
        await using var context = lease.CreateDbContext();
        var service = NewService(context, new FailingSlicer());

        await RunImportAsync(service, "valid-multi-order-batch.pdf");

        // Every order is here. Not one of them was rejected because a convenience for a later
        // workflow could not be produced.
        Assert.NotEqual(0, await context.Orders.CountAsync());
        Assert.Equal(0, await context.OrderPackingSlips.CountAsync());
        Assert.All(
            await context.Orders.AsNoTracking().ToListAsync(),
            order => Assert.Equal(OrderStatus.Ready, order.Status)
        );
    }

    [Fact]
    public async Task Import_SlipFailure_IsRecordedRatherThanSwallowedSilently()
    {
        await using var lease = await fixture.CreateDatabaseLeaseAsync();
        await using var context = lease.CreateDbContext();
        var logger = new ImportTestSupport.CapturingLogger<PackingSlipImportService>();
        var service = new PackingSlipImportService(
            new PdfPigPackingSlipParser(),
            new FailingSlicer(),
            new ImportRepository(context),
            logger
        );

        await RunImportAsync(service, "valid-multi-order-batch.pdf");

        // Swallowed, but not silently: something has to be able to notice that slips stopped
        // being produced.
        Assert.Contains(
            logger.Entries,
            entry => entry.Message.Contains("packing slip", StringComparison.OrdinalIgnoreCase)
        );
        // And the log must never carry the slip's contents (constitution: no customer PII).
        Assert.DoesNotContain(
            logger.Entries,
            entry => entry.Message.Contains("Ship To", StringComparison.OrdinalIgnoreCase)
        );
    }

    [Fact]
    public async Task Import_DoesNotRetainTheBatchDocument()
    {
        await using var lease = await fixture.CreateDatabaseLeaseAsync();
        await using var context = lease.CreateDbContext();
        var service = NewService(context, new PdfPigPackingSlipSlicer());
        var batch = await File.ReadAllBytesAsync(FixturePath("valid-multi-order-batch.pdf"));

        await RunImportAsync(service, "valid-multi-order-batch.pdf");

        // FR-020 — the half of 001's FR-019 that survives. No stored artifact is the whole batch.
        var slips = await context.OrderPackingSlips.AsNoTracking().ToListAsync();
        Assert.All(slips, slip => Assert.True(slip.Content.Length < batch.Length));
    }

    private static PackingSlipImportService NewService(
        LootSinglesDbContext context,
        IPackingSlipSlicer slicer
    ) =>
        new(
            new PdfPigPackingSlipParser(),
            slicer,
            new ImportRepository(context),
            NullLogger<PackingSlipImportService>.Instance
        );

    private static async Task RunImportAsync(PackingSlipImportService service, string fixture)
    {
        await using var stream = File.OpenRead(FixturePath(fixture));
        await foreach (var _ in service.ImportAsync(stream)) { }
    }

    private static string FixturePath(string name) =>
        Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "LootSingles.Fixtures",
            "PackingSlips",
            name
        );

    /// <summary>
    /// Stands in for the fixture T034 would otherwise add. A document whose pages cannot be
    /// copied is hard to craft and easy to misread later; a slicer that always fails exercises
    /// exactly the branch under test and says why in its name.
    /// </summary>
    private sealed class FailingSlicer : IPackingSlipSlicer
    {
        public byte[]? Slice(byte[] document, IReadOnlyList<int> pageNumbers) => null;
    }
}
