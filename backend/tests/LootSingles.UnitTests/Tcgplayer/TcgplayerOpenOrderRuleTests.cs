using LootSingles.Application.Import;
using LootSingles.Infrastructure.Tcgplayer;

namespace LootSingles.UnitTests.Tcgplayer;

/// <summary>
/// T068: FR-004 as one pure rule over an order details row and the ids the manifest resolved.
/// The ids are the synthetic manifest's: order statuses Processing 1, Ready To Ship 2, Shipped 3,
/// Delivered 4, Cancelled 5; pickup statuses Received 1, Ready for Pickup 3, Picked Up 4; delivery
/// types Standard 1, InStorePickup 4; order types Normal 1, Direct 2.
/// </summary>
public sealed class TcgplayerOpenOrderRuleTests
{
    private const int Standard = 1;
    private const int Expedited = 2;
    private const int InStorePickup = 4;
    private const int Normal = 1;
    private const int Direct = 2;

    private static readonly TcgplayerOpenOrderIds Ids = new(
        OrderStatusIds: [1, 2],
        PickupStatusIds: [1],
        OrderTypeIds: [Normal],
        InStorePickupDeliveryTypeId: InStorePickup,
        NormalOrderTypeId: Normal
    );

    [Fact]
    public void The_in_store_pickup_delivery_type_is_recognised_by_its_manifest_name()
    {
        Assert.Equal("InStorePickup", TcgplayerOpenOrderRule.InStorePickupDeliveryType);
        Assert.Equal("Normal", TcgplayerOpenOrderRule.NormalOrderType);
    }

    [Theory]
    [InlineData(1)] // Processing
    [InlineData(2)] // Ready To Ship
    public void A_shipped_order_in_an_open_status_is_open(int status)
    {
        Assert.True(TcgplayerOpenOrderRule.IsOpen(Shipped(status), Ids));
        Assert.True(
            TcgplayerOpenOrderRule.IsOpen(
                Shipped(status) with
                {
                    OrderDeliveryTypeId = Expedited,
                },
                Ids
            )
        );
    }

    [Theory]
    [InlineData(3)] // Shipped
    [InlineData(4)] // Delivered
    [InlineData(5)] // Cancelled
    public void A_shipped_order_in_any_other_status_is_not_open(int status)
    {
        Assert.False(TcgplayerOpenOrderRule.IsOpen(Shipped(status), Ids));
    }

    [Fact]
    public void A_pickup_order_that_is_Received_is_open()
    {
        // Live, a Received pickup order's order status is Processing.
        Assert.True(TcgplayerOpenOrderRule.IsOpen(Pickup(pickupStatus: 1, status: 1), Ids));
    }

    [Theory]
    [InlineData(3)] // Ready for Pickup
    [InlineData(4)] // Picked Up
    public void A_pickup_order_past_Received_is_not_open_whatever_its_order_status(int pickupStatus)
    {
        // The order status alone would say open; the pickup status decides a pickup order.
        Assert.False(
            TcgplayerOpenOrderRule.IsOpen(Pickup(pickupStatus: pickupStatus, status: 1), Ids)
        );
    }

    [Fact]
    public void A_shipped_order_ignores_its_pickup_status()
    {
        Assert.True(
            TcgplayerOpenOrderRule.IsOpen(Shipped(2) with { OrderPickupStatusTypeId = 4 }, Ids)
        );
        Assert.False(
            TcgplayerOpenOrderRule.IsOpen(Shipped(4) with { OrderPickupStatusTypeId = 1 }, Ids)
        );
    }

    [Fact]
    public void A_Direct_order_is_not_open_even_in_an_open_status()
    {
        Assert.False(TcgplayerOpenOrderRule.IsOpen(Shipped(2) with { OrderTypeId = Direct }, Ids));
        Assert.False(
            TcgplayerOpenOrderRule.IsOpen(
                Pickup(pickupStatus: 1, status: 1) with
                {
                    OrderTypeId = Direct,
                },
                Ids
            )
        );
    }

    [Fact]
    public void An_order_with_no_order_type_counts_as_Normal()
    {
        Assert.True(TcgplayerOpenOrderRule.IsOpen(Shipped(2) with { OrderTypeId = null }, Ids));
        Assert.False(
            TcgplayerOpenOrderRule.IsOpen(
                Shipped(2) with
                {
                    OrderTypeId = null,
                },
                Ids with
                {
                    OrderTypeIds = [Direct],
                }
            )
        );
    }

    [Fact]
    public void The_configured_ids_decide_not_fixed_values()
    {
        var shippedOnlyWhenShipped = Ids with { OrderStatusIds = [3], PickupStatusIds = [3] };

        Assert.True(TcgplayerOpenOrderRule.IsOpen(Shipped(3), shippedOnlyWhenShipped));
        Assert.False(TcgplayerOpenOrderRule.IsOpen(Shipped(2), shippedOnlyWhenShipped));
        Assert.True(
            TcgplayerOpenOrderRule.IsOpen(
                Pickup(pickupStatus: 3, status: 1),
                shippedOnlyWhenShipped
            )
        );
    }

    public static TheoryData<TcgplayerOrderDetails, string> Undecidable =>
        new()
        {
            { Shipped(2) with { OrderDeliveryTypeId = null }, "delivery type" },
            { Shipped(2) with { OrderStatusTypeId = null }, "order status" },
            {
                Pickup(pickupStatus: 1, status: 1) with
                {
                    OrderPickupStatusTypeId = null,
                },
                "pickup status"
            },
        };

    [Theory]
    [MemberData(nameof(Undecidable))]
    public void A_row_missing_a_value_the_rule_needs_is_ResponseInvalid_naming_the_order_and_field(
        TcgplayerOrderDetails row,
        string field
    )
    {
        var failure = Assert.Throws<TcgplayerFeedException>(() =>
            TcgplayerOpenOrderRule.IsOpen(row, Ids)
        );

        Assert.Equal(TcgplayerFeedFailure.ResponseInvalid, failure.Failure);
        Assert.Contains("SYN-0001-A1", failure.Message);
        Assert.Contains(field, failure.Message);
    }

    [Fact]
    public void A_pickup_order_with_no_order_status_is_still_decided_by_its_pickup_status()
    {
        Assert.True(
            TcgplayerOpenOrderRule.IsOpen(
                Pickup(pickupStatus: 1, status: 1) with
                {
                    OrderStatusTypeId = null,
                },
                Ids
            )
        );
    }

    private static TcgplayerOrderDetails Shipped(int status) =>
        new()
        {
            OrderNumber = "SYN-0001-A1",
            OrderStatusTypeId = status,
            OrderDeliveryTypeId = Standard,
            OrderPickupStatusTypeId = null,
            OrderTypeId = Normal,
            ProductCount = 1,
        };

    private static TcgplayerOrderDetails Pickup(int pickupStatus, int status) =>
        new()
        {
            OrderNumber = "SYN-0001-A1",
            OrderStatusTypeId = status,
            OrderDeliveryTypeId = InStorePickup,
            OrderPickupStatusTypeId = pickupStatus,
            OrderTypeId = Normal,
            ProductCount = 1,
        };
}
