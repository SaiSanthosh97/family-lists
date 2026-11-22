using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FamilyLists.App.Services;
using FamilyLists.App.Views;
using FamilyLists.Core.Bills;
using FamilyLists.Core.Lists;

namespace FamilyLists.App.ViewModels;

public sealed record MonthBar(string Period, string Label, string Amount, double Width, bool Selected);

public sealed record ShareRow(string Name, string Detail, string Amount, double Width);

public sealed record HistoryRow(string Name, string Prices);

/// <summary>What a monthly list cost: month by month, the selected month's breakdown by product, and price history.</summary>
public sealed partial class SpendingViewModel(VaultHost host) : ObservableObject, ILoads, IQueryAttributable
{
    private const double MaxBar = 220;
    private string _listId = "";
    private string? _selected;
    private IReadOnlyList<MonthSpending> _months = [];
    private string _currency = "INR";

    [ObservableProperty]
    public partial string Title { get; set; } = "Spending";

    [ObservableProperty]
    public partial string Average { get; set; } = "";

    [ObservableProperty]
    public partial string MonthTitle { get; set; } = "";

    [ObservableProperty]
    public partial string MonthTotal { get; set; } = "";

    [ObservableProperty]
    public partial string MonthSource { get; set; } = "";

    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    [ObservableProperty]
    public partial string EmptyText { get; set; } = "No spending yet. Add a bill or type a month's total, and it shows here.";

    [ObservableProperty]
    public partial string HistoryTitle { get; set; } = "Prices over time";

    public ObservableCollection<MonthBar> Bars { get; } = [];

    public ObservableCollection<ShareRow> Breakdown { get; } = [];

    public ObservableCollection<HistoryRow> History { get; } = [];

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        _listId = query.TryGetValue("list", out var l) ? Uri.UnescapeDataString(l.ToString()!) : "";
        _selected = query.TryGetValue("period", out var p) ? p.ToString() : null;
    }

    public async Task LoadAsync()
    {
        var s = host.Services;
        if (await s.Lists.GetListAsync(_listId) is not { } list)
        {
            await Nav.BackAsync();
            return;
        }

        Title = list.Kind == ListKind.Expenses ? $"Month by month · {list.Name}" : $"Spending · {list.Name}";
        if (list.Kind == ListKind.Expenses)
        {
            EmptyText = "No expenses with an amount yet. Add one to a month, and it shows here.";
            HistoryTitle = "Amounts over time";
        }

        _currency = (await s.Families.GetAsync(list.FamilyId))?.Currency ?? "INR";
        _months = await s.Spending.ForListAsync(_listId);
        var withTotal = _months.Where(m => m.Total is not null).ToList();
        IsEmpty = withTotal.Count == 0;
        Average = withTotal.Count > 0 ? "Average " + Money.Format(withTotal.Average(m => m.Total!.Value), _currency) : "";
        if (_selected is null || withTotal.All(m => m.Month.Period != _selected))
        {
            _selected = withTotal.LastOrDefault()?.Month.Period;
        }

        History.Clear();
        foreach (var product in SpendingService.History(_months).Where(p => p.Prices.Count > 0).Take(40))
        {
            History.Add(new HistoryRow(product.Name, string.Join("  ·  ", product.Prices.Select(p => $"{Period.ShortLabel(p.Period)} {Money.Format(p.Price, _currency)}"))));
        }

        Show();
    }

    private void Show()
    {
        var recent = _months.TakeLast(12).ToList();
        var max = recent.Select(m => m.Total ?? 0).DefaultIfEmpty(0).Max();
        Bars.Clear();
        foreach (var m in recent.AsEnumerable().Reverse())
        {
            var width = max == 0 ? 0 : (double)((m.Total ?? 0) / max) * MaxBar;
            Bars.Add(new MonthBar(m.Month.Period, Period.ShortLabel(m.Month.Period), Money.Format(m.Total, _currency), Math.Max(width, m.Total is null ? 0 : 4), m.Month.Period == _selected));
        }

        Breakdown.Clear();
        if (_months.FirstOrDefault(m => m.Month.Period == _selected) is not { } month)
        {
            MonthTitle = "";
            return;
        }

        MonthTitle = Period.Label(month.Month.Period);
        MonthTotal = Money.Format(month.Total, _currency);
        MonthSource = Display.SpendSourceText(month.Source, month.Bills.Count(b => b.Status == BillStatus.Confirmed));
        var rows = month.Items.Where(i => i.Price is not null)
            .Select(i => (i.Name, Detail: Display.Qty(i.Qty, i.Unit), Amount: i.Price!.Value))
            .Concat(month.OtherLines.Where(l => l.LineTotal is not null).Select(l => (l.Name, Detail: "not a list item", Amount: l.LineTotal!.Value)))
            .OrderByDescending(r => r.Amount)
            .ToList();
        var biggest = rows.Select(r => r.Amount).DefaultIfEmpty(0).Max();
        foreach (var (name, detail, amount) in rows)
        {
            Breakdown.Add(new ShareRow(name, detail, Money.Format(amount, _currency), biggest == 0 ? 0 : (double)(amount / biggest) * 300));
        }
    }

    [RelayCommand]
    private void Select(MonthBar bar)
    {
        _selected = bar.Period;
        Show();
    }
}
