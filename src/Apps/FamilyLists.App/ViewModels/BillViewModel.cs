using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FamilyLists.App.Services;
using FamilyLists.App.Views;
using FamilyLists.Core.Bills;
using FamilyLists.Core.Lists;

namespace FamilyLists.App.ViewModels;

/// <summary>One line on the review screen. Choices: 0 = new item, 1 = skip, 2.. = the month's items (best match first).</summary>
public sealed partial class BillLineRow : ObservableObject
{
    public const int NewChoice = 0;
    public const int SkipChoice = 1;

    private readonly Action _changed;

    public BillLineRow(BillLine line, IReadOnlyList<ListItem> items, bool readOnly, string currency, Action changed)
    {
        _changed = changed;
        RawText = line.RawText is { } raw && raw != line.Name ? raw : "";
        Name = line.Name;
        Qty = line.Qty?.ToString("0.###", CultureInfo.CurrentCulture) ?? "";
        Amount = line.LineTotal?.ToString("0.00", CultureInfo.CurrentCulture) ?? "";
        ReadOnly = readOnly;
        Ranked = ProductNames.Rank(line.Name, items, i => i.Name);
        Choices = ["➕ New: add to the list", "Skip (bag, deposit, fee…)", .. Ranked.Select(i => "✓ " + i.Name)];
        Choice = line.Action switch
        {
            LineAction.Ignore => SkipChoice,
            LineAction.Match when Ranked.ToList().FindIndex(i => i.Id == line.ItemId) is >= 0 and var index => index + 2,
            _ => NewChoice,
        };
        Summary = $"{line.Action switch { LineAction.Ignore => "Skipped", LineAction.Add => "Added to the list", _ => "On the list" }} · {Money.Format(line.LineTotal, currency)}";
    }

    public IReadOnlyList<ListItem> Ranked { get; }

    public List<string> Choices { get; }

    public bool ReadOnly { get; }

    public bool Editable => !ReadOnly;

    public string RawText { get; }

    public string Summary { get; }

    [ObservableProperty]
    public partial string Name { get; set; }

    [ObservableProperty]
    public partial string Qty { get; set; }

    [ObservableProperty]
    public partial string Amount { get; set; }

    [ObservableProperty]
    public partial int Choice { get; set; }

    partial void OnAmountChanged(string value) => _changed();

    partial void OnChoiceChanged(int value) => _changed();

    public LineEdit ToEdit() => new(
        Name,
        decimal.TryParse(Qty, NumberStyles.Number, CultureInfo.CurrentCulture, out var q) && q > 0 ? q : null,
        null,
        Money.Parse(Amount),
        Choice switch { NewChoice => LineAction.Add, SkipChoice => LineAction.Ignore, _ => LineAction.Match },
        Choice >= 2 && Choice - 2 < Ranked.Count ? Ranked[Choice - 2].Id : null,
        RawText.Length > 0 ? RawText : null);
}

/// <summary>
/// Checking a bill before it's added (draft), or looking at an added one. Lines can be renamed, re-priced, matched
/// to a list item, marked new (goes on next month's list) or skipped; the difference to the printed total is shown.
/// </summary>
public sealed partial class BillViewModel(VaultHost host, Dialogs dialogs) : ObservableObject, ILoads, IQueryAttributable
{
    private string _id = "";
    private string _currency = "INR";
    private Bill? _bill;
    private IReadOnlyList<ListItem> _items = [];

    [ObservableProperty]
    public partial bool IsDraft { get; set; }

    [ObservableProperty]
    public partial string Title { get; set; } = "Bill";

    [ObservableProperty]
    public partial string StoreName { get; set; } = "";

    [ObservableProperty]
    public partial DateTime BillDate { get; set; } = DateTime.Today;

    [ObservableProperty]
    public partial string Total { get; set; } = "";

    [ObservableProperty]
    public partial string Discount { get; set; } = "";

    [ObservableProperty]
    public partial string TotalLabel { get; set; } = "Total";

    [ObservableProperty]
    public partial string Difference { get; set; } = "";

    [ObservableProperty]
    public partial bool HasDifference { get; set; }

    [ObservableProperty]
    public partial string ReadBy { get; set; } = "";

    [ObservableProperty]
    public partial string ConfirmText { get; set; } = "";

    [ObservableProperty]
    public partial string Summary { get; set; } = "";

    [ObservableProperty]
    public partial bool HasPhoto { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    public ObservableCollection<BillLineRow> Lines { get; } = [];

    public void ApplyQueryAttributes(IDictionary<string, object> query) =>
        _id = query.TryGetValue("id", out var id) ? Uri.UnescapeDataString(id.ToString()!) : "";

    public async Task LoadAsync()
    {
        // While a draft is being checked, a sync from another phone must not reload it and wipe the edits.
        if (IsDraft && _bill?.Id == _id)
        {
            return;
        }

        var s = host.Services;
        if (await s.Bills.BillAsync(_id) is not { } bill || await s.ListRepository.GetMonthAsync(bill.MonthId) is not { } month)
        {
            await Nav.BackAsync();
            return;
        }

        _bill = bill;
        _currency = (await s.Families.GetAsync(bill.FamilyId))?.Currency ?? "INR";
        _items = await s.Lists.ItemsAsync(month.ListId, month.Id);
        IsDraft = bill.Status == BillStatus.Draft;
        Title = bill.StoreName ?? "Bill";
        StoreName = bill.StoreName ?? "";
        BillDate = (bill.BillDate ?? DateOnly.FromDateTime(bill.CreatedAt.LocalDateTime)).ToDateTime(TimeOnly.MinValue);
        Total = bill.Total?.ToString("0.00", CultureInfo.CurrentCulture) ?? "";
        Discount = bill.Discount?.ToString("0.00", CultureInfo.CurrentCulture) ?? "";
        TotalLabel = $"Total ({_currency})";
        ReadBy = !IsDraft ? "" : bill.ReadBy == "claude" ? "Read by Claude. Check each line." : "Read on this phone. Check each line: fix names and amounts, and match each to your list.";
        ConfirmText = $"Add to {Period.Label(month.Period)}";
        HasPhoto = bill.PhotoId is not null && bill.PhotoOn == s.Session.MemberId;
        Summary = $"{Money.Format(bill.Total, _currency)} · {bill.BillDate?.ToString("d MMM yyyy", CultureInfo.CurrentCulture)}";

        Lines.Clear();
        foreach (var line in await s.Bills.LinesAsync(bill.Id))
        {
            Lines.Add(new BillLineRow(line, _items, !IsDraft, _currency, Recalculate));
        }

        Recalculate();
    }

    partial void OnTotalChanged(string value) => Recalculate();

    partial void OnDiscountChanged(string value) => Recalculate();

    private void Recalculate()
    {
        var diff = Money.Difference(Lines.Select(l => Money.Parse(l.Amount)), Money.Parse(Total), Money.Parse(Discount));
        HasDifference = IsDraft && diff is not null && diff != 0;
        Difference = diff switch
        {
            > 0 => $"The lines add up to {Money.Format(diff, _currency)} less than the total. A line may be missing or misread (or tax is separate).",
            < 0 => $"The lines add up to {Money.Format(-diff, _currency)} more than the total. Check the amounts and the discount.",
            _ => "",
        };
    }

    [RelayCommand]
    private void AddLine()
    {
        var line = new BillLine { Id = Core.Common.Ids.New(), FamilyId = "", BillId = _id, Name = "", Action = LineAction.Add };
        Lines.Add(new BillLineRow(line, _items, false, _currency, Recalculate));
    }

    [RelayCommand]
    private void RemoveLine(BillLineRow row) => Lines.Remove(row);

    [RelayCommand]
    private async Task ConfirmAsync()
    {
        if (_bill is null)
        {
            return;
        }

        var edits = Lines.Where(l => !string.IsNullOrWhiteSpace(l.Name)).Select(l => l.ToEdit()).ToList();
        var header = new BillEdit(StoreName, DateOnly.FromDateTime(BillDate), null, null, Money.Parse(Discount), Money.Parse(Total));
        IsBusy = true;
        try
        {
            await host.Services.Bills.ConfirmAsync(_bill.Id, header, edits);
            Dialogs.Toast("Bill added. Prices are on the items, and new things will be on next month's list.");
            await Nav.BackAsync();
        }
        catch (BillRuleException ex)
        {
            await dialogs.AlertAsync("Add the bill", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        var message = IsDraft ? "Delete this bill? Nothing was added from it yet." : "Delete this bill for everyone in the group? The prices it set are worked out again.";
        if (_bill is not null && await dialogs.ConfirmAsync("Delete bill", message, "Delete"))
        {
            await host.Services.Bills.DeleteAsync(_bill.Id);
            await Nav.BackAsync();
        }
    }

    [RelayCommand]
    private async Task ViewPhotoAsync()
    {
        if (_bill is not null && await host.Services.Bills.PhotoAsync(_bill) is { } bytes)
        {
            await BillReading.ViewAsync(bytes);
        }
    }
}
