using LootSingles.Application.Import;
using LootSingles.Infrastructure.Import;
using LootSingles.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LootSingles.IntegrationTests.Import;

internal static class ImportTestSupport
{
    public static LootSinglesDbContext CreateInMemoryContext() =>
        new(
            new DbContextOptionsBuilder<LootSinglesDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options
        );

    public static PackingSlipImportService CreateService(LootSinglesDbContext context) =>
        new(new PdfPigPackingSlipParser(), new ImportRepository(context));

    public static FileStream OpenFixture(string name) =>
        File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "PackingSlips", name));

    public static async Task<ImportProgressUpdate> ImportFixtureAsync(
        IPackingSlipImportService service,
        string name
    )
    {
        await using var stream = OpenFixture(name);
        ImportProgressUpdate? final = null;
        await foreach (var update in service.ImportAsync(stream))
            final = update;
        return Assert.IsType<ImportProgressUpdate>(final);
    }
}
