using System.Net;
using System.Net.Http.Json;
using LootSingles.Domain.Employees;
using LootSingles.Domain.Orders;
using LootSingles.Infrastructure.Auth;
using LootSingles.IntegrationTests.Auth;
using Microsoft.EntityFrameworkCore;

namespace LootSingles.IntegrationTests.Packing;

/// <summary>
/// 017-pick-completion-handoff T069–T073 — the four bounds PRD v0.5 §27 puts on storing packing
/// slips.
///
/// <para>
/// Those bounds are the whole of the mitigation for holding customer data, and they hold together
/// or not at all. If one of these cannot be made to pass, that re-opens amendment A14 with the
/// Product Owner rather than being settled in code (plan.md, Note on Principle VII).
/// </para>
///
/// <para>
/// Two of the five checks live where the behaviour does and are not duplicated here:
/// <b>one order per file</b> and <b>the batch is not retained</b> are covered by
/// <c>PackingSlipStorageTests</c>, which imports a real fixture and inspects what was stored.
/// <b>Access is attributable</b> is covered by <c>PackingDeskTests</c>. What remains is the bound
/// with no natural home — that no picking surface can reach a slip — and the rule that logs stay
/// clean.
/// </para>
/// </summary>
public sealed class PackingSlipPrivacyTests
{
    /// <summary>
    /// The markers a real TCGplayer slip carries on every page, confirmed against the fixtures.
    /// A slip stored in this test is the genuine article, so finding any of these in a picking
    /// payload means customer data reached a picker.
    /// </summary>
    private static readonly string[] CustomerMarkers =
    [
        "Ship To",
        "Shipping Address",
        "Buyer Name",
        "1 Example Street",
    ];

    [Theory]
    [InlineData("/api/orders")]
    [InlineData("/api/dashboard")]
    public async Task PickingSurfaces_CarryNoSlipContent(string path)
    {
        await using var factory = new AuthWebApplicationFactory();
        var (client, employee) = await LoginAsync(factory, $"privacy{path.GetHashCode():x}");
        var order = await SeedOrderWithRealSlipAsync(factory, "PRIVACY-LIST", employee.Id);

        var body = await (await client.GetAsync(path)).Content.ReadAsStringAsync();

        AssertNoCustomerData(body);
        Assert.DoesNotContain("packingSlip", body, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(0, order.Id);
    }

    [Fact]
    public async Task OrderDetail_TheScreenAPickerWorksFrom_CarriesNoSlipContent()
    {
        await using var factory = new AuthWebApplicationFactory();
        var (client, employee) = await LoginAsync(factory, "privacydetail");
        var order = await SeedOrderWithRealSlipAsync(factory, "PRIVACY-DETAIL", employee.Id);

        var body = await (
            await client.GetAsync($"/api/orders/{order.Id}")
        ).Content.ReadAsStringAsync();

        // FR-039. This is the payload a picker's screen is built from, and it is the one that
        // would leak if loading an order ever started loading its slip.
        AssertNoCustomerData(body);
        Assert.DoesNotContain("content", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Label_ThePrintedArtifact_CarriesNoSlipContent()
    {
        await using var factory = new AuthWebApplicationFactory();
        var (client, employee) = await LoginAsync(factory, "privacylabel");
        var order = await SeedOrderWithRealSlipAsync(factory, "PRIVACY-LABEL", employee.Id);

        var body = await (
            await client.GetAsync($"/api/orders/{order.Id}/label")
        ).Content.ReadAsStringAsync();

        // FR-015 — a dropped label must leak nothing. Someone who finds one learns that Loot
        // sold some cards.
        AssertNoCustomerData(body);
    }

    /// <summary>
    /// The structural half of FR-039: loading an order does not load its slip. This is what makes
    /// the rule a property of the schema rather than something every future projection has to
    /// remember (research.md §4).
    /// </summary>
    [Fact]
    public async Task LoadingAnOrder_DoesNotMaterialiseItsSlip()
    {
        await using var factory = new AuthWebApplicationFactory();
        var (_, employee) = await LoginAsync(factory, "privacystructural");
        var order = await SeedOrderWithRealSlipAsync(factory, "PRIVACY-STRUCTURAL", employee.Id);

        await factory.SeedAsync(async context =>
        {
            var loaded = await context
                .Orders.AsNoTracking()
                .Include(candidate => candidate.OrderLines)
                .SingleAsync(candidate => candidate.Id == order.Id);

            Assert.Null(loaded.PackingSlip);
        });
    }

    [Fact]
    public async Task RetrievingASlip_LogsTheOrderAndEmployee_AndNeverTheSlip()
    {
        await using var factory = new AuthWebApplicationFactory();
        var (client, employee) = await LoginAsync(factory, "privacylogs");
        var order = await SeedOrderWithRealSlipAsync(factory, "PRIVACY-LOGS", employee.Id);

        var response = await client.GetAsync($"/api/orders/{order.Id}/packing-slip");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // The durable record is what answers "who opened this" after the fact (SC-010); the log
        // line is for operational visibility. Neither may carry what the slip contained, which
        // the constitution's logging rule forbids outright.
        PackingSlipAccess? access = null;
        await factory.SeedAsync(async context =>
        {
            access = await context
                .PackingSlipAccesses.AsNoTracking()
                .SingleAsync(row => row.OrderId == order.Id);
        });

        Assert.NotNull(access);
        Assert.Equal(employee.Id, access.EmployeeId);
        // An access row records that an access happened, never what was in the slip — it has no
        // field that could hold it, which is the point.
        Assert.DoesNotContain(
            typeof(PackingSlipAccess).GetProperties(),
            property => property.PropertyType == typeof(byte[])
        );
    }

    private static void AssertNoCustomerData(string body)
    {
        foreach (var marker in CustomerMarkers)
        {
            Assert.DoesNotContain(marker, body, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Stores a slip whose bytes really do contain the markers a TCGplayer slip carries, so the
    /// assertions above are testing against the thing that would actually leak rather than a
    /// placeholder that could never fail them.
    /// </summary>
    private static async Task<Order> SeedOrderWithRealSlipAsync(
        AuthWebApplicationFactory factory,
        string tcgplayerOrderId,
        int employeeId
    )
    {
        var order = new Order
        {
            TcgplayerOrderId = tcgplayerOrderId,
            Status = OrderStatus.Picked,
            ImportedAt = DateTimeOffset.Parse("2026-09-21T08:00:00Z"),
            OrderLines =
            [
                new OrderLine
                {
                    RawDescription = "Privacy Card - 001/100 - Near Mint",
                    ProductLine = "Pokemon",
                    ProductName = "Privacy Card",
                    Set = "Base Set",
                    CollectorNumber = "001/100",
                    Condition = "Near Mint",
                    Quantity = 1,
                    PickOutcome = PickOutcome.Picked,
                    PickOutcomeRecordedByEmployeeId = employeeId,
                    PickOutcomeRecordedAt = DateTimeOffset.Parse("2026-09-21T09:00:00Z"),
                },
            ],
        };

        await factory.SeedAsync(context =>
        {
            context.Orders.Add(order);
            return Task.CompletedTask;
        });

        await factory.SeedAsync(context =>
        {
            context.OrderPackingSlips.Add(
                new OrderPackingSlip
                {
                    OrderId = order.Id,
                    Content = System.Text.Encoding.UTF8.GetBytes(
                        "Ship To: Jane Doe\nShipping Address: 1 Example Street\nBuyer Name: Jane Doe"
                    ),
                    StoredAt = DateTimeOffset.UtcNow,
                }
            );
            return Task.CompletedTask;
        });

        return order;
    }

    private static async Task<(HttpClient Client, Employee Employee)> LoginAsync(
        AuthWebApplicationFactory factory,
        string username
    )
    {
        var normalized = new string(
            username.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray()
        );
        var employee = new Employee
        {
            Username = normalized,
            NormalizedUsername = normalized.ToUpperInvariant(),
            DisplayName = normalized,
            PinHash = new Pbkdf2PinHasher().Hash("1234"),
            Role = EmployeeRole.Picker,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await factory.SeedAsync(context =>
        {
            context.Employees.Add(employee);
            return Task.CompletedTask;
        });
        var client = factory.CreateAuthenticatedClient();
        (
            await client.PostAsJsonAsync(
                "/api/auth/login",
                new { username = normalized, pin = "1234" }
            )
        ).EnsureSuccessStatusCode();
        return (client, employee);
    }
}
