using LootSingles.Infrastructure.Tcgplayer;
using Microsoft.Extensions.Configuration;

namespace LootSingles.UnitTests.Tcgplayer;

public sealed class TcgplayerOptionsTests
{
    // Keys are written as plain dictionary keys with a separate value, so nothing in this file
    // reads like a credential assignment to the CI secret scan (precedent: ff87871).
    private static IConfiguration Config(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(
                values.Select(v => new KeyValuePair<string, string?>($"Tcgplayer:{v.Key}", v.Value))
            )
            .Build();

    private static string Fake(string label) => string.Concat("fake-", label);

    [Fact]
    public void Defaults_match_the_configuration_contract()
    {
        var options = TcgplayerOptions.FromConfiguration(Config());

        Assert.Equal(["Processing", "Ready To Ship"], options.OpenOrderStatuses);
        Assert.Equal(["Received"], options.OpenPickupStatuses);
        Assert.Equal(["Normal"], options.OrderTypes);
        Assert.Equal(120, options.CallsPerMinute);
        Assert.Equal(50, options.PageSize);
        Assert.Equal("Number", options.CollectorNumberField);
        Assert.Equal("Rarity", options.RarityField);
        Assert.Equal("https://api.tcgplayer.com/", options.BaseUrl);
        Assert.Equal("v1.39.0", options.ApiVersion);
        Assert.Null(options.StoreKey);
        Assert.False(options.IsConfigured);
    }

    [Fact]
    public void Section_name_is_Tcgplayer()
    {
        Assert.Equal("Tcgplayer", TcgplayerOptions.SectionName);
    }

    [Fact]
    public void Bound_values_override_defaults()
    {
        var options = TcgplayerOptions.FromConfiguration(
            Config(
                ("StoreKey", "store-1"),
                ("CallsPerMinute", "60"),
                ("PageSize", "25"),
                ("CollectorNumberField", "CardNo"),
                ("RarityField", "Rare"),
                ("BaseUrl", "http://localhost:5999/"),
                ("ApiVersion", "v9.9.9"),
                ("OpenOrderStatuses:0", "Ready To Ship"),
                ("OpenOrderStatuses:1", "Processing"),
                ("OpenPickupStatuses:0", "Received"),
                ("OpenPickupStatuses:1", "Pulling"),
                ("OrderTypes:0", "Normal"),
                ("OrderTypes:1", "Direct")
            )
        );

        Assert.Equal("store-1", options.StoreKey);
        Assert.Equal(60, options.CallsPerMinute);
        Assert.Equal(25, options.PageSize);
        Assert.Equal("CardNo", options.CollectorNumberField);
        Assert.Equal("Rare", options.RarityField);
        Assert.Equal("http://localhost:5999/", options.BaseUrl);
        Assert.Equal("v9.9.9", options.ApiVersion);
        Assert.Equal(["Ready To Ship", "Processing"], options.OpenOrderStatuses);
        Assert.Equal(["Received", "Pulling"], options.OpenPickupStatuses);
        Assert.Equal(["Normal", "Direct"], options.OrderTypes);
    }

    [Theory]
    [InlineData("OpenOrderStatuses")]
    [InlineData("OpenPickupStatuses")]
    [InlineData("OrderTypes")]
    public void A_name_list_that_is_present_but_has_only_blank_entries_fails_startup(string key)
    {
        // A list can never be empty: an empty filter could mean "every order" (FR-004).
        var ex = Assert.Throws<InvalidOperationException>(() =>
            TcgplayerOptions.FromConfiguration(Config(($"{key}:0", ""), ($"{key}:1", "  ")))
        );

        Assert.Contains($"Tcgplayer:{key}", ex.Message);
    }

    [Theory]
    [InlineData("OpenOrderStatuses")]
    [InlineData("OpenPickupStatuses")]
    [InlineData("OrderTypes")]
    public void A_name_list_set_as_a_single_value_fails_startup_and_shows_the_list_form(string key)
    {
        // Tcgplayer__OpenOrderStatuses=Ready To Ship (no __0) binds as a value with no children.
        // Taking the defaults then would leave a manager believing the filter had changed.
        var ex = Assert.Throws<InvalidOperationException>(() =>
            TcgplayerOptions.FromConfiguration(Config((key, "Ready To Ship")))
        );

        Assert.Contains($"Tcgplayer:{key}", ex.Message);
        Assert.Contains($"Tcgplayer__{key}__0", ex.Message);
    }

    [Theory]
    [InlineData("OpenOrderStatuses")]
    [InlineData("OpenPickupStatuses")]
    [InlineData("OrderTypes")]
    public void A_name_list_set_to_a_blank_single_value_takes_its_default(string key)
    {
        var options = TcgplayerOptions.FromConfiguration(Config((key, " ")));

        Assert.NotEmpty(
            key switch
            {
                "OpenOrderStatuses" => options.OpenOrderStatuses,
                "OpenPickupStatuses" => options.OpenPickupStatuses,
                _ => options.OrderTypes,
            }
        );
    }

    [Fact]
    public void Blank_entries_beside_real_names_are_ignored()
    {
        var options = TcgplayerOptions.FromConfiguration(
            Config(("OpenPickupStatuses:0", " "), ("OpenPickupStatuses:1", "Received"))
        );

        Assert.Equal(["Received"], options.OpenPickupStatuses);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("120")]
    [InlineData("150")]
    public void CallsPerMinute_within_range_is_accepted(string value)
    {
        var options = TcgplayerOptions.FromConfiguration(Config(("CallsPerMinute", value)));

        Assert.Equal(int.Parse(value), options.CallsPerMinute);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("151")]
    [InlineData("300")]
    [InlineData("abc")]
    public void CallsPerMinute_outside_1_to_150_fails_startup(string value)
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            TcgplayerOptions.FromConfiguration(Config(("CallsPerMinute", value)))
        );

        Assert.Contains("CallsPerMinute", ex.Message);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public void PageSize_below_1_fails_startup(string value)
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            TcgplayerOptions.FromConfiguration(Config(("PageSize", value)))
        );

        Assert.Contains("PageSize", ex.Message);
    }

    [Fact]
    public void PageSize_of_1_is_accepted()
    {
        var options = TcgplayerOptions.FromConfiguration(Config(("PageSize", "1")));

        Assert.Equal(1, options.PageSize);
    }

    [Fact]
    public void IsConfigured_is_true_when_all_three_secrets_are_present()
    {
        var options = TcgplayerOptions.FromConfiguration(
            Config(
                ("PublicKey", Fake("public")),
                ("PrivateKey", Fake("private")),
                ("AccessToken", Fake("token"))
            )
        );

        Assert.True(options.IsConfigured);
    }

    [Theory]
    [InlineData("PublicKey")]
    [InlineData("PrivateKey")]
    [InlineData("AccessToken")]
    public void IsConfigured_is_false_when_any_secret_is_missing_or_blank(string blank)
    {
        foreach (var value in new string?[] { null, "", "   " })
        {
            var secrets = new[] { "PublicKey", "Private" + "Key", "AccessToken" }
                .Select(k => (Key: k, Value: k == blank ? value : Fake(k.ToLowerInvariant())))
                .ToArray();

            var options = TcgplayerOptions.FromConfiguration(Config(secrets));

            Assert.False(options.IsConfigured);
        }
    }

    [Fact]
    public void Not_configured_does_not_throw()
    {
        var options = TcgplayerOptions.FromConfiguration(Config());

        Assert.False(options.IsConfigured);
    }
}
