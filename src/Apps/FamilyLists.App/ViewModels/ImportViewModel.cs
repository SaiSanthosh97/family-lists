using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FamilyLists.App.Services;
using FamilyLists.App.Views;
using FamilyLists.Core.Lists;

namespace FamilyLists.App.ViewModels;

/// <summary>One line of the note: ticked lines are added.</summary>
public sealed partial class ImportRow(NoteLine line, string detail) : ObservableObject
{
    public NoteLine Line { get; } = line;

    public string Name => Line.Name;

    public string Qty => Display.Qty(Line.Qty, null);

    [ObservableProperty]
    public partial bool Picked { get; set; }

    [ObservableProperty]
    public partial string Detail { get; set; } = detail;
}

/// <summary>Where the items go: an existing list (a monthly one adds to its open month), or a new list.</summary>
public sealed record ImportTarget(string Label, ShoppingList? List, ListKind NewKind);

/// <summary>
/// Import a note from Google Keep, Samsung Notes or any notes app: shared into the app (Share → Family Lists) or
/// pasted. Pick the list, untick what you don't want, add.
/// </summary>
public sealed partial class ImportViewModel(VaultHost host, AppSettings settings, Dialogs dialogs) : ObservableObject, ILoads
{
    private bool _loaded;
    private int _refreshes;
    private ParsedNote _note = new(null, []);

    [ObservableProperty]
    public partial string NoteText { get; set; } = "";

    [ObservableProperty]
    public partial bool HasLines { get; set; }

    [ObservableProperty]
    public partial string AddText { get; set; } = "Add";

    [ObservableProperty]
    public partial bool CanAdd { get; set; }

    [ObservableProperty]
    public partial int TargetIndex { get; set; } = -1;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    public ObservableCollection<ImportRow> Rows { get; } = [];

    public ObservableCollection<ImportTarget> Targets { get; } = [];

    public async Task LoadAsync()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        if (settings.CurrentGroupId is not { } groupId)
        {
            await dialogs.AlertAsync("Import a note", "Start or join a group first. Then share the note again.");
            await Nav.BackAsync();
            return;
        }

        Targets.Clear();
        var lists = await host.Services.Lists.ListsAsync(groupId);
        // Expense lists are left out: a note's lines are things to buy or do, not amounts.
        foreach (var list in lists.Where(l => l.Kind != ListKind.Expenses).OrderBy(l => l.Kind))
        {
            Targets.Add(new ImportTarget(list.Kind == ListKind.Monthly ? $"{list.Name} (monthly)" : list.Name, list, list.Kind));
        }

        Targets.Add(new ImportTarget("New to-do list", null, ListKind.Todo));
        Targets.Add(new ImportTarget("New monthly list", null, ListKind.Monthly));

        TakeShared();
    }

    /// <summary>Uses a note shared from another app, if one is waiting.</summary>
    public void TakeShared()
    {
        if (Inbox.TakeNote() is { } shared)
        {
            NoteText = shared; // parses, see OnNoteTextChanged
        }
    }

    partial void OnNoteTextChanged(string value)
    {
        _note = NoteImport.Parse(value);
        if (TargetIndex < 0 || Targets[TargetIndex].List is null)
        {
            TargetIndex = DefaultTarget();
        }

        _ = RefreshRowsAsync();
    }

    partial void OnTargetIndexChanged(int value) => _ = RefreshRowsAsync();

    /// <summary>The list named like the note ("Groceries"), else the first monthly list, else a new to-do list.</summary>
    private int DefaultTarget()
    {
        if (Targets.Count == 0)
        {
            return -1;
        }

        if (_note.Title is { } title)
        {
            var named = Targets.Select((t, i) => (t, i)).FirstOrDefault(x => x.t.List is { } l && ProductNames.Similarity(l.Name, title) >= 0.6);
            if (named.t is not null)
            {
                return named.i;
            }
        }

        var monthly = Targets.Select((t, i) => (t, i)).FirstOrDefault(x => x.t.List?.Kind == ListKind.Monthly);
        return monthly.t is not null ? monthly.i : Targets.Count - 2;
    }

    /// <summary>Lines already on the chosen list, or ticked in the note, start unticked.</summary>
    private async Task RefreshRowsAsync()
    {
        var version = ++_refreshes; // typing re-reads the note: only the latest refresh fills the rows
        var existing = new HashSet<string>(StringComparer.Ordinal);
        if (TargetIndex >= 0 && TargetIndex < Targets.Count && Targets[TargetIndex].List is { } list)
        {
            var month = list.Kind == ListKind.Monthly ? await TargetMonthAsync(list, create: false) : null;
            if (list.Kind == ListKind.Todo || month is not null)
            {
                foreach (var item in await host.Services.Lists.ItemsAsync(list.Id, month?.Id))
                {
                    existing.Add(ProductNames.Normalize(item.Name));
                }
            }
        }

        if (version != _refreshes)
        {
            return;
        }

        Rows.Clear();
        foreach (var line in _note.Lines)
        {
            var already = existing.Contains(ProductNames.Normalize(line.Name));
            var row = new ImportRow(line, already ? "already on the list" : line.Done ? "ticked in the note" : "")
            {
                Picked = !already && !line.Done,
            };
            row.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ImportRow.Picked))
                {
                    Count();
                }
            };
            Rows.Add(row);
        }

        HasLines = Rows.Count > 0;
        Count();
    }

    private void Count()
    {
        var picked = Rows.Count(r => r.Picked);
        CanAdd = picked > 0 && TargetIndex >= 0;
        AddText = picked == 1 ? "Add 1 item" : $"Add {picked} items";
    }

    /// <summary>
    /// A monthly list's month to add to: the newest one unless it's already shopped (or there is none, or it's
    /// from an earlier month); then this month, or the one after the newest.
    /// </summary>
    private async Task<MonthList?> TargetMonthAsync(ShoppingList list, bool create)
    {
        var s = host.Services;
        var now = Period.Of(s.Session.Today);
        var latest = (await s.Lists.MonthsAsync(list.Id)).LastOrDefault();
        if (latest is not null && latest.Status != MonthStatus.Shopped && string.CompareOrdinal(latest.Period, now) >= 0)
        {
            return latest;
        }

        if (!create)
        {
            return null;
        }

        var period = latest is null || string.CompareOrdinal(latest.Period, now) < 0 ? now : Period.Shift(latest.Period, 1);
        return await s.Lists.StartMonthAsync(list.Id, period);
    }

    [RelayCommand]
    private async Task PasteAsync()
    {
        if (await Clipboard.Default.GetTextAsync() is { Length: > 0 } text)
        {
            NoteText = text;
        }
        else
        {
            await dialogs.AlertAsync("Paste", "Nothing copied yet. In your notes app, select the text and tap Copy, then come back.");
        }
    }

    [RelayCommand]
    private async Task AddAsync()
    {
        if (TargetIndex < 0 || settings.CurrentGroupId is not { } groupId || IsBusy)
        {
            return;
        }

        var picked = Rows.Where(r => r.Picked).Select(r => r.Line).ToList();
        if (picked.Count == 0)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var s = host.Services;
            var target = Targets[TargetIndex];
            var list = target.List;
            if (list is null)
            {
                var name = await dialogs.PromptAsync(target.Label, "Name for the new list", _note.Title ?? (target.NewKind == ListKind.Monthly ? "Groceries" : "To do"), maxLength: 60);
                if (string.IsNullOrWhiteSpace(name))
                {
                    return;
                }

                list = await s.Lists.CreateListAsync(groupId, name.Trim(), target.NewKind);
            }

            var month = list.Kind == ListKind.Monthly ? await TargetMonthAsync(list, create: true) : null;
            foreach (var line in picked)
            {
                await s.Lists.AddItemAsync(list.Id, month?.Id, line.Name, line.Qty);
            }

            Dialogs.Toast(picked.Count == 1 ? $"Added 1 item to {list.Name}." : $"Added {picked.Count} items to {list.Name}.");
            await Nav.BackAsync();
            await (list.Kind == ListKind.Monthly ? Nav.MonthAsync(list.Id, month?.Period) : Nav.TodoAsync(list.Id));
        }
        catch (ListRuleException ex)
        {
            await dialogs.AlertAsync("Import a note", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
