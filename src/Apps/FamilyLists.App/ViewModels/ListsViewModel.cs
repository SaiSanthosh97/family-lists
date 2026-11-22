using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FamilyLists.App.Services;
using FamilyLists.App.Views;
using FamilyLists.Core.Families;
using FamilyLists.Core.Lists;
using FamilyLists.Core.Sync;

namespace FamilyLists.App.ViewModels;

public sealed record ListRow(string Id, string Name, ListKind Kind, string Status, Color StatusColor);

/// <summary>The Lists tab: the open group's monthly lists (with this month's status) and to-do lists; switch group.</summary>
public sealed partial class ListsViewModel(VaultHost host, AppSettings settings, SyncScheduler scheduler, Dialogs dialogs) : ObservableObject, ILoads
{
    private IReadOnlyList<Family> _groups = [];

    [ObservableProperty]
    public partial bool HasGroup { get; set; }

    [ObservableProperty]
    public partial string GroupName { get; set; } = "";

    [ObservableProperty]
    public partial bool CanSwitch { get; set; }

    [ObservableProperty]
    public partial bool Waiting { get; set; }

    [ObservableProperty]
    public partial int JoinRequests { get; set; }

    [ObservableProperty]
    public partial bool NoMonthly { get; set; }

    [ObservableProperty]
    public partial bool NoTodos { get; set; }

    [ObservableProperty]
    public partial bool NoExpenses { get; set; }

    [ObservableProperty]
    public partial bool IsRefreshing { get; set; }

    public ObservableCollection<ListRow> Monthly { get; } = [];

    public ObservableCollection<ListRow> Todos { get; } = [];

    public ObservableCollection<ListRow> ExpenseLists { get; } = [];

    public async Task LoadAsync()
    {
        var s = host.Services;
        _groups = (await s.Families.ListAsync()).ToList();
        var removed = new List<string>();
        foreach (var g in _groups)
        {
            if (await s.Sync.IsRemovedAsync(g.Id))
            {
                removed.Add(g.Id);
            }
        }

        var current = _groups.FirstOrDefault(g => g.Id == settings.CurrentGroupId) ?? _groups.FirstOrDefault();
        settings.CurrentGroupId = current?.Id;
        scheduler.WatchCurrentGroup();
        HasGroup = current is not null;
        CanSwitch = _groups.Count > 1;
        Monthly.Clear();
        Todos.Clear();
        ExpenseLists.Clear();
        if (current is null)
        {
            return;
        }

        GroupName = removed.Contains(current.Id) ? $"{current.Name} (you were removed)" : current.Name;
        Waiting = !FamilySync.Admitted(current);
        JoinRequests = current.IsAdmin(s.Session.MemberId) ? (await s.Sync.PendingJoinsAsync(current.Id)).Count : 0;

        var now = Period.Of(s.Session.Today);
        foreach (var list in await s.Lists.ListsAsync(current.Id))
        {
            if (list.Kind == ListKind.Monthly)
            {
                var (status, color) = await MonthStatusAsync(list.Id, now);
                Monthly.Add(new ListRow(list.Id, list.Name, list.Kind, status, color));
            }
            else if (list.Kind == ListKind.Expenses)
            {
                var (status, color) = await ExpenseStatusAsync(list.Id, current.Currency, now);
                ExpenseLists.Add(new ListRow(list.Id, list.Name, list.Kind, status, color));
            }
            else
            {
                var items = await s.Lists.ItemsAsync(list.Id, null);
                var open = items.Count(i => !i.Bought);
                var status = items.Count == 0 ? "Empty" : open == 0 ? "All done" : $"{open} to do";
                Todos.Add(new ListRow(list.Id, list.Name, list.Kind, status, Palette.Subtle));
            }
        }

        NoMonthly = Monthly.Count == 0;
        NoTodos = Todos.Count == 0;
        NoExpenses = ExpenseLists.Count == 0;

        // A note shared while the app was closed: open Import now that the data is ready.
        if (Inbox.HasNote && !Waiting)
        {
            await Nav.ImportAsync();
        }
    }

    [RelayCommand]
    private Task ImportAsync() => Nav.ImportAsync();

    private async Task<(string, Color)> MonthStatusAsync(string listId, string now)
    {
        var months = await host.Services.Lists.MonthsAsync(listId);
        var latest = months.LastOrDefault();
        if (latest is null || string.CompareOrdinal(latest.Period, now) < 0)
        {
            return ($"Start {Period.MonthName(now)}", Palette.Warning);
        }

        // Someone on another phone may already be in next month (time zones around the 1st).
        var prefix = string.CompareOrdinal(latest.Period, now) > 0 ? Period.MonthName(latest.Period) + ": " : "";
        return latest.Status switch
        {
            MonthStatus.Draft => (prefix + "Reviewing", Palette.Info),
            MonthStatus.Final => (prefix + "Ready to shop", Palette.Ok),
            _ => (prefix + "Shopped", Palette.Subtle),
        };
    }

    /// <summary>"Start December", "2 overdue", "₹3,200 left" or "All paid" for this month.</summary>
    private async Task<(string, Color)> ExpenseStatusAsync(string listId, string currency, string now)
    {
        var s = host.Services;
        if (await s.Lists.MonthAsync(listId, now) is not { } month)
        {
            return ($"Start {Period.MonthName(now)}", Palette.Warning);
        }

        var summary = Expenses.Summarize(await s.Lists.ItemsAsync(listId, month.Id), now, s.Session.Today);
        return summary switch
        {
            { Overdue: > 0 } => ($"{summary.Overdue} overdue", Palette.Danger),
            { Count: 0 } => ("Empty", Palette.Subtle),
            { Left: <= 0 } when summary.PaidCount == summary.Count => ("All paid", Palette.Ok),
            _ => ($"{Display.Money(summary.Left, currency)} left", Palette.Info),
        };
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        try
        {
            await scheduler.SyncNowAsync();
            await LoadAsync();
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    [RelayCommand]
    private Task OpenAsync(ListRow row) => row.Kind switch
    {
        ListKind.Monthly => Nav.MonthAsync(row.Id),
        ListKind.Expenses => Nav.ExpensesAsync(row.Id),
        _ => Nav.TodoAsync(row.Id),
    };

    [RelayCommand]
    private async Task SwitchGroupAsync()
    {
        var names = _groups.Select(g => g.Name).ToArray();
        var choice = await dialogs.ChooseAsync("Switch group", names);
        if (_groups.FirstOrDefault(g => g.Name == choice) is { } group)
        {
            settings.CurrentGroupId = group.Id;
            await LoadAsync();
        }
    }

    [RelayCommand]
    private Task NewMonthlyAsync() => NewListAsync(ListKind.Monthly, "New monthly list", "For groceries: each month starts as a copy of last month's list.", "Groceries");

    [RelayCommand]
    private Task NewTodoAsync() => NewListAsync(ListKind.Todo, "New to-do list", "For chores, errands, trip packing, anything.", "Weekend chores");

    [RelayCommand]
    private Task NewExpensesAsync() => NewListAsync(ListKind.Expenses, "New expenses list", "For rent, bills, fees and EMIs: each month starts with last month's expenses and amounts.", "Home expenses");

    private async Task NewListAsync(ListKind kind, string title, string message, string example)
    {
        if (settings.CurrentGroupId is not { } groupId)
        {
            return;
        }

        var first = kind switch
        {
            ListKind.Monthly => Monthly.Count == 0,
            ListKind.Expenses => ExpenseLists.Count == 0,
            _ => false,
        };
        var name = await dialogs.PromptAsync(title, message, first ? example : null, maxLength: 60);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var list = await host.Services.Lists.CreateListAsync(groupId, name, kind);
        await Nav.ListAsync(list);
    }

    [RelayCommand]
    private async Task CreateGroupAsync()
    {
        var name = await dialogs.PromptAsync("New group", "A group is shared with the people you live with, for example \"Home\".", "Home", maxLength: 60);
        if (!string.IsNullOrWhiteSpace(name))
        {
            var group = await host.Services.Sync.CreateFamilyAsync(name.Trim());
            settings.CurrentGroupId = group.Id;
            await LoadAsync();
        }
    }

    [RelayCommand]
    private Task JoinAsync() => Nav.GoAsync("join");

    [RelayCommand]
    private Task ReviewRequestsAsync() => Nav.GoAsync("//main/group");
}
