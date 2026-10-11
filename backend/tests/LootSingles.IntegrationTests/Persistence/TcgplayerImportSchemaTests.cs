using LootSingles.Application.Import;
using LootSingles.Domain.Orders;
using LootSingles.IntegrationTests.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace LootSingles.IntegrationTests.Persistence;

/// <summary>
/// 020-tcgplayer-api-import schema (data-model.md): import source on orders and attempts, a
/// nullable collector number, and the API-only language and image URL on lines.
/// </summary>
[Collection(SqlServerTestCollection.Name)]
public sealed class TcgplayerImportSchemaTests(SqlServerContainerFixture fixture)
{
    [Fact]
    public async Task Order_round_trips_import_source()
    {
        await using var lease = await fixture.CreateDatabaseLeaseAsync();
        await using (var context = lease.CreateDbContext())
        {
            context.Orders.Add(CreateOrder("SYN-API-1", OrderImportSource.TcgplayerApi));
            context.Orders.Add(CreateOrder("SYN-PDF-1", OrderImportSource.PackingSlipPdf));
            await context.SaveChangesAsync();
        }

        await using var readContext = lease.CreateDbContext();
        var sources = await readContext.Orders.ToDictionaryAsync(
            order => order.TcgplayerOrderId,
            order => order.ImportSource
        );
        Assert.Equal(OrderImportSource.TcgplayerApi, sources["SYN-API-1"]);
        Assert.Equal(OrderImportSource.PackingSlipPdf, sources["SYN-PDF-1"]);
    }

    [Fact]
    public async Task Order_inserted_without_import_source_defaults_to_packing_slip_pdf()
    {
        await using var lease = await fixture.CreateDatabaseLeaseAsync();
        await using (var connection = new SqlConnection(lease.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            // The column list omits ImportSource, as every row that predates the migration does.
            command.CommandText = """
                INSERT INTO Orders (TcgplayerOrderId, Status, ImportedAt)
                VALUES ('SYN-LEGACY-1', 0, SYSDATETIMEOFFSET())
                """;
            await command.ExecuteNonQueryAsync();
        }

        await using var context = lease.CreateDbContext();
        var order = await context.Orders.SingleAsync(o => o.TcgplayerOrderId == "SYN-LEGACY-1");
        Assert.Equal(OrderImportSource.PackingSlipPdf, order.ImportSource);
    }

    [Fact]
    public async Task OrderLine_saves_null_collector_number_language_and_image_url()
    {
        await using var lease = await fixture.CreateDatabaseLeaseAsync();
        var language = new string('l', 50);
        var imageUrl = "https://example.invalid/" + new string('i', 2048 - 24);
        Assert.Equal(2048, imageUrl.Length);

        await using (var context = lease.CreateDbContext())
        {
            var order = CreateOrder("SYN-API-2", OrderImportSource.TcgplayerApi);
            order.OrderLines.Add(CreateLine(collectorNumber: null, language, imageUrl));
            order.OrderLines.Add(CreateLine(collectorNumber: "#1", language: null, imageUrl: null));
            context.Orders.Add(order);
            await context.SaveChangesAsync();
        }

        await using var readContext = lease.CreateDbContext();
        var lines = await readContext.OrderLines.OrderBy(line => line.Id).ToListAsync();
        Assert.Null(lines[0].CollectorNumber);
        Assert.Equal(language, lines[0].Language);
        Assert.Equal(imageUrl, lines[0].ImageUrl);
        Assert.Equal("#1", lines[1].CollectorNumber);
        Assert.Null(lines[1].Language);
        Assert.Null(lines[1].ImageUrl);
    }

    [Fact]
    public async Task OrderLine_rejects_language_over_50_and_image_url_over_2048()
    {
        await using var lease = await fixture.CreateDatabaseLeaseAsync();
        await using var context = lease.CreateDbContext();
        var order = CreateOrder("SYN-API-3", OrderImportSource.TcgplayerApi);
        order.OrderLines.Add(CreateLine("#1", new string('l', 51), null));
        context.Orders.Add(order);
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());

        context.ChangeTracker.Clear();
        var other = CreateOrder("SYN-API-4", OrderImportSource.TcgplayerApi);
        other.OrderLines.Add(CreateLine("#1", null, new string('i', 2049)));
        context.Orders.Add(other);
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task ImportAttempt_round_trips_source_and_new_failure_types()
    {
        await using var lease = await fixture.CreateDatabaseLeaseAsync();
        await using (var context = lease.CreateDbContext())
        {
            var attempt = new ImportAttempt
            {
                StartedAt = DateTimeOffset.UtcNow,
                Source = OrderImportSource.TcgplayerApi,
                AttemptFailureCode = FailureType.TcgplayerNotConfigured,
                AttemptFailureMessage = "synthetic",
            };
            context.ImportAttempts.Add(attempt);
            context.ImportAttempts.Add(new ImportAttempt { StartedAt = DateTimeOffset.UtcNow });
            await context.SaveChangesAsync();
        }

        await using var readContext = lease.CreateDbContext();
        var attempts = await readContext.ImportAttempts.OrderBy(a => a.Id).ToListAsync();
        Assert.Equal(OrderImportSource.TcgplayerApi, attempts[0].Source);
        Assert.Equal(FailureType.TcgplayerNotConfigured, attempts[0].AttemptFailureCode);
        Assert.Equal(OrderImportSource.PackingSlipPdf, attempts[1].Source);
    }

    [Fact]
    public void New_failure_types_append_after_existing_values()
    {
        Assert.Equal(10, (int)FailureType.PersistenceFailure);
        Assert.Equal(11, (int)FailureType.IncompleteOrder);
        Assert.Equal(12, (int)FailureType.TcgplayerUnavailable);
        Assert.Equal(13, (int)FailureType.TcgplayerAccessRefused);
        Assert.Equal(14, (int)FailureType.TcgplayerResponseInvalid);
        Assert.Equal(15, (int)FailureType.TcgplayerNotConfigured);
    }

    private static Order CreateOrder(string identifier, OrderImportSource source) =>
        new()
        {
            TcgplayerOrderId = identifier,
            Status = OrderStatus.Ready,
            ImportedAt = DateTimeOffset.UtcNow,
            ImportSource = source,
        };

    private static OrderLine CreateLine(
        string? collectorNumber,
        string? language,
        string? imageUrl
    ) =>
        new()
        {
            RawDescription = "raw",
            ProductLine = "Magic",
            ProductName = "Card",
            Set = "Set",
            CollectorNumber = collectorNumber,
            Condition = "Near Mint",
            Quantity = 1,
            Language = language,
            ImageUrl = imageUrl,
        };
}
