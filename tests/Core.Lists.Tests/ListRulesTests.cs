using System.Globalization;
using FamilyLists.Core.Common;

namespace FamilyLists.Core.Lists.Tests;

/// <summary>Carry-over, name matching and months: the same cases as the web version's tests.</summary>
public class RolloverTests
{
    private static ListItem Src(string name, decimal qty = 1, string? unit = null, bool included = true, int position = 0) =>
        new() { Id = Ids.New(), FamilyId = "g", ListId = "l", MonthId = "m", Name = name, Qty = qty, Unit = unit, Included = included, Position = position, Price = 99, Bought = true };

    [Fact]
    public void Copies_last_months_items_keeps_quantities_clears_prices_and_ticks()
    {
        var output = Rollover.BuildCarryOver([Src("Rice", 5, "kg", position: 2), Src("Milk", position: 1)]);
        Assert.Equal(["Milk", "Rice"], output.Select(i => i.Name));
        Assert.Equal((5m, "kg", false), (output[1].Qty, output[1].Unit, output[1].SkippedLast));
        Assert.Equal([1, 2], output.Select(i => i.Position));
    }

    [Fact]
    public void Carries_items_that_were_added_from_the_bill()
    {
        var output = Rollover.BuildCarryOver([Src("Milk", position: 1), Src("Bananas", position: 9)]);
        Assert.Contains("Bananas", output.Select(i => i.Name));
    }

    [Fact]
    public void Keeps_skipped_items_but_flags_them_and_lists_them_last()
    {
        var output = Rollover.BuildCarryOver([Src("Olive oil", included: false, position: 1), Src("Eggs", position: 2)]);
        Assert.Equal([("Eggs", false), ("Olive oil", true)], output.Select(i => (i.Name, i.SkippedLast)));
    }

    [Fact]
    public void Collapses_duplicates_with_plural_case_and_size_differences()
    {
        var output = Rollover.BuildCarryOver(
        [
            Src("Tomatoes", included: false, position: 1),
            Src("tomato", position: 2),
            Src("Milk 1L", position: 3),
            Src("milk", position: 4),
        ]);
        Assert.Equal(["Tomatoes", "Milk 1L"], output.Select(i => i.Name));
        Assert.False(output[0].SkippedLast); // needed by one of the duplicates
    }

    [Fact]
    public void Ignores_blank_names_deleted_items_and_starts_empty_without_history()
    {
        Assert.Empty(Rollover.BuildCarryOver([]));
        var deleted = Src("Gone");
        deleted.IsDeleted = true;
        Assert.Empty(Rollover.BuildCarryOver([Src("   "), deleted]));
    }

    [Fact]
    public void The_same_items_give_the_same_keys_on_every_phone()
    {
        var a = Rollover.BuildCarryOver([Src("Toned Milk 500ml"), Src("Rice")]);
        var b = Rollover.BuildCarryOver([Src("Toned Milk 500ml"), Src("Rice")]);
        Assert.Equal(a.Select(i => i.Key), b.Select(i => i.Key));
        Assert.Equal(Ids.Derive("item", "m", a[0].Key), Ids.Derive("item", "m", b[0].Key));
    }
}

public class ProductNameTests
{
    private sealed record Candidate(string Id, string Name);

    private static readonly Candidate[] Items = [new("1", "Milk"), new("2", "Bananas"), new("3", "Basmati rice"), new("4", "Dish soap")];

    [Fact]
    public void Normalises_sizes_plurals_and_punctuation()
    {
        Assert.Equal("banana", ProductNames.Normalize("Bananas 1kg"));
        Assert.Equal("milk toned", ProductNames.Normalize("Toned-Milk 500ml"));
    }

    [Theory]
    [InlineData("Toned milk 1L", "1")]
    [InlineData("Banana", "2")]
    [InlineData("India Gate basmati rice 5kg", "3")]
    public void Matches_cleaned_up_bill_names_to_list_items(string line, string expected) =>
        Assert.Equal(expected, ProductNames.BestMatch(line, Items, c => c.Name)?.Id);

    [Fact]
    public void Does_not_force_a_match_for_unrelated_products()
    {
        Assert.Null(ProductNames.BestMatch("Chocolate cookies", Items, c => c.Name));
        Assert.Equal(0, ProductNames.Similarity("Rice", "Dish soap"));
    }

    [Fact]
    public void Ranks_the_best_candidates_first() =>
        Assert.Equal("4", ProductNames.Rank("Liquid dish soap", Items, c => c.Name)[0].Id);
}

public class PeriodTests
{
    [Fact]
    public void Formats_and_shifts_months_across_year_ends()
    {
        Assert.Equal("2026-11", Period.Of(new DateOnly(2026, 11, 22)));
        Assert.Equal("2027-01", Period.Shift("2026-12", 1));
        Assert.Equal("2025-12", Period.Shift("2026-01", -1));
    }

    [Fact]
    public void Labels_always_show_the_full_year()
    {
        var en = CultureInfo.GetCultureInfo("en-GB");
        Assert.Equal("November 2026", Period.Label("2026-11", en));
        Assert.Equal("Nov 2026", Period.ShortLabel("2026-11", en));
        Assert.Equal("November", Period.MonthName("2026-11", en));
    }

    [Theory]
    [InlineData("2026-13")]
    [InlineData("26-11")]
    [InlineData("")]
    [InlineData(null)]
    public void Rejects_bad_months(string? text) => Assert.False(Period.TryParse(text, out _));
}
