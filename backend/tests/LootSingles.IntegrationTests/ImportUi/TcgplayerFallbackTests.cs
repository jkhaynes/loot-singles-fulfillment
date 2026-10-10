using System.Net;
using System.Text.Json;
using LootSingles.Domain.Orders;
using LootSingles.Infrastructure.Persistence;
using LootSingles.IntegrationTests.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LootSingles.IntegrationTests.ImportUi;

/// <summary>
/// T046 (FR-016): when TCGplayer is down or refuses the store, packing-slip upload still works.
/// The TCGplayer attempt fails first, the stub stays broken, and a PDF upload on the same session
/// then imports every order and stores each one's packing slip.
/// </summary>
public sealed class TcgplayerFallbackTests
{
    public static TheoryData<HttpStatusCode, string> Failures =>
        new()
        {
            { HttpStatusCode.ServiceUnavailable, "tcgplayerUnavailable" },
            { HttpStatusCode.Unauthorized, "tcgplayerAccessRefused" },
        };

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task After_a_failed_TCGplayer_attempt_a_PDF_upload_imports_and_stores_each_packing_slip(
        HttpStatusCode upstreamStatus,
        string attemptFailureCode
    )
    {
        await using var root = new AuthWebApplicationFactory();
        var stub = new TcgplayerStubHandler
        {
            Override = _ => Task.FromResult<HttpResponseMessage?>(new(upstreamStatus)),
        };
        await using var factory = ImportUiTestSupport.WithTcgplayerStub(root, stub);
        using var client = await ImportUiTestSupport.LoginAsync(factory);

        var apiLines = await ImportUiTestSupport.PostTcgplayerAsync(client);
        using (var apiTerminal = JsonDocument.Parse(apiLines[^1]))
        {
            Assert.Equal("failed", apiTerminal.RootElement.GetProperty("status").GetString());
            Assert.Equal(
                attemptFailureCode,
                apiTerminal.RootElement.GetProperty("attemptFailureCode").GetString()
            );
        }
        var upstreamRequestsBefore = stub.Requests.Count;

        var pdfLines = await ImportUiTestSupport.PostFixtureAsync(
            client,
            "valid-multi-order-batch.pdf"
        );

        using var pdfTerminal = JsonDocument.Parse(pdfLines[^1]);
        var snapshot = pdfTerminal.RootElement;
        Assert.Equal("completed", snapshot.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, snapshot.GetProperty("attemptFailureCode").ValueKind);
        Assert.Equal(0, snapshot.GetProperty("failedCount").GetInt32());
        var succeeded = snapshot.GetProperty("succeededCount").GetInt32();
        Assert.True(succeeded > 0);
        // The upload never touches TCGplayer.
        Assert.Equal(upstreamRequestsBefore, stub.Requests.Count);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LootSinglesDbContext>();
        var orders = await context
            .Orders.AsNoTracking()
            .Include(order => order.PackingSlip)
            .ToListAsync();
        Assert.Equal(succeeded, orders.Count);
        Assert.All(
            orders,
            order =>
            {
                Assert.Equal(OrderImportSource.PackingSlipPdf, order.ImportSource);
                Assert.Equal(OrderStatus.Ready, order.Status);
                Assert.NotNull(order.PackingSlip);
                Assert.NotEmpty(order.PackingSlip.Content);
            }
        );
    }
}
