using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using LootSingles.Api.Controllers;
using LootSingles.Domain.Employees;
using LootSingles.Domain.Orders;
using LootSingles.Infrastructure.Auth;
using LootSingles.Infrastructure.Persistence;
using LootSingles.Infrastructure.Tcgplayer.Synthetic;
using LootSingles.IntegrationTests.Auth;
using LootSingles.IntegrationTests.ImportUi;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LootSingles.IntegrationTests.Orders;

/// <summary>
/// 021 contracts/api.md: order deletion over HTTP, Manager/Admin only (FR-010, SC-006).
/// </summary>
public sealed class OrderDeletionEndpointTests
{
    [Fact]
    public async Task Delete_an_unclaimed_order_returns_204_and_hides_it_from_the_list()
    {
        await using var factory = new AuthWebApplicationFactory();
        var manager = await LoginAsync(factory, "delmanager1", EmployeeRole.ManagerAdmin);
        var order = await SeedOrderAsync(factory, "DEL-OK");

        var response = await manager.Client.DeleteAsync($"/api/orders/{order.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.DoesNotContain("DEL-OK", await ListedOrderIdsAsync(manager.Client));
    }

    [Fact]
    public async Task Delete_a_missing_order_returns_404_order_not_found()
    {
        await using var factory = new AuthWebApplicationFactory();
        var manager = await LoginAsync(factory, "delmanager2", EmployeeRole.ManagerAdmin);

        var response = await manager.Client.DeleteAsync("/api/orders/2147483647");

        await AssertErrorAsync(response, HttpStatusCode.NotFound, "order_not_found");
    }

    [Fact]
    public async Task Delete_an_already_deleted_order_returns_404_order_already_deleted()
    {
        await using var factory = new AuthWebApplicationFactory();
        var manager = await LoginAsync(factory, "delmanager3", EmployeeRole.ManagerAdmin);
        var order = await SeedOrderAsync(factory, "DEL-TWICE");
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await manager.Client.DeleteAsync($"/api/orders/{order.Id}")).StatusCode
        );

        var response = await manager.Client.DeleteAsync($"/api/orders/{order.Id}");

        await AssertErrorAsync(response, HttpStatusCode.NotFound, "order_already_deleted");
    }

    [Fact]
    public async Task Delete_a_claimed_before_order_under_the_production_rule_returns_409()
    {
        await using var factory = new AuthWebApplicationFactory();
        var manager = await LoginAsync(factory, "delmanager4", EmployeeRole.ManagerAdmin);
        var order = await SeedOrderAsync(factory, "DEL-TOUCHED", touchedBy: manager.Employee);

        var response = await manager.Client.DeleteAsync($"/api/orders/{order.Id}");

        await AssertErrorAsync(response, HttpStatusCode.Conflict, "order_claimed_before");
    }

    [Fact]
    public async Task Delete_a_claimed_before_order_where_touched_orders_are_allowed_returns_204()
    {
        await using var root = new AuthWebApplicationFactory();
        await using var factory = WithAllowTouched(root, true);
        var manager = await LoginAsync(factory, "delmanager5", EmployeeRole.ManagerAdmin);
        var order = await SeedOrderAsync(factory, "DEL-TOUCHED-OK", touchedBy: manager.Employee);

        var response = await manager.Client.DeleteAsync($"/api/orders/{order.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Deletion_summary_returns_its_shape()
    {
        await using var factory = new AuthWebApplicationFactory();
        var manager = await LoginAsync(factory, "summarymanager", EmployeeRole.ManagerAdmin);
        await SeedOrderAsync(factory, "SUM-FREE-1");
        await SeedOrderAsync(factory, "SUM-FREE-2");
        await SeedOrderAsync(factory, "SUM-TOUCHED", touchedBy: manager.Employee);

        var response = await manager.Client.GetAsync("/api/orders/deletion-summary");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var body = document.RootElement;
        Assert.True(body.GetProperty("asOf").GetDateTimeOffset() <= DateTimeOffset.UtcNow);
        Assert.Equal(2, body.GetProperty("deletableCount").GetInt32());
        Assert.Equal(1, body.GetProperty("keptCount").GetInt32());
        Assert.Equal(0, body.GetProperty("claimedCount").GetInt32());
        Assert.Equal(0, body.GetProperty("withUnresolvedIssueCount").GetInt32());
    }

    [Fact]
    public async Task Delete_all_returns_the_counts_and_keeps_the_claimed_before_order()
    {
        await using var factory = new AuthWebApplicationFactory();
        var manager = await LoginAsync(factory, "deleteallmanager", EmployeeRole.ManagerAdmin);
        await SeedOrderAsync(factory, "ALL-FREE-1");
        await SeedOrderAsync(factory, "ALL-FREE-2");
        await SeedOrderAsync(factory, "ALL-TOUCHED", touchedBy: manager.Employee);
        var summary = await manager.Client.GetFromJsonAsync<JsonElement>(
            "/api/orders/deletion-summary"
        );

        var response = await manager.Client.PostAsJsonAsync(
            "/api/orders/delete-all",
            new { asOf = summary.GetProperty("asOf").GetDateTimeOffset() }
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(2, document.RootElement.GetProperty("deletedCount").GetInt32());
        Assert.Equal(1, document.RootElement.GetProperty("keptCount").GetInt32());
        Assert.Equal(["ALL-TOUCHED"], await ListedOrderIdsAsync(manager.Client));
    }

    [Fact]
    public async Task Delete_all_with_asOf_in_the_future_returns_400_invalid_as_of()
    {
        await using var factory = new AuthWebApplicationFactory();
        var manager = await LoginAsync(factory, "futuremanager", EmployeeRole.ManagerAdmin);

        var response = await manager.Client.PostAsJsonAsync(
            "/api/orders/delete-all",
            new { asOf = DateTimeOffset.UtcNow.AddHours(1) }
        );

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, "invalid_as_of");
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("")]
    [InlineData("{\"asOf\":null}")]
    [InlineData("{\"asOf\":\"not a date\"}")]
    public async Task Delete_all_with_asOf_missing_or_unreadable_returns_400_invalid_as_of(
        string json
    )
    {
        await using var factory = new AuthWebApplicationFactory();
        var manager = await LoginAsync(factory, "missingmanager", EmployeeRole.ManagerAdmin);
        await SeedOrderAsync(factory, "MISSING-ASOF");

        var response = await manager.Client.PostAsync(
            "/api/orders/delete-all",
            new StringContent(json, Encoding.UTF8, "application/json")
        );

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, "invalid_as_of");
        Assert.Contains("MISSING-ASOF", await ListedOrderIdsAsync(manager.Client));
    }

    [Fact]
    public async Task A_picker_gets_403_on_all_three_endpoints_and_nothing_is_deleted()
    {
        await using var factory = new AuthWebApplicationFactory();
        var manager = await LoginAsync(factory, "scopemanager", EmployeeRole.ManagerAdmin);
        var picker = await LoginAsync(factory, "scopepicker", EmployeeRole.Picker);
        var order = await SeedOrderAsync(factory, "PICKER-CANT");

        var delete = await picker.Client.DeleteAsync($"/api/orders/{order.Id}");
        var summary = await picker.Client.GetAsync("/api/orders/deletion-summary");
        var deleteAll = await picker.Client.PostAsJsonAsync(
            "/api/orders/delete-all",
            new { asOf = DateTimeOffset.UtcNow }
        );

        Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, summary.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, deleteAll.StatusCode);
        Assert.Contains("PICKER-CANT", await ListedOrderIdsAsync(manager.Client));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_list_tells_a_manager_what_can_be_deleted_under_each_setting(
        bool allowTouched
    )
    {
        await using var root = new AuthWebApplicationFactory();
        await using var factory = WithAllowTouched(root, allowTouched);
        var manager = await LoginAsync(factory, "listmanager", EmployeeRole.ManagerAdmin);
        await SeedOrderAsync(factory, "LIST-FREE");
        await SeedOrderAsync(factory, "LIST-CLAIMED", touchedBy: manager.Employee);
        // Claimed once, then released: nobody holds it now, but it was touched.
        await SeedOrderAsync(
            factory,
            "LIST-RELEASED",
            touchedBy: manager.Employee,
            stillClaimed: false
        );

        var items = await ListAsync(manager.Client);

        AssertItem(items["LIST-FREE"], canDelete: true, reason: null);
        var reason = allowTouched ? null : "claimed_before";
        AssertItem(items["LIST-CLAIMED"], canDelete: allowTouched, reason);
        AssertItem(items["LIST-RELEASED"], canDelete: allowTouched, reason);
    }

    [Fact]
    public async Task The_list_never_offers_a_picker_deletion()
    {
        await using var root = new AuthWebApplicationFactory();
        await using var factory = WithAllowTouched(root, true);
        var picker = await LoginAsync(factory, "listpicker", EmployeeRole.Picker);
        await SeedOrderAsync(factory, "PICK-LIST-FREE");

        var items = await ListAsync(picker.Client);

        AssertItem(items["PICK-LIST-FREE"], canDelete: false, reason: null);
    }

    [Fact]
    public async Task The_list_and_delete_agree_on_which_orders_can_be_deleted()
    {
        await using var factory = new AuthWebApplicationFactory();
        var manager = await LoginAsync(factory, "agreemanager", EmployeeRole.ManagerAdmin);
        var free = await SeedOrderAsync(factory, "AGREE-FREE");
        var touched = await SeedOrderAsync(factory, "AGREE-TOUCHED", touchedBy: manager.Employee);
        var items = await ListAsync(manager.Client);

        var freeResult = await manager.Client.DeleteAsync($"/api/orders/{free.Id}");
        var touchedResult = await manager.Client.DeleteAsync($"/api/orders/{touched.Id}");

        Assert.Equal(
            items["AGREE-FREE"].canDelete,
            freeResult.StatusCode == HttpStatusCode.NoContent
        );
        Assert.Equal(
            items["AGREE-TOUCHED"].canDelete,
            touchedResult.StatusCode == HttpStatusCode.NoContent
        );
    }

    [Fact]
    public async Task Re_importing_a_deleted_PDF_order_creates_a_new_visible_order_and_leaves_the_deleted_row()
    {
        await using var factory = new AuthWebApplicationFactory();
        var manager = await LoginAsync(factory, "pdfreimport", EmployeeRole.ManagerAdmin);
        await ImportUiTestSupport.PostFixtureAsync(manager.Client, "valid-multi-order-batch.pdf");
        var (deletedId, tcgplayerOrderId) = await FirstVisibleOrderAsync(factory);
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await manager.Client.DeleteAsync($"/api/orders/{deletedId}")).StatusCode
        );
        var before = await DeletedRowAsync(factory, deletedId);

        var lines = await ImportUiTestSupport.PostFixtureAsync(
            manager.Client,
            "valid-multi-order-batch.pdf"
        );

        using var terminal = JsonDocument.Parse(lines[^1]);
        Assert.Contains(
            terminal.RootElement.GetProperty("results").EnumerateArray(),
            item =>
                item.GetProperty("sourceOrderIdentifier").GetString() == tcgplayerOrderId
                && item.GetProperty("outcome").GetString() == "succeeded"
        );
        await AssertReimportedAsync(factory, tcgplayerOrderId, deletedId, before);
    }

    [Fact]
    public async Task Re_importing_a_deleted_API_order_creates_a_new_visible_order_and_leaves_the_deleted_row()
    {
        await using var root = new AuthWebApplicationFactory();
        var stub = new SyntheticTcgplayerHandler();
        await using var factory = ImportUiTestSupport.WithTcgplayerStub(root, stub);
        var manager = await LoginAsync(factory, "apireimport", EmployeeRole.ManagerAdmin);
        await ImportUiTestSupport.PostTcgplayerAsync(manager.Client);
        var (deletedId, tcgplayerOrderId) = await FirstVisibleOrderAsync(factory);
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await manager.Client.DeleteAsync($"/api/orders/{deletedId}")).StatusCode
        );
        var before = await DeletedRowAsync(factory, deletedId);

        await ImportUiTestSupport.PostTcgplayerAsync(manager.Client);

        await AssertReimportedAsync(factory, tcgplayerOrderId, deletedId, before);
    }

    private static async Task AssertReimportedAsync(
        WebApplicationFactory<Program> factory,
        string tcgplayerOrderId,
        int deletedId,
        (DateTimeOffset? DeletedAt, int? DeletedBy) before
    )
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LootSinglesDbContext>();
        var rows = await context
            .Orders.IgnoreQueryFilters()
            .Where(order => order.TcgplayerOrderId == tcgplayerOrderId)
            .ToListAsync();

        Assert.Equal(2, rows.Count);
        var visible = Assert.Single(rows, order => order.DeletedAt == null);
        Assert.NotEqual(deletedId, visible.Id);
        Assert.Equal(OrderStatus.Ready, visible.Status);
        var deleted = Assert.Single(rows, order => order.Id == deletedId);
        Assert.Equal(before.DeletedAt, deleted.DeletedAt);
        Assert.Equal(before.DeletedBy, deleted.DeletedByEmployeeId);
    }

    private static async Task<(int Id, string TcgplayerOrderId)> FirstVisibleOrderAsync(
        WebApplicationFactory<Program> factory
    )
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LootSinglesDbContext>();
        var order = await context.Orders.OrderBy(o => o.Id).FirstAsync();
        return (order.Id, order.TcgplayerOrderId);
    }

    private static async Task<(DateTimeOffset? DeletedAt, int? DeletedBy)> DeletedRowAsync(
        WebApplicationFactory<Program> factory,
        int orderId
    )
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LootSinglesDbContext>();
        var row = await context
            .Orders.IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(order => order.Id == orderId);
        return (row.DeletedAt, row.DeletedByEmployeeId);
    }

    private static WebApplicationFactory<Program> WithAllowTouched(
        WebApplicationFactory<Program> root,
        bool allowTouched
    ) =>
        root.WithWebHostBuilder(builder =>
            builder.UseSetting("OrderDeletion:AllowTouchedOrders", allowTouched ? "true" : "false")
        );

    private static async Task AssertErrorAsync(
        HttpResponseMessage response,
        HttpStatusCode status,
        string error
    )
    {
        Assert.Equal(status, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(error, document.RootElement.GetProperty("error").GetString());
    }

    private static void AssertItem(
        (int orderId, bool canDelete, string? reason) item,
        bool canDelete,
        string? reason
    )
    {
        Assert.Equal(canDelete, item.canDelete);
        Assert.Equal(reason, item.reason);
    }

    private static async Task<List<string>> ListedOrderIdsAsync(HttpClient client) =>
        [.. (await ListAsync(client)).Keys];

    private static async Task<
        Dictionary<string, (int orderId, bool canDelete, string? reason)>
    > ListAsync(HttpClient client)
    {
        var body = await client.GetFromJsonAsync<JsonElement>("/api/orders");
        return body.EnumerateArray()
            .ToDictionary(
                item => item.GetProperty("tcgplayerOrderId").GetString()!,
                item =>
                    (
                        item.GetProperty("orderId").GetInt32(),
                        item.GetProperty("canDelete").GetBoolean(),
                        item.GetProperty("deleteBlockedReason").GetString()
                    )
            );
    }

    private static async Task<Order> SeedOrderAsync(
        WebApplicationFactory<Program> factory,
        string tcgplayerOrderId,
        Employee? touchedBy = null,
        bool stillClaimed = true
    )
    {
        var order = new Order
        {
            TcgplayerOrderId = tcgplayerOrderId,
            Status = OrderStatus.Ready,
            ImportedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
        };
        if (touchedBy is not null)
        {
            order.FirstClaimedAt = DateTimeOffset.UtcNow.AddMinutes(-4);
            if (stillClaimed)
            {
                order.Status = OrderStatus.InProgress;
                order.ClaimedByEmployeeId = touchedBy.Id;
                order.ClaimedAt = order.FirstClaimedAt;
            }
        }
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LootSinglesDbContext>();
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        return order;
    }

    private static async Task<(HttpClient Client, Employee Employee)> LoginAsync(
        WebApplicationFactory<Program> factory,
        string username,
        EmployeeRole role
    )
    {
        var employee = new Employee
        {
            Username = username,
            NormalizedUsername = username.ToUpperInvariant(),
            DisplayName = username,
            PinHash = new Pbkdf2PinHasher().Hash("1234"),
            Role = role,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<LootSinglesDbContext>();
            context.Employees.Add(employee);
            await context.SaveChangesAsync();
        }
        var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") }
        );
        var login = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(username, "1234")
        );
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        return (client, employee);
    }
}
