using System.Globalization;
using System.Text.RegularExpressions;

namespace FamilyLists.Core.Lists;

/// <summary>A month of expenses in numbers.</summary>
/// <param name="Total">Every expense due this month (skipped ones not counted).</param>
/// <param name="Paid">The part marked paid.</param>
/// <param name="Count">Expenses due this month.</param>
/// <param name="PaidCount">Of those, how many are paid.</param>
/// <param name="Overdue">Unpaid ones past their due day.</param>
public sealed record ExpenseSummary(decimal Total, decimal Paid, int Count, int PaidCount, int Overdue)
{
    public decimal Left => Total - Paid;
}

/// <summary>Rules for expense lists: totals, due dates and what's overdue.</summary>
public static class Expenses
{
    public static ExpenseSummary Summarize(IEnumerable<ListItem> items, string period, DateOnly today)
    {
        var due = items.Where(i => !i.IsDeleted && i.Included).ToList();
        return new ExpenseSummary(
            due.Sum(i => i.Price ?? 0),
            due.Where(i => i.Bought).Sum(i => i.Price ?? 0),
            due.Count,
            due.Count(i => i.Bought),
            due.Count(i => IsOverdue(i, period, today)));
    }

    /// <summary>The date it's due in that month; a day past the month's end (31 in February) means its last day.</summary>
    public static DateOnly? DueDate(ListItem item, string period)
    {
        if (item.DueDay is not { } day || !Period.TryParse(period, out var first))
        {
            return null;
        }

        return first.AddDays(Math.Clamp(day, 1, DateTime.DaysInMonth(first.Year, first.Month)) - 1);
    }

    /// <summary>Due this month, not paid, and its due date has passed (an older month's unpaid ones are all overdue).</summary>
    public static bool IsOverdue(ListItem item, string period, DateOnly today)
    {
        if (!item.Included || item.Bought || item.IsDeleted || !Period.TryParse(period, out var first))
        {
            return false;
        }

        if (DueDate(item, period) is { } date)
        {
            return date < today;
        }

        // No due day: overdue only once the whole month is over.
        return first.AddMonths(1) <= today;
    }

    public static bool IsValidDueDay(int? day) => day is null or >= 1 and <= 31;

    private const string Amount = @"(?:₹|rs\.?|inr|\$|€|£)?\s*(\d[\d,]*(?:\.\d{1,2})?)\s*(?:/-)?";

    private static readonly Regex Trailing = new(@"^(.*?[^\d\s,.].*?)[\s:=–-]+" + Amount + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex Leading = new("^" + Amount + @"\s+(.*[^\d\s].*)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// The add box: "Rent 15000", "Electricity ₹1,240.50", "Maid - 3000/-" or "15000 rent" → name and amount.
    /// Without a number it's just a name.
    /// </summary>
    public static (string Name, decimal? Amount) ParseQuickAdd(string text)
    {
        var raw = (text ?? "").Trim();
        foreach (var (regex, nameGroup, amountGroup) in new[] { (Trailing, 1, 2), (Leading, 2, 1) })
        {
            var match = regex.Match(raw);
            if (match.Success && decimal.TryParse(match.Groups[amountGroup].Value.Replace(",", ""), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount))
            {
                return (match.Groups[nameGroup].Value.Trim(), amount);
            }
        }

        return (raw, null);
    }
}
