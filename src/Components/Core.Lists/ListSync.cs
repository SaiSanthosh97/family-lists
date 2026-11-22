using System.Text.Json;
using FamilyLists.Core.Common;
using FamilyLists.Core.Vault;

namespace FamilyLists.Core.Lists;

/// <summary>Sync handlers for lists, months and items: last writer wins, and nothing may point into another group.</summary>
public static class ListSync
{
    public static IEnumerable<ISyncedRecords> Handlers(IListRepository repo) =>
        [new Lists(repo), new Months(repo), new Items(repo)];

    internal static T? Read<T>(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonDefaults.Options);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    public static string Write<T>(T value) => JsonSerializer.Serialize(value, JsonDefaults.Options);

    private sealed class Lists(IListRepository repo) : ISyncedRecords
    {
        public string Type => RecordTypes.List;

        public async Task<SyncedRecord?> LoadAsync(string id, CancellationToken ct = default) =>
            await repo.GetListAsync(id, ct) is { } list ? new SyncedRecord(list.FamilyId, Write(list), list.Version) : null;

        public async Task<bool> ApplyAsync(string familyId, string id, string payload, CancellationToken ct = default)
        {
            if (Read<ShoppingList>(payload) is not { } incoming || incoming.Id != id || incoming.FamilyId != familyId)
            {
                return false;
            }

            var local = await repo.GetListAsync(id, ct);
            if (local is not null && (local.FamilyId != familyId || !Versioning.Wins(incoming, local) || local.Kind != incoming.Kind))
            {
                return false;
            }

            await repo.SaveListAsync(incoming, ct);
            return true;
        }
    }

    private sealed class Months(IListRepository repo) : ISyncedRecords
    {
        public string Type => RecordTypes.Month;

        public async Task<SyncedRecord?> LoadAsync(string id, CancellationToken ct = default) =>
            await repo.GetMonthAsync(id, ct) is { } month ? new SyncedRecord(month.FamilyId, Write(month), month.Version) : null;

        public async Task<bool> ApplyAsync(string familyId, string id, string payload, CancellationToken ct = default)
        {
            if (Read<MonthList>(payload) is not { } incoming || incoming.Id != id || incoming.FamilyId != familyId
                || !Period.TryParse(incoming.Period, out _) || incoming.Id != ListService.MonthId(incoming.ListId, incoming.Period)
                || !await ListInGroupAsync(repo, incoming.ListId, familyId, ct))
            {
                return false;
            }

            var local = await repo.GetMonthAsync(id, ct);
            if (local is not null && (local.FamilyId != familyId || !Versioning.Wins(incoming, local)))
            {
                return false;
            }

            await repo.SaveMonthAsync(incoming, ct);
            return true;
        }
    }

    private sealed class Items(IListRepository repo) : ISyncedRecords
    {
        public string Type => RecordTypes.Item;

        public async Task<SyncedRecord?> LoadAsync(string id, CancellationToken ct = default) =>
            await repo.GetItemAsync(id, ct) is { } item ? new SyncedRecord(item.FamilyId, Write(item), item.Version) : null;

        public async Task<bool> ApplyAsync(string familyId, string id, string payload, CancellationToken ct = default)
        {
            if (Read<ListItem>(payload) is not { } incoming || incoming.Id != id || incoming.FamilyId != familyId
                || string.IsNullOrWhiteSpace(incoming.Name) || incoming.Name.Length > 200 || !Expenses.IsValidDueDay(incoming.DueDay)
                || !await ListInGroupAsync(repo, incoming.ListId, familyId, ct))
            {
                return false;
            }

            if (incoming.MonthId is not null && await repo.GetMonthAsync(incoming.MonthId, ct) is { } month
                && (month.FamilyId != familyId || month.ListId != incoming.ListId))
            {
                return false;
            }

            var local = await repo.GetItemAsync(id, ct);
            if (local is not null && (local.FamilyId != familyId || local.ListId != incoming.ListId || !Versioning.Wins(incoming, local)))
            {
                return false;
            }

            await repo.SaveItemAsync(incoming, ct);
            return true;
        }
    }

    /// <summary>
    /// True unless the list is known to belong to another group. An unknown list is allowed: records can arrive
    /// before their list (batches are split across messages), and they only show up once the list arrives.
    /// </summary>
    internal static async Task<bool> ListInGroupAsync(IListRepository repo, string listId, string familyId, CancellationToken ct) =>
        await repo.GetListAsync(listId, ct) is not { } list || list.FamilyId == familyId;
}
