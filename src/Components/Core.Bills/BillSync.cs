using System.Text.Json;
using FamilyLists.Core.Common;
using FamilyLists.Core.Lists;
using FamilyLists.Core.Vault;

namespace FamilyLists.Core.Bills;

/// <summary>
/// Sync handlers for bills and bill lines. Drafts are never sent (they are being checked on one phone); a confirmed
/// bill is, and so is its deletion. Bill photos never leave the phone that took them.
/// </summary>
public static class BillSync
{
    public static IEnumerable<ISyncedRecords> Handlers(IBillRepository bills, IListRepository lists) =>
        [new Bills(bills, lists), new Lines(bills)];

    private static T? Read<T>(string json)
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

    private static string Write<T>(T value) => JsonSerializer.Serialize(value, JsonDefaults.Options);

    private sealed class Bills(IBillRepository bills, IListRepository lists) : ISyncedRecords
    {
        public string Type => RecordTypes.Bill;

        public async Task<SyncedRecord?> LoadAsync(string id, CancellationToken ct = default) =>
            await bills.GetBillAsync(id, ct) is { Status: BillStatus.Confirmed } bill ? new SyncedRecord(bill.FamilyId, Write(bill), bill.Version) : null;

        public async Task<bool> ApplyAsync(string familyId, string id, string payload, CancellationToken ct = default)
        {
            if (Read<Bill>(payload) is not { Status: BillStatus.Confirmed } incoming || incoming.Id != id || incoming.FamilyId != familyId)
            {
                return false;
            }

            if (await lists.GetListAsync(incoming.ListId, ct) is { } list && list.FamilyId != familyId)
            {
                return false;
            }

            if (await lists.GetMonthAsync(incoming.MonthId, ct) is { } month && (month.FamilyId != familyId || month.ListId != incoming.ListId))
            {
                return false;
            }

            var local = await bills.GetBillAsync(id, ct);
            if (local is not null && (local.FamilyId != familyId || !Versioning.Wins(incoming, local)))
            {
                return false;
            }

            await bills.SaveBillAsync(incoming, ct);
            return true;
        }
    }

    private sealed class Lines(IBillRepository bills) : ISyncedRecords
    {
        public string Type => RecordTypes.BillLine;

        public async Task<SyncedRecord?> LoadAsync(string id, CancellationToken ct = default) =>
            await bills.GetLineAsync(id, ct) is { } line && await bills.GetBillAsync(line.BillId, ct) is { Status: BillStatus.Confirmed }
                ? new SyncedRecord(line.FamilyId, Write(line), line.Version)
                : null;

        public async Task<bool> ApplyAsync(string familyId, string id, string payload, CancellationToken ct = default)
        {
            if (Read<BillLine>(payload) is not { } incoming || incoming.Id != id || incoming.FamilyId != familyId
                || string.IsNullOrWhiteSpace(incoming.Name) || incoming.Name.Length > 200)
            {
                return false;
            }

            if (await bills.GetBillAsync(incoming.BillId, ct) is { } bill && bill.FamilyId != familyId)
            {
                return false;
            }

            var local = await bills.GetLineAsync(id, ct);
            if (local is not null && (local.FamilyId != familyId || local.BillId != incoming.BillId || !Versioning.Wins(incoming, local)))
            {
                return false;
            }

            await bills.SaveLineAsync(incoming, ct);
            return true;
        }
    }
}
