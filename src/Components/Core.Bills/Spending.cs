using FamilyLists.Core.Lists;

namespace FamilyLists.Core.Bills;

/// <summary>What one month of a list cost.</summary>
/// <param name="Items">Items bought or priced that month (the product breakdown).</param>
/// <param name="OtherLines">Skipped bill lines (bags, fees): part of the total, not a list item.</param>
public sealed record MonthSpending(MonthList Month, decimal? Total, SpendSource Source, IReadOnlyList<ListItem> Items, IReadOnlyList<BillLine> OtherLines, IReadOnlyList<Bill> Bills);

/// <summary>One product's price month by month.</summary>
public sealed record ProductHistory(string Name, IReadOnlyList<(string Period, decimal Price)> Prices)
{
    public decimal Latest => Prices[^1].Price;
}

/// <summary>Spending of a monthly list: per month, per product, and each product's price over time.</summary>
public sealed class SpendingService(IListRepository lists, IBillRepository bills)
{
    /// <summary>Every month of the list, oldest first.</summary>
    public async Task<IReadOnlyList<MonthSpending>> ForListAsync(string listId, CancellationToken ct = default)
    {
        var months = await lists.MonthsAsync(listId, ct);
        var items = (await lists.AllItemsAsync(listId, ct)).GroupBy(i => i.MonthId ?? "").ToDictionary(g => g.Key, g => g.ToList());
        if (await lists.GetListAsync(listId, ct) is { Kind: ListKind.Expenses })
        {
            return ExpenseMonths(months, items);
        }

        var allBills = (await bills.BillsOfListAsync(listId, ct)).GroupBy(b => b.MonthId).ToDictionary(g => g.Key, g => g.ToList());

        var result = new List<MonthSpending>();
        foreach (var month in months)
        {
            var monthItems = items.GetValueOrDefault(month.Id) ?? [];
            var monthBills = allBills.GetValueOrDefault(month.Id) ?? [];
            var (total, source) = Money.MonthSpend(monthBills, month.ManualTotal, monthItems.Select(i => i.Price));
            var other = new List<BillLine>();
            foreach (var bill in monthBills.Where(b => b.Status == BillStatus.Confirmed))
            {
                other.AddRange((await bills.LinesAsync(bill.Id, ct)).Where(l => l.Action == LineAction.Ignore));
            }

            result.Add(new MonthSpending(month, total, source, monthItems.Where(i => i.Bought || i.Price is not null).ToList(), other, monthBills));
        }

        return result;
    }

    /// <summary>An expense list's months: each one's total is its expenses that were due (skipped ones left out).</summary>
    private static List<MonthSpending> ExpenseMonths(IEnumerable<MonthList> months, Dictionary<string, List<ListItem>> items) =>
        months.Select(month =>
        {
            var due = (items.GetValueOrDefault(month.Id) ?? []).Where(i => i.Included && i.Price is not null).ToList();
            return new MonthSpending(
                month,
                due.Count > 0 ? Money.Sum(due.Select(i => i.Price)) : null,
                due.Count > 0 ? SpendSource.Expenses : SpendSource.None,
                due,
                [],
                []);
        }).ToList();

    /// <summary>Products with a price in at least one month, grouped by name (sizes and plurals ignored), most bought first.</summary>
    public static IReadOnlyList<ProductHistory> History(IEnumerable<MonthSpending> months)
    {
        var byName = new Dictionary<string, (string Name, List<(string Period, decimal Price)> Prices)>();
        foreach (var month in months.OrderBy(m => m.Month.Period, StringComparer.Ordinal))
        {
            foreach (var item in month.Items.Where(i => i.Price is not null))
            {
                var key = ProductNames.Normalize(item.Name) is { Length: > 0 } k ? k : item.Name.ToLowerInvariant();
                if (!byName.TryGetValue(key, out var entry))
                {
                    entry = (item.Name, []);
                    byName[key] = entry;
                }

                var existing = entry.Prices.FindIndex(p => p.Period == month.Month.Period);
                if (existing >= 0)
                {
                    entry.Prices[existing] = (month.Month.Period, entry.Prices[existing].Price + item.Price!.Value);
                }
                else
                {
                    entry.Prices.Add((month.Month.Period, item.Price!.Value));
                }
            }
        }

        return byName.Values
            .OrderByDescending(e => e.Prices.Count)
            .ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(e => new ProductHistory(e.Name, e.Prices))
            .ToList();
    }
}
