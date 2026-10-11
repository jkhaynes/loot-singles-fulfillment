using System.Net;
using System.Text.Json;
using LootSingles.Domain.Orders;
using LootSingles.Infrastructure.Persistence;
using LootSingles.IntegrationTests.Auth;
using LootSingles.IntegrationTests.ImportUi;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LootSingles.IntegrationTests.Packing;

/// <summary>
/// T048 (FR-018): an order imported from the TCGplayer API has no stored packing slip, and the
/// packing desk handles that as the normal case it already is for orders imported earlier.
/// </summary>
public sealed class TcgplayerPackingTests
{
    [Fact]
    public async Task An_API_imported_order_has_no_slip_and_can_still_be_packed()
    {
        await using var root = new AuthWebApplicationFactory();
        var stub = new TcgplayerStubHandler();
        await using var factory = ImportUiTestSupport.WithTcgplayerStub(root, stub);
        using var client = await ImportUiTestSupport.LoginAsync(factory);
        await ImportUiTestSupport.PostTcgplayerAsync(client);

        // Picking itself is covered elsewhere; this puts one imported order where the desk expects it.
        int orderId;
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<LootSinglesDbContext>();
            var employeeId = await context.Employees.Select(e => e.Id).SingleAsync();
            var order = await context
                .Orders.Include(o => o.OrderLines)
                .SingleAsync(o => o.TcgplayerOrderId == "SYN-0002-A1");
            order.Status = OrderStatus.Picked;
            foreach (var line in order.OrderLines)
            {
                line.PickOutcome = PickOutcome.Picked;
                line.PickOutcomeRecordedByEmployeeId = employeeId;
                line.PickOutcomeRecordedAt = DateTimeOffset.UtcNow;
            }
            await context.SaveChangesAsync();
            orderId = order.Id;
        }

        var resolved = await client.GetAsync($"/api/packing/orders/{orderId}");
        using var body = JsonDocument.Parse(await resolved.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, resolved.StatusCode);
        Assert.False(body.RootElement.GetProperty("hasPackingSlip").GetBoolean());
        Assert.True(body.RootElement.GetProperty("canPack").GetBoolean());

        var slip = await client.GetAsync($"/api/orders/{orderId}/packing-slip");
        Assert.Equal(HttpStatusCode.NotFound, slip.StatusCode);

        var packed = await client.PostAsync($"/api/orders/{orderId}/packed", content: null);
        Assert.Equal(HttpStatusCode.OK, packed.StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<LootSinglesDbContext>();
            Assert.Equal(
                OrderStatus.Packed,
                (await context.Orders.AsNoTracking().SingleAsync(o => o.Id == orderId)).Status
            );
            // data-model.md invariant: no API order ever has a slip row.
            Assert.False(
                await context.OrderPackingSlips.AnyAsync(s =>
                    context.Orders.Any(o =>
                        o.Id == s.OrderId && o.ImportSource == OrderImportSource.TcgplayerApi
                    )
                )
            );
        }
    }
}
