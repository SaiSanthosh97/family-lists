using FamilyLists.Core.Common;

namespace FamilyLists.Core.Lists.Tests;

/// <summary>Expense lists: what comes forward to the next month, totals, and what's overdue.</summary>
public class ExpenseTests
{
    private static ListItem Expense(string name, decimal? amount, bool paid = false, bool included = true, int? dueDay = null, int position = 0) =>
        new() { Id = Ids.New(), FamilyId = "g", ListId = "l", MonthId = "m", Name = name, Price = amount, Bought = paid, Included = included, DueDay = dueDay, Position = position };

    [Fact]
    public void Carries_every_expense_with_its_amount_due_day_and_notes_but_unpaid()
    {
        var rent = Expense("Rent", 15000, paid: true, dueDay: 5, position: 1);
        rent.Notes = "To the owner by UPI";
        var output = Rollover.BuildExpenseCarryOver([rent, Expense("Electricity", 1240.50m, paid: true, position: 2)]);

        Assert.Equal(["Rent", "Electricity"], output.Select(i => i.Name));
        Assert.Equal((15000m, 5, "To the owner by UPI"), (output[0].Amount, output[0].DueDay, output[0].Notes));
        Assert.Equal(1240.50m, output[1].Amount);
        Assert.All(output, i => Assert.False(i.UnpaidLast));
    }

    [Fact]
    public void Flags_expenses_that_were_due_but_never_marked_paid()
    {
        var output = Rollover.BuildExpenseCarryOver([Expense("Internet", 999, paid: false, position: 1), Expense("Rent", 15000, paid: true, position: 2)]);
        Assert.Equal([("Internet", true), ("Rent", false)], output.Select(i => (i.Name, i.UnpaidLast)));
    }

    [Fact]
    public void A_skipped_expense_comes_back_flagged_and_last_but_is_not_called_unpaid()
    {
        var output = Rollover.BuildExpenseCarryOver([Expense("Car insurance", 8000, included: false, position: 1), Expense("Rent", 15000, position: 2)]);
        Assert.Equal(["Rent", "Car insurance"], output.Select(i => i.Name));
        Assert.True(output[1].SkippedLast);
        Assert.False(output[1].UnpaidLast);
        Assert.Equal(8000m, output[1].Amount);
    }

    [Fact]
    public void Only_exact_duplicate_names_collapse()
    {
        var output = Rollover.BuildExpenseCarryOver(
        [
            Expense("Car loan", 12000, position: 1),
            Expense("car  LOAN", 12000, position: 2),
            Expense("Car loans", 5000, position: 3),
        ]);
        Assert.Equal(["Car loan", "Car loans"], output.Select(i => i.Name));
    }

    [Fact]
    public void Expense_keys_differ_from_grocery_keys_and_are_the_same_on_every_phone()
    {
        Assert.Equal("car loans", Rollover.Key(ListKind.Expenses, "  Car   Loans "));
        Assert.Equal(Rollover.Key(ListKind.Monthly, "Tomatoes"), Rollover.Key(ListKind.Monthly, "tomato"));
        Assert.NotEqual(Rollover.Key(ListKind.Expenses, "Tomatoes"), Rollover.Key(ListKind.Expenses, "tomato"));
    }

    [Fact]
    public void Totals_count_due_expenses_and_what_is_paid()
    {
        var items = new[]
        {
            Expense("Rent", 15000, paid: true),
            Expense("Electricity", 1200.50m),
            Expense("Maid", null),
            Expense("Insurance", 8000, included: false),
        };
        var summary = Expenses.Summarize(items, "2026-11", new DateOnly(2026, 11, 22));
        Assert.Equal((16200.50m, 15000m, 1200.50m, 3, 1), (summary.Total, summary.Paid, summary.Left, summary.Count, summary.PaidCount));
    }

    [Fact]
    public void Overdue_means_unpaid_and_past_its_due_day()
    {
        var today = new DateOnly(2026, 11, 22);
        Assert.True(Expenses.IsOverdue(Expense("Rent", 1, dueDay: 5), "2026-11", today));
        Assert.False(Expenses.IsOverdue(Expense("Rent", 1, dueDay: 22), "2026-11", today)); // due today
        Assert.False(Expenses.IsOverdue(Expense("Rent", 1, dueDay: 5, paid: true), "2026-11", today));
        Assert.False(Expenses.IsOverdue(Expense("Rent", 1, dueDay: 5, included: false), "2026-11", today));
        Assert.False(Expenses.IsOverdue(Expense("Maid", 1), "2026-11", today)); // no date, month not over
        Assert.True(Expenses.IsOverdue(Expense("Maid", 1), "2026-10", today));  // last month, never paid
        Assert.False(Expenses.IsOverdue(Expense("Rent", 1, dueDay: 5), "2026-12", today));
    }

    [Theory]
    [InlineData("Rent 15000", "Rent", "15000")]
    [InlineData("Electricity ₹1,240.50", "Electricity", "1240.50")]
    [InlineData("Maid - 3000/-", "Maid", "3000")]
    [InlineData("School fee: Rs. 4,500", "School fee", "4500")]
    [InlineData("15000 rent", "rent", "15000")]
    [InlineData("  Car loan EMI  12500 ", "Car loan EMI", "12500")]
    [InlineData("Netflix", "Netflix", null)]
    [InlineData("Vitamin B12", "Vitamin B12", null)]
    public void The_add_box_reads_a_name_and_an_amount(string text, string name, string? amount)
    {
        var (n, a) = Expenses.ParseQuickAdd(text);
        Assert.Equal(name, n);
        Assert.Equal(amount is null ? null : decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture), a);
    }

    [Fact]
    public void A_due_day_past_the_months_end_means_its_last_day()
    {
        Assert.Equal(new DateOnly(2027, 2, 28), Expenses.DueDate(Expense("EMI", 1, dueDay: 31), "2027-02"));
        Assert.Equal(new DateOnly(2028, 2, 29), Expenses.DueDate(Expense("EMI", 1, dueDay: 30), "2028-02"));
        Assert.Null(Expenses.DueDate(Expense("EMI", 1), "2027-02"));
        Assert.True(Expenses.IsValidDueDay(null));
        Assert.False(Expenses.IsValidDueDay(0));
        Assert.False(Expenses.IsValidDueDay(32));
    }
}
