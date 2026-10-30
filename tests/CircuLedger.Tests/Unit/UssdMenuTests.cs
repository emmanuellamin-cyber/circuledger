using CircuLedger.Application.Ussd;
using CircuLedger.Domain.Enums;

namespace CircuLedger.Tests.Unit;

public class UssdMenuTests
{
    private static readonly Guid NjalaHub = Guid.NewGuid();
    private static readonly Guid Kiosk = Guid.NewGuid();

    private static readonly UssdMenuContext Ctx = new(
        ["Samsung", "Tecno", "Itel", "Other"],
        [new UssdDropOffOption(NjalaHub, "Njala Hub", "Mon-Sat 9-5"), new UssdDropOffOption(Kiosk, "Kiosk Bo/Kowama", "Daily 9-7")],
        (brand, condition) => brand == "Samsung" && condition == DeviceCondition.BrokenScreen ? 200m : 50m);

    private static UssdScreen Screen(string text) => Assert.IsType<UssdScreen>(UssdMenu.Evaluate(text, Ctx));

    [Fact]
    public void Empty_text_shows_main_menu()
    {
        var screen = Screen("");
        Assert.False(screen.IsFinal);
        Assert.StartsWith("CON ", screen.Render());
        Assert.Contains("1. Sell a phone", screen.Text);
        Assert.Contains("4. Nearest drop-off points", screen.Text);
    }

    [Fact]
    public void Sell_flow_with_drop_off_walks_brand_condition_method_point_confirm()
    {
        Assert.Contains("1. Samsung", Screen("1").Text);
        Assert.Contains("2. Broken screen", Screen("1*1").Text);
        Assert.Contains("1. Drop-off point", Screen("1*1*2").Text);
        Assert.Contains("1. Njala Hub", Screen("1*1*2*1").Text);

        var confirm = Screen("1*1*2*1*1");
        Assert.Contains("Samsung, Broken screen", confirm.Text);
        Assert.Contains("Drop-off: Njala Hub", confirm.Text);
        Assert.Contains("Est. offer: SLE 200", confirm.Text);

        var confirmed = Assert.IsType<UssdSellConfirmed>(UssdMenu.Evaluate("1*1*2*1*1*1", Ctx));
        Assert.Equal("Samsung", confirmed.Brand);
        Assert.Equal(DeviceCondition.BrokenScreen, confirmed.Condition);
        Assert.Equal(FulfilmentMethod.DropOff, confirmed.Method);
        Assert.Equal(NjalaHub, confirmed.DropOffPointId);
    }

    [Fact]
    public void Sell_flow_with_pickup_captures_area()
    {
        Assert.Contains("area or nearest landmark", Screen("1*2*1*2").Text);
        Assert.Contains("Pickup: Towama mosque", Screen("1*2*1*2*Towama mosque").Text);

        var confirmed = Assert.IsType<UssdSellConfirmed>(UssdMenu.Evaluate("1*2*1*2*Towama mosque*1", Ctx));
        Assert.Equal("Tecno", confirmed.Brand);
        Assert.Equal(DeviceCondition.Dead, confirmed.Condition);
        Assert.Equal(FulfilmentMethod.Pickup, confirmed.Method);
        Assert.Equal("Towama mosque", confirmed.PickupArea);
        Assert.Null(confirmed.DropOffPointId);
    }

    [Fact]
    public void Cancelling_at_confirmation_ends_the_session()
    {
        var screen = Screen("1*1*2*1*2*2");
        Assert.True(screen.IsFinal);
        Assert.StartsWith("END ", screen.Render());
        Assert.Contains("cancelled", screen.Text);
    }

    [Theory]
    [InlineData("9")]
    [InlineData("1*9")]
    [InlineData("1*1*4")]
    [InlineData("1*1*2*3")]
    [InlineData("1*1*2*1*7")]
    [InlineData("1*1*2*1*1*5")]
    [InlineData("1*abc")]
    public void Invalid_choices_end_with_an_error(string text)
    {
        var screen = Screen(text);
        Assert.True(screen.IsFinal);
        Assert.Contains("Invalid choice", screen.Text);
    }

    [Fact]
    public void Status_lookup_prompts_then_returns_lookup_action()
    {
        Assert.Contains("reference code", Screen("2").Text);
        Assert.Equal("SYABC234", Assert.IsType<UssdStatusLookup>(UssdMenu.Evaluate("2*syabc234", Ctx)).ReferenceCode);
    }

    [Fact]
    public void Certificate_lookup_prompts_then_returns_lookup_action()
    {
        Assert.Contains("certificate number", Screen("3").Text);
        Assert.Equal("CDS-2026-000045", Assert.IsType<UssdCertificateLookup>(UssdMenu.Evaluate("3*cds-2026-000045", Ctx)).CertificateNumber);
    }

    [Fact]
    public void Nearest_points_asks_for_area()
    {
        Assert.Contains("town or area", Screen("4").Text);
        Assert.Equal("Bo", Assert.IsType<UssdNearestPoints>(UssdMenu.Evaluate("4*Bo", Ctx)).Area);
    }

    [Fact]
    public void Evaluation_is_deterministic_for_retries()
    {
        Assert.Equal(Screen("1*1*2*1*1").Text, Screen("1*1*2*1*1").Text);
    }

    [Fact]
    public void Screens_fit_within_typical_ussd_length_limit()
    {
        foreach (var text in new[] { "", "1", "1*1", "1*1*2", "1*1*2*1", "1*1*2*1*1", "1*1*2*2*Towama near the central mosque" })
            Assert.True(Screen(text).Render().Length <= 182, $"Screen for '{text}' is too long");
    }
}
