using FamilyLists.Core.Common;
using FamilyLists.Core.Vault;

namespace FamilyLists.Core.Lists;

/// <summary>
/// The list use cases. Every change is saved locally first, stamped with a new version, and announced as a
/// <see cref="VaultChange"/> (sync signs and queues it; screens refresh).
/// </summary>
public sealed class ListService(VaultSession session, IListRepository repo, VaultEvents events)
{
    public const int MaxNameLength = 120;

    // ------------------------------------------------------------------ lists

    public Task<IReadOnlyList<ShoppingList>> ListsAsync(string familyId, CancellationToken ct = default) => repo.ListsAsync(familyId, ct);

    public async Task<ShoppingList?> GetListAsync(string listId, CancellationToken ct = default) =>
        await repo.GetListAsync(listId, ct) is { IsDeleted: false } list ? list : null;

    public async Task<ShoppingList> CreateListAsync(string familyId, string name, ListKind kind, CancellationToken ct = default)
    {
        var list = new ShoppingList { Id = Ids.New(), FamilyId = familyId, Name = CleanName(name, "list"), Kind = kind, CreatedAt = session.Now };
        await SaveAsync(list, RecordTypes.List, repo.SaveListAsync, ct);
        return list;
    }

    public async Task RenameListAsync(string listId, string name, CancellationToken ct = default)
    {
        var list = await RequireListAsync(listId, ct);
        list.Name = CleanName(name, "list");
        await SaveAsync(list, RecordTypes.List, repo.SaveListAsync, ct);
    }

    /// <summary>Deletes a list for the whole group (its months and items are hidden with it).</summary>
    public async Task DeleteListAsync(string listId, CancellationToken ct = default)
    {
        var list = await RequireListAsync(listId, ct);
        list.IsDeleted = true;
        await SaveAsync(list, RecordTypes.List, repo.SaveListAsync, ct);
    }

    // ------------------------------------------------------------------ months

    public Task<IReadOnlyList<MonthList>> MonthsAsync(string listId, CancellationToken ct = default) => repo.MonthsAsync(listId, ct);

    public async Task<MonthList?> MonthAsync(string listId, string period, CancellationToken ct = default) =>
        await repo.GetMonthAsync(MonthId(listId, period), ct) is { IsDeleted: false } month ? month : null;

    /// <summary>The most recent month before <paramref name="period"/> (what "Start {month}" copies from), or null.</summary>
    public async Task<MonthList?> PreviousMonthAsync(string listId, string period, CancellationToken ct = default) =>
        (await repo.MonthsAsync(listId, ct)).Where(m => string.CompareOrdinal(m.Period, period) < 0).MaxBy(m => m.Period);

    public static string MonthId(string listId, string period) => Ids.Derive("month", listId, period);

    /// <summary>
    /// Starts a month: copies every item from the most recent earlier month (see <see cref="Rollover"/>) into a new
    /// draft. Idempotent, and two phones doing it at once produce the same month and items (derived ids, seed
    /// versions), which sync then merges into one.
    /// </summary>
    public async Task<MonthList> StartMonthAsync(string listId, string period, CancellationToken ct = default)
    {
        var list = await RequireListAsync(listId, ct);
        if (list.Kind != ListKind.Monthly)
        {
            throw new ListRuleException("Only monthly lists have months.");
        }

        if (!Period.TryParse(period, out _))
        {
            throw new ListRuleException("Not a month.");
        }

        if (await MonthAsync(listId, period, ct) is { } existing)
        {
            return existing;
        }

        var month = new MonthList
        {
            Id = MonthId(listId, period),
            FamilyId = list.FamilyId,
            ListId = listId,
            Period = period,
            Status = MonthStatus.Draft,
            Version = Seeds.Version,
            UpdatedBy = session.MemberId,
            UpdatedAt = session.Now,
        };

        var items = new List<ListItem>();
        if (await PreviousMonthAsync(listId, period, ct) is { } previous)
        {
            foreach (var carried in Rollover.BuildCarryOver(await repo.ItemsAsync(listId, previous.Id, ct)))
            {
                items.Add(new ListItem
                {
                    Id = Ids.Derive("item", month.Id, carried.Key),
                    FamilyId = list.FamilyId,
                    ListId = listId,
                    MonthId = month.Id,
                    Name = carried.Name,
                    Qty = carried.Qty,
                    Unit = carried.Unit,
                    Notes = carried.Notes,
                    Source = ItemSource.Carried,
                    SkippedLast = carried.SkippedLast,
                    Position = carried.Position,
                    CreatedAt = session.Now,
                    Version = Seeds.Version,
                    UpdatedBy = session.MemberId,
                    UpdatedAt = session.Now,
                });
            }
        }

        await repo.SaveMonthAsync(month, ct);
        await repo.SaveItemsAsync(items, ct);
        await events.PublishAsync(new VaultChange(month.FamilyId, RecordTypes.Month, month.Id), ct);
        foreach (var item in items)
        {
            await events.PublishAsync(new VaultChange(item.FamilyId, RecordTypes.Item, item.Id), ct);
        }

        return month;
    }

    public async Task SetStatusAsync(string monthId, MonthStatus status, CancellationToken ct = default)
    {
        var month = await RequireMonthAsync(monthId, ct);
        if (month.Status == status)
        {
            return;
        }

        month.Status = status;
        await SaveAsync(month, RecordTypes.Month, repo.SaveMonthAsync, ct);
    }

    public async Task SetManualTotalAsync(string monthId, decimal? total, CancellationToken ct = default)
    {
        if (total is < 0)
        {
            throw new ListRuleException("The total can't be negative.");
        }

        var month = await RequireMonthAsync(monthId, ct);
        month.ManualTotal = total is null ? null : decimal.Round(total.Value, 2);
        await SaveAsync(month, RecordTypes.Month, repo.SaveMonthAsync, ct);
    }

    // ------------------------------------------------------------------ items

    public Task<IReadOnlyList<ListItem>> ItemsAsync(string listId, string? monthId, CancellationToken ct = default) => repo.ItemsAsync(listId, monthId, ct);

    public async Task<ListItem?> ItemAsync(string itemId, CancellationToken ct = default) =>
        await repo.GetItemAsync(itemId, ct) is { IsDeleted: false } item ? item : null;

    public async Task<ListItem> AddItemAsync(string listId, string? monthId, string name, decimal qty = 1, string? unit = null, ItemSource source = ItemSource.Added, CancellationToken ct = default)
    {
        var list = await RequireListAsync(listId, ct);
        if ((list.Kind == ListKind.Monthly) != (monthId is not null))
        {
            throw new ListRuleException(list.Kind == ListKind.Monthly ? "Choose a month first." : "To-do lists have no months.");
        }

        if (monthId is not null && (await RequireMonthAsync(monthId, ct)).ListId != listId)
        {
            throw new ListRuleException("That month belongs to another list.");
        }

        var existing = await repo.ItemsAsync(listId, monthId, ct);
        var item = new ListItem
        {
            Id = Ids.New(),
            FamilyId = list.FamilyId,
            ListId = listId,
            MonthId = monthId,
            Name = CleanName(name, "item"),
            Qty = qty > 0 ? qty : 1,
            Unit = string.IsNullOrWhiteSpace(unit) ? null : unit.Trim(),
            Source = source,
            Position = existing.Count == 0 ? 1 : existing.Max(i => i.Position) + 1,
            CreatedAt = session.Now,
        };
        await SaveAsync(item, RecordTypes.Item, repo.SaveItemAsync, ct);
        return item;
    }

    /// <summary>Saves edits made on the item screen (name, quantity, unit, notes, price).</summary>
    public async Task UpdateItemAsync(string itemId, Action<ListItem> edit, CancellationToken ct = default)
    {
        var item = await RequireItemAsync(itemId, ct);
        edit(item);
        item.Name = CleanName(item.Name, "item");
        item.Qty = item.Qty > 0 ? item.Qty : 1;
        item.Unit = string.IsNullOrWhiteSpace(item.Unit) ? null : item.Unit.Trim();
        item.Notes = string.IsNullOrWhiteSpace(item.Notes) ? null : item.Notes.Trim();
        if (item.Price is < 0)
        {
            throw new ListRuleException("The price can't be negative.");
        }

        await SaveAsync(item, RecordTypes.Item, repo.SaveItemAsync, ct);
    }

    /// <summary>Reviewing: ticked = needed this month, unticked = not needed.</summary>
    public Task SetIncludedAsync(string itemId, bool included, CancellationToken ct = default) =>
        UpdateItemAsync(itemId, i => i.Included = included, ct);

    /// <summary>Shopping: in the cart. To-dos: done.</summary>
    public Task SetBoughtAsync(string itemId, bool bought, CancellationToken ct = default) =>
        UpdateItemAsync(itemId, i => i.Bought = bought, ct);

    /// <summary>"Remove for good": the item is deleted and won't be copied to next month.</summary>
    public async Task DeleteItemAsync(string itemId, CancellationToken ct = default)
    {
        var item = await RequireItemAsync(itemId, ct);
        item.IsDeleted = true;
        await SaveAsync(item, RecordTypes.Item, repo.SaveItemAsync, ct);
    }

    /// <summary>Moves an item one place up or down (to-do lists).</summary>
    public async Task MoveItemAsync(string itemId, int direction, CancellationToken ct = default)
    {
        var item = await RequireItemAsync(itemId, ct);
        var siblings = (await repo.ItemsAsync(item.ListId, item.MonthId, ct)).Where(i => i.Bought == item.Bought).ToList();
        var index = siblings.FindIndex(i => i.Id == itemId);
        var target = index + Math.Sign(direction);
        if (index < 0 || target < 0 || target >= siblings.Count)
        {
            return;
        }

        // Renumber the whole list so equal positions (two phones adding at once) can't block a move.
        var reordered = siblings.ToList();
        (reordered[index], reordered[target]) = (reordered[target], reordered[index]);
        for (var i = 0; i < reordered.Count; i++)
        {
            if (reordered[i].Position != i + 1)
            {
                reordered[i].Position = i + 1;
                await SaveAsync(reordered[i], RecordTypes.Item, repo.SaveItemAsync, ct);
            }
        }
    }

    /// <summary>Deletes every done item of a to-do list.</summary>
    public async Task<int> ClearDoneAsync(string listId, CancellationToken ct = default)
    {
        var done = (await repo.ItemsAsync(listId, null, ct)).Where(i => i.Bought).ToList();
        foreach (var item in done)
        {
            item.IsDeleted = true;
            await SaveAsync(item, RecordTypes.Item, repo.SaveItemAsync, ct);
        }

        return done.Count;
    }

    // ------------------------------------------------------------------ helpers

    private async Task SaveAsync<T>(T record, string type, Func<T, CancellationToken, Task> save, CancellationToken ct)
        where T : IVersioned
    {
        session.Stamp(record);
        await save(record, ct);
        await events.PublishAsync(new VaultChange(record.FamilyId, type, record.Id), ct);
    }

    private async Task<ShoppingList> RequireListAsync(string id, CancellationToken ct) =>
        await GetListAsync(id, ct) ?? throw new ListRuleException("That list no longer exists.");

    private async Task<MonthList> RequireMonthAsync(string id, CancellationToken ct) =>
        await repo.GetMonthAsync(id, ct) is { IsDeleted: false } month ? month : throw new ListRuleException("That month no longer exists.");

    private async Task<ListItem> RequireItemAsync(string id, CancellationToken ct) =>
        await ItemAsync(id, ct) ?? throw new ListRuleException("That item no longer exists.");

    private static string CleanName(string name, string what)
    {
        var trimmed = (name ?? "").Trim();
        if (trimmed.Length == 0)
        {
            throw new ListRuleException($"Please type a name for the {what}.");
        }

        return trimmed.Length > MaxNameLength ? trimmed[..MaxNameLength] : trimmed;
    }
}
