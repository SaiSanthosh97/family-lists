namespace FamilyLists.Core.Lists;

/// <summary>An item as copied into the next month.</summary>
/// <param name="Key">The normalised name: two phones copying the same month derive the same item id from it.</param>
public sealed record CarriedItem(string Key, string Name, decimal Qty, string? Unit, string? Notes, bool SkippedLast, int Position);

/// <summary>Builds next month's draft from the most recent month's items. Ported from the web version.</summary>
public static class Rollover
{
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

            var key = ProductNames.Normalize(name) is { Length: > 0 } normal ? normal : name.ToLowerInvariant();
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
}
