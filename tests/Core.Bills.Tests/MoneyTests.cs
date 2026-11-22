using System.Globalization;
using FamilyLists.Core.Lists;

namespace FamilyLists.Core.Bills.Tests;

public class MoneyTests
{
    private static Bill BillOf(decimal? total, BillStatus status = BillStatus.Confirmed) =>
        new() { Id = "b", FamilyId = "g", ListId = "l", MonthId = "m", Total = total, Status = status };

    [Fact]
    public void Sums_exactly() => Assert.Equal(0.6m, Money.Sum([0.1m, 0.2m, 0.3m, null]));

    [Fact]
    public void Prefers_confirmed_bills_then_a_manual_total_then_item_prices()
    {
        Assert.Equal((40.10m, SpendSource.Bills), Money.MonthSpend([BillOf(40.10m), BillOf(99, BillStatus.Draft)], 5, [1]));
        Assert.Equal((120m, SpendSource.Manual), Money.MonthSpend([], 120, [1]));
        Assert.Equal((3.5m, SpendSource.Items), Money.MonthSpend([], null, [1.5m, null, 2m]));
        Assert.Equal((null, SpendSource.None), Money.MonthSpend([], null, []));
    }

    [Fact]
    public void Reports_how_far_the_lines_are_from_the_printed_total()
    {
        decimal?[] lines = [3.5m, 1.2m];
        Assert.Equal(0m, Money.Difference(lines, 4.2m, 0.5m));
        Assert.Equal(0.3m, Money.Difference(lines, 5m, null));
        Assert.Null(Money.Difference(lines, null, null));
    }

    [Fact]
    public void Formats_rupees_with_indian_grouping()
    {
        var en = CultureInfo.GetCultureInfo("en-IN");
        Assert.Equal("₹1,05,400.00", Money.Format(105400m, "INR", en));
        Assert.Equal("—", Money.Format(null, "INR", en));
    }

    [Theory]
    [InlineData("1,054.50", 1054.50)]
    [InlineData("₹ 60", 60)]
    [InlineData("12.5", 12.5)]
    public void Parses_amounts_people_type(string text, double expected) => Assert.Equal((decimal)expected, Money.Parse(text));

    [Fact]
    public void Empty_or_junk_is_no_amount()
    {
        Assert.Null(Money.Parse(""));
        Assert.Null(Money.Parse("abc"));
    }
}

public class HistoryTests
{
    private static MonthSpending Month(string period, params (string Name, decimal Price)[] items) =>
        new(
            new MonthList { Id = period, FamilyId = "g", ListId = "l", Period = period },
            null,
            SpendSource.Items,
            items.Select(i => new ListItem { Id = i.Name + period, FamilyId = "g", ListId = "l", MonthId = period, Name = i.Name, Price = i.Price, Bought = true }).ToList(),
            [],
            []);

    [Fact]
    public void Groups_a_product_across_months_by_its_normalised_name()
    {
        var history = SpendingService.History(
        [
            Month("2026-12", ("Milk 1L", 130)),
            Month("2026-11", ("Milk", 120), ("Rice", 650)),
        ]);

        var milk = history[0];
        Assert.Equal("Milk", milk.Name);
        Assert.Equal([("2026-11", 120m), ("2026-12", 130m)], milk.Prices);
        Assert.Equal(130m, milk.Latest);
        Assert.Equal("Rice", history[1].Name);
    }
}
