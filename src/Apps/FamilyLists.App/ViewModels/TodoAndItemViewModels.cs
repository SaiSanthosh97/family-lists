using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FamilyLists.App.Services;
using FamilyLists.App.Views;
using FamilyLists.Core.Bills;
using FamilyLists.Core.Lists;

namespace FamilyLists.App.ViewModels;

/// <summary>A to-do list: tick tasks done, add, reorder from the task screen, clear done.</summary>
public sealed partial class TodoViewModel(VaultHost host, Dialogs dialogs) : ObservableObject, ILoads, IQueryAttributable
{
    private string _listId = "";

    [ObservableProperty]
    public partial string Title { get; set; } = "";

    [ObservableProperty]
    public partial string NewItem { get; set; } = "";

    [ObservableProperty]
    public partial string DoneTitle { get; set; } = "";

    [ObservableProperty]
    public partial bool HasDone { get; set; }

    [ObservableProperty]
    public partial bool HasOpen { get; set; }

    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    public ObservableCollection<ItemRow> Open { get; } = [];

    public ObservableCollection<ItemRow> Done { get; } = [];

    public void ApplyQueryAttributes(IDictionary<string, object> query) =>
        _listId = query.TryGetValue("list", out var l) ? Uri.UnescapeDataString(l.ToString()!) : "";

    public async Task LoadAsync()
    {
        if (await host.Services.Lists.GetListAsync(_listId) is not { } list)
        {
            await Nav.BackAsync();
            return;
        }

        Title = list.Name;
        var items = await host.Services.Lists.ItemsAsync(_listId, null);
        Open.Clear();
        Done.Clear();
        foreach (var i in items)
        {
            (i.Bought ? Done : Open).Add(new ItemRow(i.Id, i.Name, Display.Qty(i.Qty, i.Unit), null, i.Bought, i.Bought, i.Notes, Palette.Subtle));
        }

        DoneTitle = $"Done ({Done.Count})";
        HasDone = Done.Count > 0;
        HasOpen = Open.Count > 0;
        IsEmpty = items.Count == 0;
    }

    [RelayCommand]
    private async Task AddAsync()
    {
        if (string.IsNullOrWhiteSpace(NewItem))
        {
            return;
        }

        var (name, qty) = Display.ParseQuickAdd(NewItem);
        NewItem = "";
        await host.Services.Lists.AddItemAsync(_listId, null, name, qty);
        await LoadAsync();
    }

    [RelayCommand]
    private async Task ToggleAsync(ItemRow row)
    {
        await host.Services.Lists.SetBoughtAsync(row.Id, !row.Checked);
        await LoadAsync();
    }

    [RelayCommand]
    private Task OpenItemAsync(ItemRow row) => Nav.ItemAsync(row.Id);

    [RelayCommand]
    private async Task ClearDoneAsync()
    {
        if (await dialogs.ConfirmAsync("Clear done", "Delete every done task, for everyone in the group?", "Clear"))
        {
            await host.Services.Lists.ClearDoneAsync(_listId);
            await LoadAsync();
        }
    }

    [RelayCommand]
    private async Task MenuAsync()
    {
        const string rename = "Rename list", delete = "Delete list";
        switch (await dialogs.ChooseAsync(Title, rename, delete))
        {
            case rename when await dialogs.PromptAsync(rename, "New name", Title, maxLength: 60) is { Length: > 0 } name:
                await host.Services.Lists.RenameListAsync(_listId, name);
                await LoadAsync();
                break;
            case delete when await dialogs.ConfirmAsync(delete, $"Delete \"{Title}\" for everyone in the group? This can't be undone.", "Delete"):
                await host.Services.Lists.DeleteListAsync(_listId);
                await Nav.BackAsync();
                break;
        }
    }
}

/// <summary>One item: name, quantity, unit, notes, price; remove for good (monthly) or delete (to-do); move (to-do).</summary>
public sealed partial class ItemViewModel(VaultHost host, Dialogs dialogs) : ObservableObject, ILoads, IQueryAttributable
{
    private string _id = "";
    private bool _loaded;

    [ObservableProperty]
    public partial string Name { get; set; } = "";

    [ObservableProperty]
    public partial string Qty { get; set; } = "1";

    [ObservableProperty]
    public partial string Unit { get; set; } = "";

    [ObservableProperty]
    public partial string Notes { get; set; } = "";

    [ObservableProperty]
    public partial string Price { get; set; } = "";

    [ObservableProperty]
    public partial string PriceLabel { get; set; } = "Amount spent";

    [ObservableProperty]
    public partial bool IsMonthly { get; set; }

    [ObservableProperty]
    public partial string DeleteText { get; set; } = "Delete";

    [ObservableProperty]
    public partial string Source { get; set; } = "";

    public void ApplyQueryAttributes(IDictionary<string, object> query) =>
        _id = query.TryGetValue("id", out var id) ? Uri.UnescapeDataString(id.ToString()!) : "";

    public async Task LoadAsync()
    {
        // Loaded once: a sync from another phone must not overwrite what's being typed.
        if (_loaded)
        {
            return;
        }

        var s = host.Services;
        if (await s.Lists.ItemAsync(_id) is not { } item || await s.Lists.GetListAsync(item.ListId) is not { } list)
        {
            await Nav.BackAsync();
            return;
        }

        _loaded = true;

        var currency = (await s.Families.GetAsync(list.FamilyId))?.Currency ?? "INR";
        Name = item.Name;
        Qty = item.Qty.ToString("0.###", CultureInfo.CurrentCulture);
        Unit = item.Unit ?? "";
        Notes = item.Notes ?? "";
        Price = item.Price?.ToString("0.##", CultureInfo.CurrentCulture) ?? "";
        IsMonthly = list.Kind == ListKind.Monthly;
        PriceLabel = $"Amount spent this month ({currency})";
        DeleteText = IsMonthly ? "Remove for good" : "Delete";
        Source = item.Source switch
        {
            ItemSource.Bill => "Added from a bill.",
            ItemSource.Carried when item.SkippedLast => "Copied from last month, where it wasn't needed.",
            ItemSource.Carried => "Copied from last month.",
            _ => "",
        };
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            await dialogs.AlertAsync("Item", "Please type a name.");
            return;
        }

        var qty = decimal.TryParse(Qty, NumberStyles.Number, CultureInfo.CurrentCulture, out var q) && q > 0 ? q : 1;
        var price = Money.Parse(Price);
        try
        {
            await host.Services.Lists.UpdateItemAsync(_id, i =>
            {
                i.Name = Name;
                i.Qty = qty;
                i.Unit = Unit;
                i.Notes = Notes;
                i.Price = price;
            });
            await Nav.BackAsync();
        }
        catch (ListRuleException ex)
        {
            await dialogs.AlertAsync("Item", ex.Message);
        }
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        var message = IsMonthly ? $"Remove \"{Name}\" for good? It won't be copied to next month." : $"Delete \"{Name}\"?";
        if (await dialogs.ConfirmAsync(DeleteText, message, DeleteText))
        {
            await host.Services.Lists.DeleteItemAsync(_id);
            await Nav.BackAsync();
        }
    }

    [RelayCommand]
    private Task MoveUpAsync() => host.Services.Lists.MoveItemAsync(_id, -1);

    [RelayCommand]
    private Task MoveDownAsync() => host.Services.Lists.MoveItemAsync(_id, 1);
}
