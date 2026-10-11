using System.Net;
using System.Net.Http.Json;
using LootSingles.Api.Controllers;
using LootSingles.Domain.Employees;
using LootSingles.Domain.Orders;
using LootSingles.Infrastructure.Auth;
using LootSingles.Infrastructure.Persistence;
using LootSingles.IntegrationTests.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LootSingles.IntegrationTests.Orders;

/// <summary>
/// 021 R6: <c>FirstClaimedAt</c> is stamped by the first claim (Pick Next or a specific claim) and
/// never moved or cleared afterwards, so an order is "touched" for good once claimed.
/// </summary>
public sealed class FirstClaimedAtTests
{
    [Fact]
    public async Task PickNext_SetsFirstClaimedAt()
    {
        await using var factory = new AuthWebApplicationFactory();
        var client = await LoginAsync(factory, "firstclaimpicknext");
        var order = NewOrder("FIRST-CLAIM-NEXT");
        await SeedOrderAsync(factory, order);
        var before = DateTimeOffset.UtcNow;

        var response = await client.PostAsync("/api/orders/pick-next", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var stamped = await FirstClaimedAtAsync(factory, order.Id);
        Assert.NotNull(stamped);
        Assert.True(stamped >= before.AddSeconds(-1));
    }

    [Fact]
    public async Task ClaimSpecific_SetsFirstClaimedAt()
    {
        await using var factory = new AuthWebApplicationFactory();
        var client = await LoginAsync(factory, "firstclaimspecific");
        var order = NewOrder("FIRST-CLAIM-SPECIFIC");
        await SeedOrderAsync(factory, order);

        var response = await client.PostAsync($"/api/orders/{order.Id}/claim", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(await FirstClaimedAtAsync(factory, order.Id));
    }

    [Fact]
    public async Task Release_LeavesFirstClaimedAtUnchanged()
    {
        await using var factory = new AuthWebApplicationFactory();
        var client = await LoginAsync(factory, "firstclaimrelease");
        var order = NewOrder("FIRST-CLAIM-RELEASE");
        await SeedOrderAsync(factory, order);
        await client.PostAsync($"/api/orders/{order.Id}/claim", content: null);
        var stamped = await FirstClaimedAtAsync(factory, order.Id);

        var response = await client.PostAsync($"/api/orders/{order.Id}/release", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(stamped);
        Assert.Equal(stamped, await FirstClaimedAtAsync(factory, order.Id));
    }

    [Fact]
    public async Task ForceRelease_LeavesFirstClaimedAtUnchanged()
    {
        await using var factory = new AuthWebApplicationFactory();
        var pickerClient = await LoginAsync(factory, "firstclaimforcepicker");
        var managerClient = await LoginAsync(
            factory,
            "firstclaimforcemanager",
            EmployeeRole.ManagerAdmin
        );
        var order = NewOrder("FIRST-CLAIM-FORCE");
        await SeedOrderAsync(factory, order);
        await pickerClient.PostAsync($"/api/orders/{order.Id}/claim", content: null);
        var stamped = await FirstClaimedAtAsync(factory, order.Id);

        var response = await managerClient.PostAsync(
            $"/api/orders/{order.Id}/force-release",
            content: null
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(stamped);
        Assert.Equal(stamped, await FirstClaimedAtAsync(factory, order.Id));
    }

    [Fact]
    public async Task SecondClaimAfterRelease_DoesNotMoveFirstClaimedAt()
    {
        await using var factory = new AuthWebApplicationFactory();
        var client = await LoginAsync(factory, "firstclaimreclaim");
        var order = NewOrder("FIRST-CLAIM-RECLAIM");
        await SeedOrderAsync(factory, order);
        await client.PostAsync($"/api/orders/{order.Id}/claim", content: null);
        var stamped = await FirstClaimedAtAsync(factory, order.Id);
        await client.PostAsync($"/api/orders/{order.Id}/release", content: null);
        await Task.Delay(50);

        var reclaim = await client.PostAsync($"/api/orders/{order.Id}/claim", content: null);

        Assert.Equal(HttpStatusCode.OK, reclaim.StatusCode);
        Assert.NotNull(stamped);
        Assert.Equal(stamped, await FirstClaimedAtAsync(factory, order.Id));
    }

    [Fact]
    public async Task SecondPickNextAfterRelease_DoesNotMoveFirstClaimedAt()
    {
        await using var factory = new AuthWebApplicationFactory();
        var client = await LoginAsync(factory, "firstclaimnextagain");
        var order = NewOrder("FIRST-CLAIM-NEXT-AGAIN");
        await SeedOrderAsync(factory, order);
        await client.PostAsync("/api/orders/pick-next", content: null);
        var stamped = await FirstClaimedAtAsync(factory, order.Id);
        await client.PostAsync($"/api/orders/{order.Id}/release", content: null);
        await Task.Delay(50);

        var again = await client.PostAsync("/api/orders/pick-next", content: null);

        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.NotNull(stamped);
        Assert.Equal(stamped, await FirstClaimedAtAsync(factory, order.Id));
    }

    private static async Task SeedOrderAsync(AuthWebApplicationFactory factory, Order order) =>
        await factory.SeedAsync(context =>
        {
            context.Orders.Add(order);
            return Task.CompletedTask;
        });

    private static async Task<DateTimeOffset?> FirstClaimedAtAsync(
        AuthWebApplicationFactory factory,
        int orderId
    )
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LootSinglesDbContext>();
        return await context
            .Orders.AsNoTracking()
            .Where(order => order.Id == orderId)
            .Select(order => order.FirstClaimedAt)
            .SingleAsync();
    }

    private static async Task<HttpClient> LoginAsync(
        AuthWebApplicationFactory factory,
        string username,
        EmployeeRole role = EmployeeRole.Picker
    )
    {
        await factory.SeedAsync(context =>
        {
            context.Employees.Add(
                new Employee
                {
                    Username = username,
                    NormalizedUsername = username.ToUpperInvariant(),
                    DisplayName = username,
                    PinHash = new Pbkdf2PinHasher().Hash("1234"),
                    Role = role,
                    CreatedAt = DateTimeOffset.UtcNow,
                }
            );
            return Task.CompletedTask;
        });
        var client = factory.CreateAuthenticatedClient();
        var login = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(username, "1234")
        );
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        return client;
    }

    private static Order NewOrder(string tcgplayerOrderId) =>
        new()
        {
            TcgplayerOrderId = tcgplayerOrderId,
            Status = OrderStatus.Ready,
            ImportedAt = DateTimeOffset.UtcNow,
        };
}
