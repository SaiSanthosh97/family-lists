using FamilyLists.Core.Lists;
using static FamilyLists.Storage.Sqlite.SqlHelpers;

namespace FamilyLists.Storage.Sqlite;

public sealed class SqliteListRepository(VaultDatabase db) : IListRepository
{
    public Task<ShoppingList?> GetListAsync(string id, CancellationToken cancellationToken = default) =>
        db.UseAsync(c => c.Query("SELECT json FROM lists WHERE id = $id", r => FromJson<ShoppingList>(r.GetString(0)), ("$id", id)).FirstOrDefault(), cancellationToken);

    public Task<IReadOnlyList<ShoppingList>> ListsAsync(string familyId, CancellationToken cancellationToken = default) =>
        db.UseAsync<IReadOnlyList<ShoppingList>>(
            c => c.Query("SELECT json FROM lists WHERE family_id = $f AND is_deleted = 0", r => FromJson<ShoppingList>(r.GetString(0)), ("$f", familyId))
                .OrderBy(l => l.Kind).ThenBy(l => l.Name, StringComparer.CurrentCultureIgnoreCase).ToList(),
            cancellationToken);

    public Task SaveListAsync(ShoppingList l, CancellationToken cancellationToken = default) =>
        db.UseAsync(c => c.Run(
            """
            INSERT INTO lists (id, family_id, version, is_deleted, json) VALUES ($id, $f, $v, $del, $json)
            ON CONFLICT (id) DO UPDATE SET version = $v, is_deleted = $del, json = $json
            """,
            null,
            ("$id", l.Id), ("$f", l.FamilyId), ("$v", l.Version.ToString()), ("$del", Bool(l.IsDeleted)), ("$json", ToJson(l))), cancellationToken);

    public Task<MonthList?> GetMonthAsync(string id, CancellationToken cancellationToken = default) =>
        db.UseAsync(c => c.Query("SELECT json FROM months WHERE id = $id", r => FromJson<MonthList>(r.GetString(0)), ("$id", id)).FirstOrDefault(), cancellationToken);

    public Task<IReadOnlyList<MonthList>> MonthsAsync(string listId, CancellationToken cancellationToken = default) =>
        db.UseAsync<IReadOnlyList<MonthList>>(
            // Only months of the list's own group (a record can't point into another group's list).
            c => c.Query(
                "SELECT json FROM months WHERE list_id = $l AND is_deleted = 0 AND family_id = (SELECT family_id FROM lists WHERE id = $l) ORDER BY period",
                r => FromJson<MonthList>(r.GetString(0)),
                ("$l", listId)),
            cancellationToken);

    public Task SaveMonthAsync(MonthList m, CancellationToken cancellationToken = default) =>
        db.UseAsync(c => c.Run(
            """
            INSERT INTO months (id, family_id, list_id, period, version, is_deleted, json) VALUES ($id, $f, $l, $p, $v, $del, $json)
            ON CONFLICT (id) DO UPDATE SET version = $v, is_deleted = $del, json = $json
            """,
            null,
            ("$id", m.Id), ("$f", m.FamilyId), ("$l", m.ListId), ("$p", m.Period), ("$v", m.Version.ToString()), ("$del", Bool(m.IsDeleted)), ("$json", ToJson(m))), cancellationToken);

    public Task<ListItem?> GetItemAsync(string id, CancellationToken cancellationToken = default) =>
        db.UseAsync(c => c.Query("SELECT json FROM items WHERE id = $id", r => FromJson<ListItem>(r.GetString(0)), ("$id", id)).FirstOrDefault(), cancellationToken);

    public Task<IReadOnlyList<ListItem>> ItemsAsync(string listId, string? monthId, CancellationToken cancellationToken = default) =>
        db.UseAsync<IReadOnlyList<ListItem>>(
            c => c.Query(
                $"""
                SELECT json FROM items
                WHERE list_id = $l AND {(monthId is null ? "month_id IS NULL" : "month_id = $m")} AND is_deleted = 0
                  AND family_id = (SELECT family_id FROM lists WHERE id = $l)
                ORDER BY position, created_at, id
                """,
                r => FromJson<ListItem>(r.GetString(0)),
                ("$l", listId), ("$m", monthId)),
            cancellationToken);

    public Task<IReadOnlyList<ListItem>> AllItemsAsync(string listId, CancellationToken cancellationToken = default) =>
        db.UseAsync<IReadOnlyList<ListItem>>(
            c => c.Query(
                "SELECT json FROM items WHERE list_id = $l AND is_deleted = 0 AND family_id = (SELECT family_id FROM lists WHERE id = $l) ORDER BY position, created_at, id",
                r => FromJson<ListItem>(r.GetString(0)),
                ("$l", listId)),
            cancellationToken);

    public Task SaveItemAsync(ListItem item, CancellationToken cancellationToken = default) =>
        db.UseAsync(c => SaveItem(c, null, item), cancellationToken);

    public Task SaveItemsAsync(IReadOnlyList<ListItem> items, CancellationToken cancellationToken = default) =>
        db.InTransactionAsync((c, t) =>
        {
            foreach (var item in items)
            {
                SaveItem(c, t, item);
            }
        }, cancellationToken);

    private static void SaveItem(Microsoft.Data.Sqlite.SqliteConnection c, Microsoft.Data.Sqlite.SqliteTransaction? t, ListItem i) =>
        c.Run(
            """
            INSERT INTO items (id, family_id, list_id, month_id, position, created_at, version, is_deleted, json)
            VALUES ($id, $f, $l, $m, $pos, $at, $v, $del, $json)
            ON CONFLICT (id) DO UPDATE SET position = $pos, version = $v, is_deleted = $del, json = $json
            """,
            t,
            ("$id", i.Id), ("$f", i.FamilyId), ("$l", i.ListId), ("$m", i.MonthId), ("$pos", i.Position), ("$at", Utc(i.CreatedAt)),
            ("$v", i.Version.ToString()), ("$del", Bool(i.IsDeleted)), ("$json", ToJson(i)));
}
