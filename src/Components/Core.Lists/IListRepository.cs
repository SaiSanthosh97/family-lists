namespace FamilyLists.Core.Lists;

/// <summary>Storage for lists, months and items (implemented in Storage.Sqlite). Reads include deleted records only where noted.</summary>
public interface IListRepository
{
    Task<ShoppingList?> GetListAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>Lists of a group, not deleted.</summary>
    Task<IReadOnlyList<ShoppingList>> ListsAsync(string familyId, CancellationToken cancellationToken = default);

    Task SaveListAsync(ShoppingList list, CancellationToken cancellationToken = default);

    /// <summary>Includes deleted months (sync needs to compare versions).</summary>
    Task<MonthList?> GetMonthAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>Months of a list, not deleted, oldest first.</summary>
    Task<IReadOnlyList<MonthList>> MonthsAsync(string listId, CancellationToken cancellationToken = default);

    Task SaveMonthAsync(MonthList month, CancellationToken cancellationToken = default);

    /// <summary>Includes deleted items.</summary>
    Task<ListItem?> GetItemAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>Items of a month (or of a to-do list when <paramref name="monthId"/> is null), not deleted, in order.</summary>
    Task<IReadOnlyList<ListItem>> ItemsAsync(string listId, string? monthId, CancellationToken cancellationToken = default);

    /// <summary>Every item of a list across months (for spending history), not deleted.</summary>
    Task<IReadOnlyList<ListItem>> AllItemsAsync(string listId, CancellationToken cancellationToken = default);

    Task SaveItemAsync(ListItem item, CancellationToken cancellationToken = default);

    /// <summary>Several items in one transaction (starting a month).</summary>
    Task SaveItemsAsync(IReadOnlyList<ListItem> items, CancellationToken cancellationToken = default);
}
