namespace FamilyLists.Core.Lists;

/// <summary>An item as copied into the next month.</summary>
/// <param name="Key">The normalised name: two phones copying the same month derive the same item id from it.</param>
/// <param name="Amount">Expenses only: last month's amount, so this month starts with it.</param>
/// <param name="DueDay">Expenses only: the day of the month it's due.</param>
/// <param name="UnpaidLast">Expenses only: it was due last month but never marked paid.</param>
public sealed record CarriedItem(
    string Key, string Name, decimal Qty, string? Unit, string? Notes, bool SkippedLast, int Position,
    decimal? Amount = null, int? DueDay = null, bool UnpaidLast = false);

/// <summary>Builds next month's draft from the most recent month's items. Ported from the web version.</summary>
public static class Rollover
{
    /// <summary>The carry-over rules for a kind of list.</summary>
    public static IReadOnlyList<CarriedItem> For(ListKind kind, IEnumerable<ListItem> previous) =>
        kind == ListKind.Expenses ? BuildExpenseCarryOver(previous) : BuildCarryOver(previous);

    /// <summary>The key two items of a kind of list are "the same thing" by (and that carried item ids derive from).</summary>
    public static string Key(ListKind kind, string name)
    {
        var trimmed = name.Trim();
        if (kind == ListKind.Expenses)
        {
            // Exact names: "Car loan" and "Car loans" may be two loans, and unit words mean nothing here.
            return string.Join(' ', trimmed.ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        }

        return ProductNames.Normalize(trimmed) is { Length: > 0 } normal ? normal : trimmed.ToLowerInvariant();
    }

    /// <summary>
    /// Everything from last month comes forward, including things found on the bill. Items marked "not needed" last
    /// month still come forward (needs change month to month) but are flagged and listed last. Duplicate names
    /// collapse into one. Prices and ticks are not copied: spending is tracked per month.
    /// </summary>
    public static IReadOnlyList<CarriedItem> BuildCarryOver(IEnumerable<ListItem> previous)
    {
        var seen = new Dictionary<string, (CarriedItem Item, int Order)>();
        var order = 0;
        foreach (var item in previous.Where(i => !i.IsDeleted).OrderBy(i => i.Position).ThenBy(i => i.CreatedAt))
        {
            var name = item.Name.Trim();
            if (name.Length == 0)
            {
                continue;
            }

            var key = Key(ListKind.Monthly, name);
            if (seen.TryGetValue(key, out var existing))
            {
                if (item.Included)
                {
                    seen[key] = (existing.Item with { SkippedLast = false }, existing.Order);
                }

                continue;
            }

            seen[key] = (new CarriedItem(key, name, item.Qty > 0 ? item.Qty : 1, item.Unit, item.Notes, !item.Included, 0), order++);
        }

        return seen.Values
            .OrderBy(x => x.Item.SkippedLast)
            .ThenBy(x => x.Order)
            .Select((x, i) => x.Item with { Position = i + 1 })
            .ToList();
    }

    /// <summary>
    /// Expenses: every expense comes forward with its amount, due day and notes, unpaid, so a month you forgot to
    /// open still has everything in it. One that was due but never marked paid is flagged. One skipped last month
    /// ("not this month") still comes back, flagged and listed last. Exact duplicate names collapse into one.
    /// </summary>
    public static IReadOnlyList<CarriedItem> BuildExpenseCarryOver(IEnumerable<ListItem> previous)
    {
        var seen = new Dictionary<string, (CarriedItem Item, int Order)>();
        var order = 0;
        foreach (var item in previous.Where(i => !i.IsDeleted).OrderBy(i => i.Position).ThenBy(i => i.CreatedAt))
        {
            var name = item.Name.Trim();
            if (name.Length == 0)
            {
                continue;
            }

            var key = Key(ListKind.Expenses, name);
            if (seen.TryGetValue(key, out var existing))
            {
                if (item.Included && existing.Item.SkippedLast)
                {
                    seen[key] = (existing.Item with { SkippedLast = false, UnpaidLast = !item.Bought, Amount = item.Price ?? existing.Item.Amount }, existing.Order);
                }

                continue;
            }

            seen[key] = (new CarriedItem(key, name, 1, null, item.Notes, !item.Included, 0, item.Price, item.DueDay, item.Included && !item.Bought), order++);
        }

        return seen.Values
            .OrderBy(x => x.Item.SkippedLast)
            .ThenBy(x => x.Order)
            .Select((x, i) => x.Item with { Position = i + 1 })
            .ToList();
    }
}
