using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FamilyLists.App.Services;
using FamilyLists.App.Views;
using FamilyLists.Core.Bills;
using FamilyLists.Core.Lists;

namespace FamilyLists.App.ViewModels;

/// <summary>One row on a list: a tappable box (needed / in the cart / done) and a tappable name (opens the item).</summary>
public sealed record ItemRow(string Id, string Name, string Detail, string? Price, bool Checked, bool Struck, string? Badge, Color BadgeColor);

public sealed record BillRow(string Id, string Title, string Amount, string Status);

/// <summary>
/// One month of a monthly list, in the four states of the month:
/// not started ("Start December list" copies last month), reviewing (untick what isn't needed, add, Finalize),
/// shopping (tick into the cart; add a bill or "Done, no bill"), shopped (what it cost, the bills, bought / didn't get).
/// </summary>
public sealed partial class MonthViewModel(VaultHost host, BillReading reading, Dialogs dialogs) : ObservableObject, ILoads, IQueryAttributable
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
    public partial bool IsDraft { get; set; }

    [ObservableProperty]
    public partial bool IsFinal { get; set; }

    [ObservableProperty]
    public partial bool IsShopped { get; set; }

    [ObservableProperty]
    public partial bool CanAdd { get; set; }

    [ObservableProperty]
    public partial string StartTitle { get; set; } = "";

    [ObservableProperty]
    public partial string StartText { get; set; } = "";

    [ObservableProperty]
    public partial string StartButton { get; set; } = "";

    [ObservableProperty]
    public partial string NewItem { get; set; } = "";

    [ObservableProperty]
    public partial string Hint { get; set; } = "";

    [ObservableProperty]
    public partial string ItemsTitle { get; set; } = "";

    [ObservableProperty]
    public partial string SecondTitle { get; set; } = "";

    [ObservableProperty]
    public partial bool HasSecond { get; set; }

    [ObservableProperty]
    public partial string ProgressText { get; set; } = "";

    [ObservableProperty]
    public partial double Progress { get; set; }

    [ObservableProperty]
    public partial string SpendText { get; set; } = "";

    [ObservableProperty]
    public partial string SpendSource { get; set; } = "";

    [ObservableProperty]
    public partial bool HasBills { get; set; }

    [ObservableProperty]
    public partial bool HasSharedBill { get; set; }

    [ObservableProperty]
    public partial string FinalizeText { get; set; } = "";

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string BusyText { get; set; } = "";

    public ObservableCollection<ItemRow> Items { get; } = [];

    public ObservableCollection<ItemRow> Second { get; } = [];

    public ObservableCollection<BillRow> Bills { get; } = [];

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
        HasSharedBill = Inbox.HasBill;
        _month = await s.Lists.MonthAsync(_listId, _period);
        Items.Clear();
        Second.Clear();
        Bills.Clear();
        HasBills = false;

        IsNotStarted = _month is null;
        IsDraft = _month?.Status == MonthStatus.Draft;
        IsFinal = _month?.Status == MonthStatus.Final;
        IsShopped = _month?.Status == MonthStatus.Shopped;
        CanAdd = _month is not null;
        StatusText = _month?.Status switch
        {
            MonthStatus.Draft => "Reviewing",
            MonthStatus.Final => "Final list · shopping",
            MonthStatus.Shopped => "Shopped",
            _ => "",
        };

        if (_month is null)
        {
            await LoadNotStartedAsync();
            return;
        }

        var items = await s.Lists.ItemsAsync(_listId, _month.Id);
        if (IsDraft)
        {
            Hint = "Untick anything you don't need this month and add anything new. When you both agree, finalize the list.";
            Fill(Items, items.Where(i => i.Included), i => i.Included, struck: false);
            Fill(Second, items.Where(i => !i.Included), i => i.Included, struck: false);
            ItemsTitle = $"Needed ({items.Count(i => i.Included)})";
            SecondTitle = $"Not needed this month ({Second.Count})";
            FinalizeText = $"Finalize list ({items.Count(i => i.Included)} items)";
        }
        else if (IsFinal)
        {
            var needed = items.Where(i => i.Included).ToList();
            var inCart = needed.Count(i => i.Bought);
            Hint = "Tick things as they go in the cart. Everyone in the group sees the ticks.";
            Fill(Items, needed.OrderBy(i => i.Bought), i => i.Bought, struck: true);
            Fill(Second, items.Where(i => !i.Included), i => i.Bought, struck: false);
            ItemsTitle = "Shopping list";
            SecondTitle = $"Not needed this month ({Second.Count})";
            ProgressText = $"{inCart} of {needed.Count} in the cart";
            Progress = needed.Count == 0 ? 0 : (double)inCart / needed.Count;
        }
        else
        {
            Hint = "Anything you add here is copied to next month's list too.";
            Fill(Items, items.Where(i => i.Bought), i => i.Bought, struck: false);
            Fill(Second, items.Where(i => i.Included && !i.Bought), i => i.Bought, struck: false);
            ItemsTitle = $"Bought ({Items.Count})";
            SecondTitle = $"Didn't get ({Second.Count})";
        }

        HasSecond = Second.Count > 0;
        var bills = await s.Bills.BillsOfMonthAsync(_month.Id);
        foreach (var bill in bills)
        {
            var date = bill.BillDate?.ToString("d MMM", CultureInfo.CurrentCulture) ?? bill.CreatedAt.ToLocalTime().ToString("d MMM", CultureInfo.CurrentCulture);
            Bills.Add(new BillRow(bill.Id, bill.StoreName ?? "Bill", Display.Money(bill.Total, _currency) + " · " + date, bill.Status == BillStatus.Confirmed ? "Added" : "Check it"));
        }

        HasBills = Bills.Count > 0;
        var (total, source) = Money.MonthSpend(bills, _month.ManualTotal, items.Select(i => i.Price));
        SpendText = Display.Money(total, _currency);
        SpendSource = Display.SpendSourceText(source, bills.Count(b => b.Status == BillStatus.Confirmed));
    }

    private async Task LoadNotStartedAsync()
    {
        var s = host.Services;
        var previous = await s.Lists.PreviousMonthAsync(_listId, _period);
        var name = Period.MonthName(_period);
        StartTitle = $"Time for {name}'s list";
        if (previous is null)
        {
            StartText = "Start your first list. Next month it will be copied forward for you automatically.";
            StartButton = "Start the list";
        }
        else
        {
            var count = Rollover.BuildCarryOver(await s.Lists.ItemsAsync(_listId, previous.Id)).Count;
            StartText = $"We'll copy all {count} items from {Period.Label(previous.Period)}, including the extras you bought. Then review it together: untick what you don't need and add anything new.";
            StartButton = $"Start {name} list";
        }
    }

    private void Fill(ObservableCollection<ItemRow> target, IEnumerable<ListItem> items, Func<ListItem, bool> isChecked, bool struck)
    {
        foreach (var i in items)
        {
            var (badge, color) = i.SkippedLast && IsDraft ? ("skipped last month", Palette.Warning)
                : i.Source == ItemSource.Bill ? ("from bill", Palette.Info)
                : (null, Palette.Subtle);
            var price = i.Price is { } p ? Display.Money(p, _currency) : null;
            target.Add(new ItemRow(i.Id, i.Name, Display.Qty(i.Qty, i.Unit), price, isChecked(i), struck && isChecked(i), badge, color));
        }
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
    private async Task AddAsync()
    {
        if (_month is null || string.IsNullOrWhiteSpace(NewItem))
        {
            return;
        }

        var (name, qty) = Display.ParseQuickAdd(NewItem);
        NewItem = "";
        var added = await host.Services.Lists.AddItemAsync(_listId, _month.Id, name, qty);
        if (IsShopped)
        {
            // Added after shopping: it was bought (and goes on next month's list).
            await host.Services.Lists.SetBoughtAsync(added.Id, true);
        }

        await LoadAsync();
    }

    [RelayCommand]
    private async Task ToggleAsync(ItemRow row)
    {
        if (IsDraft)
        {
            await host.Services.Lists.SetIncludedAsync(row.Id, !row.Checked);
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
    private async Task FinalizeAsync()
    {
        if (_month is null)
        {
            return;
        }

        if (Items.Count == 0)
        {
            await dialogs.AlertAsync("Finalize", "Add at least one item first.");
            return;
        }

        await host.Services.Lists.SetStatusAsync(_month.Id, MonthStatus.Final);
        await LoadAsync();
    }

    [RelayCommand]
    private async Task EditListAsync()
    {
        if (_month is not null)
        {
            await host.Services.Lists.SetStatusAsync(_month.Id, MonthStatus.Draft);
            await LoadAsync();
        }
    }

    [RelayCommand]
    private async Task NoBillAsync()
    {
        if (_month is not null && await dialogs.ConfirmAsync("Done shopping?", "Mark this month as shopped without a bill? You can type what it cost.", "Done"))
        {
            await host.Services.Lists.SetStatusAsync(_month.Id, MonthStatus.Shopped);
            await LoadAsync();
        }
    }

    [RelayCommand]
    private async Task BackToShoppingAsync()
    {
        if (_month is not null)
        {
            await host.Services.Lists.SetStatusAsync(_month.Id, MonthStatus.Final);
            await LoadAsync();
        }
    }

    [RelayCommand]
    private async Task ReadBillAsync()
    {
        if (_month is null || await reading.PickAsync() is not { } picked)
        {
            return;
        }

        await ReadAsync(picked.Bytes);
    }

    [RelayCommand]
    private async Task UseSharedBillAsync()
    {
        if (_month is not null && Inbox.Take() is { } shared)
        {
            HasSharedBill = false;
            await ReadAsync(shared.Content);
        }
    }

    private async Task ReadAsync(byte[] bytes)
    {
        BusyText = "Reading the bill…";
        IsBusy = true;
        try
        {
            if (await reading.ReadIntoDraftAsync(_month!.Id, bytes) is { } billId)
            {
                await Nav.BillAsync(billId);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Android.Util.Log.Warn("FamilyLists", $"Reading a bill failed: {ex}");
            await dialogs.AlertAsync("Add a bill", "The bill couldn't be read. Try a sharper photo in good light, or type the total instead.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ManualTotalAsync()
    {
        if (_month is null)
        {
            return;
        }

        var text = await dialogs.PromptAsync("What did it cost?", "The month's total when there's no bill. Leave empty to remove it.",
            _month.ManualTotal?.ToString("0.##", CultureInfo.InvariantCulture), Keyboard.Numeric, 12);
        if (text is null)
        {
            return;
        }

        await host.Services.Lists.SetManualTotalAsync(_month.Id, Money.Parse(text));
        await LoadAsync();
    }

    [RelayCommand]
    private Task OpenBillAsync(BillRow row) => Nav.BillAsync(row.Id);

    [RelayCommand]
    private Task SpendingAsync() => Nav.SpendingAsync(_listId, _period);

    [RelayCommand]
    private async Task MenuAsync()
    {
        const string rename = "Rename list", delete = "Delete list", spending = "Spending";
        switch (await dialogs.ChooseAsync(Title, spending, rename, delete))
        {
            case spending:
                await SpendingAsync();
                break;
            case rename when await dialogs.PromptAsync(rename, "New name", Title, maxLength: 60) is { Length: > 0 } name:
                await host.Services.Lists.RenameListAsync(_listId, name);
                await LoadAsync();
                break;
            case delete when await dialogs.ConfirmAsync(delete, $"Delete \"{Title}\" and all its months, bills and spending, for everyone in the group? This can't be undone.", "Delete"):
                await host.Services.Lists.DeleteListAsync(_listId);
                await Nav.BackAsync();
                break;
        }
    }
}
