using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FamilyLists.App.Services;
using FamilyLists.App.Views;
using FamilyLists.Core.Lists;

namespace FamilyLists.App.ViewModels;

/// <summary>
/// One month of an expense list: what's due, what's paid and what's left. Starting a month copies last month's
/// expenses with their amounts; "Bring in" copies ones added to last month after this month was started.
/// </summary>
public sealed partial class ExpensesViewModel(VaultHost host, Dialogs dialogs) : ObservableObject, ILoads, IQueryAttributable
{
    private string _listId = "";
    private string _period = "";
    private string _currency = "INR";
    private MonthList? _month;

    [ObservableProperty]
    public partial string Title { get; set; } = "";

    [ObservableProperty]
    public partial string PeriodLabel { get; set; } = "";

    [ObservableProperty]
    public partial string StatusText { get; set; } = "";

    [ObservableProperty]
    public partial bool IsNotStarted { get; set; }

    [ObservableProperty]
    public partial bool IsStarted { get; set; }

    [ObservableProperty]
    public partial string StartTitle { get; set; } = "";

    [ObservableProperty]
    public partial string StartText { get; set; } = "";

    [ObservableProperty]
    public partial string StartButton { get; set; } = "";

    [ObservableProperty]
    public partial string TotalText { get; set; } = "";

    [ObservableProperty]
    public partial string PaidText { get; set; } = "";

    [ObservableProperty]
    public partial string LeftText { get; set; } = "";

    [ObservableProperty]
    public partial double Progress { get; set; }

    [ObservableProperty]
    public partial string OverdueText { get; set; } = "";

    [ObservableProperty]
    public partial string MissingText { get; set; } = "";

    [ObservableProperty]
    public partial string NewExpense { get; set; } = "";

    [ObservableProperty]
    public partial string UnpaidTitle { get; set; } = "";

    [ObservableProperty]
    public partial string PaidTitle { get; set; } = "";

    [ObservableProperty]
    public partial string SkippedTitle { get; set; } = "";

    [ObservableProperty]
    public partial bool HasPaid { get; set; }

    [ObservableProperty]
    public partial bool HasSkipped { get; set; }

    public ObservableCollection<ItemRow> Unpaid { get; } = [];

    public ObservableCollection<ItemRow> Paid { get; } = [];

    public ObservableCollection<ItemRow> Skipped { get; } = [];

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        _listId = query.TryGetValue("list", out var l) ? Uri.UnescapeDataString(l.ToString()!) : "";
        _period = query.TryGetValue("period", out var p) && Period.TryParse(p.ToString(), out _) ? p.ToString()! : "";
    }

    public async Task LoadAsync()
    {
        var s = host.Services;
        if (await s.Lists.GetListAsync(_listId) is not { } list)
        {
            await Nav.BackAsync();
            return;
        }

        if (_period.Length == 0)
        {
            _period = Period.Of(s.Session.Today);
        }

        Title = list.Name;
        _currency = (await s.Families.GetAsync(list.FamilyId))?.Currency ?? "INR";
        PeriodLabel = Period.Label(_period);
        _month = await s.Lists.MonthAsync(_listId, _period);
        Unpaid.Clear();
        Paid.Clear();
        Skipped.Clear();
        MissingText = "";
        OverdueText = "";
        StatusText = "";
        IsNotStarted = _month is null;
        IsStarted = _month is not null;

        if (_month is null)
        {
            await LoadNotStartedAsync();
            HasPaid = HasSkipped = false;
            return;
        }

        var today = s.Session.Today;
        var items = await s.Lists.ItemsAsync(_listId, _month.Id);
        var summary = Expenses.Summarize(items, _period, today);
        TotalText = Display.Money(summary.Total, _currency);
        PaidText = Display.Money(summary.Paid, _currency);
        LeftText = Display.Money(summary.Left, _currency);
        Progress = summary.Total == 0 ? 0 : (double)(summary.Paid / summary.Total);
        StatusText = summary.Count == 0 ? "" : summary.PaidCount == summary.Count ? "All paid" : $"{summary.PaidCount} of {summary.Count} paid";
        OverdueText = summary.Overdue switch
        {
            0 => "",
            1 => "1 expense is past its due date.",
            var n => $"{n} expenses are past their due date.",
        };

        var previousName = await s.Lists.PreviousMonthAsync(_listId, _period) is { } previous ? Period.MonthName(previous.Period) : "last month";
        var due = items.Where(i => i.Included).ToList();
        foreach (var i in due.Where(i => !i.Bought).OrderBy(i => Expenses.DueDate(i, _period) ?? DateOnly.MaxValue).ThenBy(i => i.Position))
        {
            Unpaid.Add(Row(i, today, previousName));
        }

        foreach (var i in due.Where(i => i.Bought))
        {
            Paid.Add(Row(i, today, previousName));
        }

        foreach (var i in items.Where(i => !i.Included))
        {
            Skipped.Add(Row(i, today, previousName));
        }

        UnpaidTitle = $"To pay ({Unpaid.Count})";
        PaidTitle = $"Paid ({Paid.Count})";
        SkippedTitle = $"Not this month ({Skipped.Count})";
        HasPaid = Paid.Count > 0;
        HasSkipped = Skipped.Count > 0;

        var (from, missing) = await s.Lists.MissingFromPreviousAsync(_listId, _period);
        if (from is not null && missing.Count > 0)
        {
            var amount = missing.Where(m => !m.SkippedLast).Sum(m => m.Amount ?? 0);
            MissingText = (missing.Count == 1 ? $"1 expense from {Period.MonthName(from.Period)} isn't here" : $"{missing.Count} expenses from {Period.MonthName(from.Period)} aren't here")
                + (amount > 0 ? $" ({Display.Money(amount, _currency)})." : ".");
        }
    }

    private async Task LoadNotStartedAsync()
    {
        var s = host.Services;
        var name = Period.MonthName(_period);
        StartTitle = $"{name}'s expenses";
        if (await s.Lists.PreviousMonthAsync(_listId, _period) is not { } previous)
        {
            StartText = "Add your regular expenses once: rent, electricity, school fees, EMIs. Next month they're copied forward for you, with their amounts.";
            StartButton = "Start this month";
            return;
        }

        var carried = Rollover.BuildExpenseCarryOver(await s.Lists.ItemsAsync(_listId, previous.Id));
        var total = carried.Where(c => !c.SkippedLast).Sum(c => c.Amount ?? 0);
        var unpaid = carried.Count(c => c.UnpaidLast);
        StartText = (carried.Count == 1 ? $"We'll copy the 1 expense from {Period.Label(previous.Period)}" : $"We'll copy all {carried.Count} expenses from {Period.Label(previous.Period)}")
            + (total > 0 ? $", {Display.Money(total, _currency)} in all," : "")
            + " with their amounts and due days. Change any amount that's different this month."
            + (unpaid > 0 ? $" {unpaid} weren't marked paid there; they're flagged." : "");
        StartButton = $"Copy to {name}";
    }

    private ItemRow Row(ListItem i, DateOnly today, string previousName)
    {
        var (badge, color) = Expenses.IsOverdue(i, _period, today) ? ("overdue", Palette.Danger)
            : i.UnpaidLast && !i.Bought ? ($"not marked paid in {previousName}", Palette.Warning)
            : i.SkippedLast && !i.Bought ? ($"skipped in {previousName}", Palette.Subtle)
            : (i.Notes, Palette.Subtle);
        var detail = Expenses.DueDate(i, _period) is { } date ? "due " + date.ToString("d MMM", CultureInfo.CurrentCulture) : "";
        var amount = i.Price is { } p ? Display.Money(p, _currency) : "no amount";
        return new ItemRow(i.Id, i.Name, detail, amount, i.Bought, false, badge, color);
    }

    [RelayCommand]
    private Task PreviousAsync()
    {
        _period = Period.Shift(_period, -1);
        return LoadAsync();
    }

    [RelayCommand]
    private Task NextAsync()
    {
        _period = Period.Shift(_period, 1);
        return LoadAsync();
    }

    [RelayCommand]
    private async Task StartAsync()
    {
        await host.Services.Lists.StartMonthAsync(_listId, _period);
        await LoadAsync();
    }

    [RelayCommand]
    private async Task BringInAsync()
    {
        var count = await host.Services.Lists.CarryMissingAsync(_listId, _period);
        Dialogs.Toast(count == 1 ? "Brought in 1 expense." : $"Brought in {count} expenses.");
        await LoadAsync();
    }

    [RelayCommand]
    private async Task AddAsync()
    {
        if (_month is null || string.IsNullOrWhiteSpace(NewExpense))
        {
            return;
        }

        var (name, amount) = Display.ParseExpense(NewExpense);
        NewExpense = "";
        try
        {
            await host.Services.Lists.AddExpenseAsync(_listId, _month.Id, name, amount);
        }
        catch (ListRuleException ex)
        {
            await dialogs.AlertAsync("Add an expense", ex.Message);
        }

        await LoadAsync();
    }

    [RelayCommand]
    private async Task ToggleAsync(ItemRow row)
    {
        if (Skipped.Any(r => r.Id == row.Id))
        {
            // Ticking one that was skipped: it's due (and paid) after all.
            await host.Services.Lists.UpdateItemAsync(row.Id, i =>
            {
                i.Included = true;
                i.Bought = true;
            });
        }
        else
        {
            await host.Services.Lists.SetBoughtAsync(row.Id, !row.Checked);
        }

        await LoadAsync();
    }

    [RelayCommand]
    private Task OpenAsync(ItemRow row) => Nav.ItemAsync(row.Id);

    [RelayCommand]
    private Task HistoryAsync() => Nav.SpendingAsync(_listId, _period);

    [RelayCommand]
    private async Task MenuAsync()
    {
        const string history = "Month by month", rename = "Rename list", delete = "Delete list";
        switch (await dialogs.ChooseAsync(Title, history, rename, delete))
        {
            case history:
                await HistoryAsync();
                break;
            case rename when await dialogs.PromptAsync(rename, "New name", Title, maxLength: 60) is { Length: > 0 } name:
                await host.Services.Lists.RenameListAsync(_listId, name);
                await LoadAsync();
                break;
            case delete when await dialogs.ConfirmAsync(delete, $"Delete \"{Title}\" and all its months, for everyone in the group? This can't be undone.", "Delete"):
                await host.Services.Lists.DeleteListAsync(_listId);
                await Nav.BackAsync();
                break;
        }
    }
}
