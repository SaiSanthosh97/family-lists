using FamilyLists.Core.Common;
using FamilyLists.Core.Lists;
using FamilyLists.Core.Receipts;
using FamilyLists.Core.Security;
using FamilyLists.Core.Vault;

namespace FamilyLists.Core.Bills;

/// <summary>The bill header as checked on the review screen.</summary>
public sealed record BillEdit(string? StoreName, DateOnly? BillDate, decimal? Subtotal, decimal? Tax, decimal? Discount, decimal? Total);

/// <summary>One line as checked on the review screen.</summary>
public sealed record LineEdit(string Name, decimal? Qty, decimal? UnitPrice, decimal? LineTotal, LineAction Action, string? ItemId, string? RawText = null);

/// <summary>
/// Bills: read → check → confirm. A draft stays on the phone that added it; confirming sends it to the group and
/// (ported from the web version's confirm_bill):
/// <list type="bullet">
/// <item>"Add" lines become new list items (source Bill), so next month's list includes them;</item>
/// <item>every item gets price = the sum of the confirmed lines pointing at it, and is marked bought;</item>
/// <item>the month becomes Shopped; a bill can't be confirmed twice.</item>
/// </list>
/// </summary>
public sealed class BillService(
    VaultSession session,
    IBillRepository bills,
    IListRepository lists,
    ListService listService,
    VaultEvents events,
    FileVault photos,
    IFileKeyStore photoKeys)
{
    public Task<IReadOnlyList<Bill>> BillsOfMonthAsync(string monthId, CancellationToken ct = default) => bills.BillsOfMonthAsync(monthId, ct);

    public async Task<Bill?> BillAsync(string billId, CancellationToken ct = default) =>
        await bills.GetBillAsync(billId, ct) is { IsDeleted: false } bill ? bill : null;

    public Task<IReadOnlyList<BillLine>> LinesAsync(string billId, CancellationToken ct = default) => bills.LinesAsync(billId, ct);

    /// <summary>Saves what a reader understood as a draft, with each line matched to the list where it fits.</summary>
    public async Task<Bill> CreateDraftAsync(string monthId, ReceiptDraft draft, byte[]? photo = null, CancellationToken ct = default)
    {
        var month = await lists.GetMonthAsync(monthId, ct) is { IsDeleted: false } m ? m : throw new BillRuleException("That month no longer exists.");
        var items = await lists.ItemsAsync(month.ListId, month.Id, ct);
        var bill = new Bill
        {
            Id = Ids.New(),
            FamilyId = month.FamilyId,
            ListId = month.ListId,
            MonthId = month.Id,
            StoreName = Trim(draft.StoreName, 60),
            BillDate = draft.BillDate,
            Subtotal = draft.Subtotal,
            Tax = draft.Tax,
            Discount = draft.Discount,
            Total = draft.Total,
            Status = BillStatus.Draft,
            ReadBy = draft.ReadBy,
            CreatedAt = session.Now,
        };

        if (photo is { Length: > 0 })
        {
            var photoId = Ids.New();
            var key = await photos.WriteAsync(bill.FamilyId, photoId, photo, ct);
            await photoKeys.SaveAsync(photoId, bill.FamilyId, session.KeyRing.Protect(key, PhotoLabel(photoId)), session.Now, ct);
            (bill.PhotoId, bill.PhotoOn) = (photoId, session.MemberId);
        }

        session.Stamp(bill);
        await bills.SaveBillAsync(bill, ct);

        for (var i = 0; i < draft.Lines.Count; i++)
        {
            var read = draft.Lines[i];
            var suggested = i < draft.SuggestedItemNames.Count ? draft.SuggestedItemNames[i] : null;
            var match = (suggested is null ? null : items.FirstOrDefault(it => string.Equals(it.Name, suggested.Trim(), StringComparison.OrdinalIgnoreCase)))
                        ?? ProductNames.BestMatch(read.Name, items, it => it.Name);
            var line = new BillLine
            {
                Id = LineId(bill.Id, i),
                FamilyId = bill.FamilyId,
                BillId = bill.Id,
                Position = i + 1,
                RawText = Trim(read.RawText, 200),
                Name = Trim(read.Name, ListService.MaxNameLength) ?? "Item",
                Qty = read.Qty,
                UnitPrice = read.UnitPrice,
                LineTotal = read.LineTotal,
                Action = match is null ? LineAction.Add : LineAction.Match,
                ItemId = match?.Id,
            };
            session.Stamp(line);
            await bills.SaveLineAsync(line, ct);
        }

        return bill;
    }

    /// <summary>Confirms a checked bill into its month (see the class summary).</summary>
    public async Task ConfirmAsync(string billId, BillEdit header, IReadOnlyList<LineEdit> edits, CancellationToken ct = default)
    {
        var bill = await BillAsync(billId, ct) ?? throw new BillRuleException("That bill no longer exists.");
        if (bill.Status == BillStatus.Confirmed)
        {
            throw new BillRuleException("This bill has already been added.");
        }

        var month = await lists.GetMonthAsync(bill.MonthId, ct) is { IsDeleted: false } m ? m : throw new BillRuleException("That month no longer exists.");
        var itemIds = (await lists.ItemsAsync(month.ListId, month.Id, ct)).Select(i => i.Id).ToHashSet();
        foreach (var edit in edits)
        {
            if (string.IsNullOrWhiteSpace(edit.Name))
            {
                throw new BillRuleException("Every line needs a name.");
            }

            if (edit.Action == LineAction.Match && (edit.ItemId is null || !itemIds.Contains(edit.ItemId)))
            {
                throw new BillRuleException($"\"{edit.Name.Trim()}\" must be matched to an item on this month's list.");
            }

            if (edit.LineTotal is < 0 && edit.Action != LineAction.Ignore)
            {
                throw new BillRuleException($"\"{edit.Name.Trim()}\" has a negative amount. Mark it Skip, or put it in the discount.");
            }
        }

        var oldLines = await bills.LinesAsync(bill.Id, ct);
        var saved = new List<BillLine>();
        for (var i = 0; i < edits.Count; i++)
        {
            var edit = edits[i];
            var itemId = edit.Action switch
            {
                LineAction.Add => (await listService.AddItemAsync(month.ListId, month.Id, edit.Name, edit.Qty is > 0 and <= 1000 ? edit.Qty.Value : 1, null, ItemSource.Bill, ct)).Id,
                LineAction.Match => edit.ItemId,
                _ => null,
            };

            var line = new BillLine
            {
                Id = LineId(bill.Id, i),
                FamilyId = bill.FamilyId,
                BillId = bill.Id,
                Position = i + 1,
                RawText = Trim(edit.RawText, 200),
                Name = Trim(edit.Name, ListService.MaxNameLength)!,
                Qty = edit.Qty,
                UnitPrice = edit.UnitPrice,
                LineTotal = edit.LineTotal,
                Action = edit.Action,
                ItemId = itemId,
            };
            session.Stamp(line);
            await bills.SaveLineAsync(line, ct);
            saved.Add(line);
        }

        foreach (var old in oldLines.Where(o => saved.All(s => s.Id != o.Id)))
        {
            old.IsDeleted = true;
            session.Stamp(old);
            await bills.SaveLineAsync(old, ct);
        }

        bill.StoreName = Trim(header.StoreName, 60);
        bill.BillDate = header.BillDate;
        bill.Subtotal = header.Subtotal;
        bill.Tax = header.Tax;
        bill.Discount = header.Discount;
        bill.Total = header.Total ?? Money.Sum(saved.Select(l => l.LineTotal)) - (header.Discount ?? 0);
        bill.Status = BillStatus.Confirmed;
        session.Stamp(bill);
        await bills.SaveBillAsync(bill, ct);

        await events.PublishAsync(new VaultChange(bill.FamilyId, RecordTypes.Bill, bill.Id), ct);
        foreach (var line in saved)
        {
            await events.PublishAsync(new VaultChange(bill.FamilyId, RecordTypes.BillLine, line.Id), ct);
        }

        await RecomputePricesAsync(month, saved.Select(l => l.ItemId).OfType<string>().ToHashSet(), ct);
        await listService.SetStatusAsync(month.Id, MonthStatus.Shopped, ct);
    }

    /// <summary>Deletes a bill. A confirmed one is deleted for the group, and the prices it set are worked out again.</summary>
    public async Task DeleteAsync(string billId, CancellationToken ct = default)
    {
        if (await BillAsync(billId, ct) is not { } bill)
        {
            return;
        }

        var lines = await bills.LinesAsync(bill.Id, ct);
        var wasConfirmed = bill.Status == BillStatus.Confirmed;
        bill.IsDeleted = true;
        session.Stamp(bill);
        await bills.SaveBillAsync(bill, ct);
        foreach (var line in lines)
        {
            line.IsDeleted = true;
            session.Stamp(line);
            await bills.SaveLineAsync(line, ct);
        }

        if (bill.PhotoId is { } photoId && bill.PhotoOn == session.MemberId)
        {
            photos.Delete(bill.FamilyId, photoId);
            await photoKeys.DeleteAsync(photoId, ct);
        }

        if (!wasConfirmed)
        {
            return; // a draft never left this phone
        }

        await events.PublishAsync(new VaultChange(bill.FamilyId, RecordTypes.Bill, bill.Id), ct);
        foreach (var line in lines)
        {
            await events.PublishAsync(new VaultChange(bill.FamilyId, RecordTypes.BillLine, line.Id), ct);
        }

        if (await lists.GetMonthAsync(bill.MonthId, ct) is { IsDeleted: false } month)
        {
            await RecomputePricesAsync(month, lines.Select(l => l.ItemId).OfType<string>().ToHashSet(), ct);
        }
    }

    /// <summary>The bill photo, if this phone has it.</summary>
    public async Task<byte[]?> PhotoAsync(Bill bill, CancellationToken ct = default)
    {
        if (bill.PhotoId is not { } photoId || await photoKeys.GetAsync(photoId, ct) is not { } wrapped
            || session.KeyRing.Unprotect(wrapped, PhotoLabel(photoId)) is not { } key)
        {
            return null;
        }

        return await photos.ReadAsync(bill.FamilyId, photoId, key, ct);
    }

    /// <summary>Price of each affected item = sum of the confirmed lines pointing at it; such items are bought.</summary>
    private async Task RecomputePricesAsync(MonthList month, IReadOnlySet<string> itemIds, CancellationToken ct)
    {
        if (itemIds.Count == 0)
        {
            return;
        }

        var totals = new Dictionary<string, (decimal Sum, bool Any)>();
        foreach (var confirmed in (await bills.BillsOfMonthAsync(month.Id, ct)).Where(b => b.Status == BillStatus.Confirmed))
        {
            foreach (var line in await bills.LinesAsync(confirmed.Id, ct))
            {
                if (line.Action != LineAction.Ignore && line.ItemId is { } id)
                {
                    var (sum, any) = totals.GetValueOrDefault(id);
                    totals[id] = (sum + (line.LineTotal ?? 0), any || line.LineTotal is not null);
                }
            }
        }

        foreach (var id in itemIds)
        {
            if (await lists.GetItemAsync(id, ct) is not { IsDeleted: false } item)
            {
                continue;
            }

            var found = totals.TryGetValue(id, out var total);
            var price = found && total.Any ? total.Sum : (decimal?)null;
            if (item.Price == price && (!found || (item.Bought && item.Included)))
            {
                continue;
            }

            await listService.UpdateItemAsync(id, i =>
            {
                i.Price = price;
                if (found)
                {
                    i.Bought = true;
                    i.Included = true;
                }
            }, ct);
        }
    }

    internal static string LineId(string billId, int index) => Ids.Derive("line", billId, index.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private static string PhotoLabel(string photoId) => "photo/" + photoId;

    private static string? Trim(string? text, int max)
    {
        var t = text?.Trim();
        return string.IsNullOrEmpty(t) ? null : t.Length > max ? t[..max] : t;
    }
}
