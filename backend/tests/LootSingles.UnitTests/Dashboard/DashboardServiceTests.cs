using LootSingles.Application.Dashboard;
using LootSingles.Domain.Orders;

namespace LootSingles.UnitTests.Dashboard;

public class DashboardServiceTests
{
    [Fact]
    public async Task GetReadyOrderSummariesAsync_NoOrders_ReturnsEmptyList()
    {
        var repository = new FakeDashboardRepository([]);
        var service = new DashboardService(repository);

        var summaries = await service.GetReadyOrderSummariesAsync(CancellationToken.None);

        Assert.Empty(summaries);
    }

    // 015-pick-completion T037: the three sections that replace the Dashboard's placeholders.
    [Fact]
    public async Task GetInProgressOrderSummariesAsync_ReturnsOnlyInProgressOrders()
    {
        var service = new DashboardService(
            new FakeDashboardRepository([
                NewOrder(1, "READY", OrderStatus.Ready),
                NewOrder(2, "IN-PROGRESS", OrderStatus.InProgress),
                NewOrder(3, "PICKED", OrderStatus.Picked),
            ])
        );

        var summaries = await service.GetInProgressOrderSummariesAsync(CancellationToken.None);

        Assert.Equal("IN-PROGRESS", Assert.Single(summaries).TcgplayerOrderId);
    }

    [Fact]
    public async Task GetPickedOrderSummariesAsync_ReturnsOnlyPickedOrders()
    {
        var service = new DashboardService(
            new FakeDashboardRepository([
                NewOrder(1, "IN-PROGRESS", OrderStatus.InProgress),
                NewOrder(2, "PICKED", OrderStatus.Picked),
            ])
        );

        var summaries = await service.GetPickedOrderSummariesAsync(CancellationToken.None);

        Assert.Equal("PICKED", Assert.Single(summaries).TcgplayerOrderId);
    }

    [Fact]
    public async Task GetNeedsAttentionOrderSummariesAsync_NamesTheFlaggedProducts()
    {
        var flagged = NewOrder(1, "NEEDS-ATTENTION", OrderStatus.NeedsAttention);
        flagged.OrderLines.Add(NewLine("Missing Card", PickOutcome.HasIssue));
        flagged.OrderLines.Add(NewLine("Found Card", PickOutcome.Picked));
        var service = new DashboardService(
            new FakeDashboardRepository([flagged, NewOrder(2, "READY", OrderStatus.Ready)])
        );

        var summaries = await service.GetNeedsAttentionOrderSummariesAsync(CancellationToken.None);

        var summary = Assert.Single(summaries);
        Assert.Equal("NEEDS-ATTENTION", summary.TcgplayerOrderId);
        Assert.Equal(["Missing Card"], summary.FlaggedProductNames);
    }

    private static Order NewOrder(int id, string tcgplayerOrderId, OrderStatus status) =>
        new()
        {
            Id = id,
            TcgplayerOrderId = tcgplayerOrderId,
            Status = status,
            ImportedAt = DateTimeOffset.UtcNow,
        };

    private static OrderLine NewLine(string productName, PickOutcome? outcome) =>
        new()
        {
            RawDescription = productName,
            ProductLine = "Magic",
            ProductName = productName,
            Set = "Alpha",
            CollectorNumber = "#1",
            Condition = "Near Mint",
            Quantity = 1,
            PickOutcome = outcome,
        };

    private sealed class FakeDashboardRepository(IReadOnlyList<Order> orders) : IDashboardRepository
    {
        public Task<IReadOnlyList<OrderSummary>> GetReadyOrderSummariesAsync(
            CancellationToken cancellationToken
        ) => SummariesForAsync(OrderStatus.Ready);

        public Task<IReadOnlyList<OrderSummary>> GetInProgressOrderSummariesAsync(
            CancellationToken cancellationToken
        ) => SummariesForAsync(OrderStatus.InProgress);

        public Task<IReadOnlyList<OrderSummary>> GetPickedOrderSummariesAsync(
            CancellationToken cancellationToken
        ) => SummariesForAsync(OrderStatus.Picked);

        /// <summary>
        /// Keyed on the claim, never on status — an order keeps its claim through Needs
        /// Attention and Picked, which is the behaviour ActiveClaimTests covers against a real
        /// database (016-mobile-picking FR-026).
        /// </summary>
        public Task<OrderSummary?> GetActiveClaimAsync(
            int employeeId,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult(
                orders
                    .Where(order => order.ClaimedByEmployeeId == employeeId)
                    .Select(order => new OrderSummary(
                        order.Id,
                        order.TcgplayerOrderId,
                        order.OrderLines.Count,
                        order.OrderLines.Sum(line => line.Quantity)
                    ))
                    .SingleOrDefault()
            );

        public Task<IReadOnlyList<NeedsAttentionOrderSummary>> GetNeedsAttentionOrderSummariesAsync(
            CancellationToken cancellationToken
        ) =>
            Task.FromResult<IReadOnlyList<NeedsAttentionOrderSummary>>(
                orders
                    .Where(order => order.Status == OrderStatus.NeedsAttention)
                    .Select(order => new NeedsAttentionOrderSummary(
                        order.Id,
                        order.TcgplayerOrderId,
                        order.OrderLines.Count,
                        order.OrderLines.Sum(line => line.Quantity),
                        order
                            .OrderLines.Where(line => line.PickOutcome == PickOutcome.HasIssue)
                            .Select(line => line.ProductName)
                            .ToList()
                    ))
                    .ToList()
            );

        private Task<IReadOnlyList<OrderSummary>> SummariesForAsync(OrderStatus status) =>
            Task.FromResult<IReadOnlyList<OrderSummary>>(
                orders
                    .Where(order => order.Status == status)
                    .Select(order => new OrderSummary(
                        order.Id,
                        order.TcgplayerOrderId,
                        order.OrderLines.Count,
                        order.OrderLines.Sum(line => line.Quantity)
                    ))
                    .ToList()
            );
    }
}
