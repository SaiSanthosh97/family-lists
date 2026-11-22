using FamilyLists.Core.Bills;
using FamilyLists.Core.Common;
using FamilyLists.Core.Families;
using FamilyLists.Core.Lists;
using FamilyLists.Core.Receipts;
using FamilyLists.Core.Security;

namespace FamilyLists.Core.Sync.Tests;

/// <summary>End-to-end sync between simulated phones (Asha = creator/admin, Ravi, Meera) through a relay that forgets like ntfy.sh.</summary>
public sealed class FamilySyncTests : IDisposable
{
    private const string Nov = "2026-11";
    private const string Dec = "2026-12";

    private readonly ManualClock _clock = new(new DateTimeOffset(2026, 11, 22, 9, 0, 0, TimeSpan.Zero));
    private readonly InMemoryRelay _relay;
    private readonly List<Phone> _phones = [];

    public FamilySyncTests() => _relay = new InMemoryRelay(_clock);

    private Phone NewPhone(string name)
    {
        var phone = new Phone(name, _clock, _relay);
        _phones.Add(phone);
        return phone;
    }

    /// <summary>Asha creates the group; Ravi joins in person (QR); both fully synced.</summary>
    private async Task<(Phone Asha, Phone Ravi, Family Group)> GroupOfTwoAsync()
    {
        var asha = NewPhone("Asha");
        var ravi = NewPhone("Ravi");
        var group = await asha.Sync.CreateFamilyAsync("Our home");
        await asha.SyncAsync();

        var qr = InviteCodec.Encode(await asha.Sync.CreateInPersonInviteAsync(group.Id));
        await ravi.Sync.JoinInPersonAsync((InPersonInvite)InviteCodec.Decode(qr, _clock.UtcNow)!);
        await Rounds(asha, ravi);
        return (asha, ravi, group);
    }

    /// <summary>A groceries list with November started and three items.</summary>
    private static async Task<(ShoppingList List, MonthList Month)> GroceriesAsync(Phone phone, string groupId)
    {
        var list = await phone.Lists.CreateListAsync(groupId, "Groceries", ListKind.Monthly);
        var month = await phone.Lists.StartMonthAsync(list.Id, Nov);
        await phone.Lists.AddItemAsync(list.Id, month.Id, "Milk");
        await phone.Lists.AddItemAsync(list.Id, month.Id, "Basmati rice 5kg");
        await phone.Lists.AddItemAsync(list.Id, month.Id, "Bananas");
        return (list, month);
    }

    /// <summary>Lets every phone sync a few times, the way background sync would over half an hour.</summary>
    private static async Task Rounds(params Phone[] phones)
    {
        for (var i = 0; i < 3; i++)
        {
            foreach (var phone in phones)
            {
                await phone.SyncAsync();
            }
        }
    }

    [Fact]
    public async Task In_person_join_then_lists_and_items_reach_the_other_phone()
    {
        var (asha, ravi, group) = await GroupOfTwoAsync();
        Assert.True(FamilySync.Admitted((await ravi.Families.GetAsync(group.Id))!));
        Assert.Contains("Ravi joined Our home", asha.Notices);

        var (list, _) = await GroceriesAsync(asha, group.Id);
        await Rounds(asha, ravi);

        Assert.Equal("Groceries", Assert.Single(await ravi.Lists.ListsAsync(group.Id)).Name);
        Assert.Equal(["Milk", "Basmati rice 5kg", "Bananas"], await ravi.NamesAsync(list.Id, Nov));
        Assert.Empty(asha.Failures.Concat(ravi.Failures));
    }

    [Fact]
    public async Task A_tick_in_the_shop_reaches_the_other_phone_and_edits_flow_back()
    {
        var (asha, ravi, group) = await GroupOfTwoAsync();
        var (list, month) = await GroceriesAsync(asha, group.Id);
        await asha.Lists.SetStatusAsync(month.Id, MonthStatus.Final);
        await Rounds(asha, ravi);

        var milk = await ravi.ItemAsync(list.Id, Nov, "Milk");
        await ravi.Lists.SetBoughtAsync(milk.Id, true);
        await ravi.Sync.FlushAsync(group.Id);
        await asha.SyncAsync();

        Assert.True((await asha.ItemAsync(list.Id, Nov, "Milk")).Bought);
        Assert.Equal(MonthStatus.Final, (await ravi.Lists.MonthAsync(list.Id, Nov))!.Status);

        await asha.Lists.DeleteItemAsync((await asha.ItemAsync(list.Id, Nov, "Bananas")).Id);
        await Rounds(asha, ravi);
        Assert.DoesNotContain("Bananas", await ravi.NamesAsync(list.Id, Nov));
    }

    [Fact]
    public async Task Concurrent_edits_of_one_item_settle_the_same_way_on_both_phones()
    {
        var (asha, ravi, group) = await GroupOfTwoAsync();
        var (list, _) = await GroceriesAsync(asha, group.Id);
        await Rounds(asha, ravi);

        var id = (await asha.ItemAsync(list.Id, Nov, "Milk")).Id;
        await asha.Lists.UpdateItemAsync(id, i => i.Qty = 2);
        _clock.Advance(TimeSpan.FromSeconds(5));
        await ravi.Lists.UpdateItemAsync(id, i => i.Qty = 3); // later edit wins
        await Rounds(asha, ravi);

        Assert.Equal(3, (await asha.Lists.ItemAsync(id))!.Qty);
        Assert.Equal(3, (await ravi.Lists.ItemAsync(id))!.Qty);
    }

    [Fact]
    public async Task Both_phones_starting_the_same_month_offline_end_up_with_one_month_and_no_duplicates()
    {
        var (asha, ravi, group) = await GroupOfTwoAsync();
        var (list, month) = await GroceriesAsync(asha, group.Id);
        await asha.Lists.SetStatusAsync(month.Id, MonthStatus.Shopped);
        await Rounds(asha, ravi);

        _clock.Set(new DateTimeOffset(2026, 12, 2, 9, 0, 0, TimeSpan.Zero));
        var a = await asha.Lists.StartMonthAsync(list.Id, Dec);
        var r = await ravi.Lists.StartMonthAsync(list.Id, Dec); // both before syncing
        Assert.Equal(a.Id, r.Id);

        // Asha removes an item; Ravi's late copy of the same "start" must not bring it back.
        await asha.Lists.DeleteItemAsync((await asha.ItemAsync(list.Id, Dec, "Bananas")).Id);
        await Rounds(asha, ravi);

        Assert.Equal(["Milk", "Basmati rice 5kg"], await asha.NamesAsync(list.Id, Dec));
        Assert.Equal(["Milk", "Basmati rice 5kg"], await ravi.NamesAsync(list.Id, Dec));
        Assert.Equal(2, (await ravi.Lists.MonthsAsync(list.Id)).Count);
    }

    [Fact]
    public async Task A_confirmed_bill_puts_prices_and_new_items_on_both_phones_and_next_month_includes_them()
    {
        var (asha, ravi, group) = await GroupOfTwoAsync();
        var (list, month) = await GroceriesAsync(asha, group.Id);
        await Rounds(asha, ravi);

        var draft = new ReceiptDraft("FreshMart", new DateOnly(2026, 11, 21), null, null, null, 905m,
        [
            new("AMUL TONED MILK 1L", "Toned milk 1L", 2, 60, 120),
            new("INDIA GATE BASMATI 5KG", "Basmati rice 5kg", 1, 650, 650),
            new("COLGATE 200G", "Toothpaste 200g", 1, 130, 130),
            new("CARRY BAG", "Carry bag", 1, 5, 5),
        ]);
        var bill = await ravi.Bills.CreateDraftAsync(month.Id, draft, photo: [1, 2, 3]);
        var lines = await ravi.Bills.LinesAsync(bill.Id);
        Assert.Equal([LineAction.Match, LineAction.Match, LineAction.Add, LineAction.Add], lines.Select(l => l.Action));

        // Asha sees nothing while Ravi is still checking the draft.
        await Rounds(ravi, asha);
        Assert.Empty(await asha.Bills.BillsOfMonthAsync(month.Id));

        var edits = lines.Select(l => new LineEdit(l.Name, l.Qty, l.UnitPrice, l.LineTotal, l.Name == "Carry bag" ? LineAction.Ignore : l.Action, l.ItemId, l.RawText)).ToList();
        await ravi.Bills.ConfirmAsync(bill.Id, new BillEdit("FreshMart", new DateOnly(2026, 11, 21), null, null, null, 905m), edits);
        await Assert.ThrowsAsync<BillRuleException>(() => ravi.Bills.ConfirmAsync(bill.Id, new BillEdit(null, null, null, null, null, null), edits));
        await Rounds(ravi, asha);

        Assert.Equal(120m, (await asha.ItemAsync(list.Id, Nov, "Milk")).Price);
        Assert.True((await asha.ItemAsync(list.Id, Nov, "Milk")).Bought);
        var toothpaste = await asha.ItemAsync(list.Id, Nov, "Toothpaste 200g");
        Assert.Equal((ItemSource.Bill, 130m), (toothpaste.Source, toothpaste.Price));
        Assert.Equal(MonthStatus.Shopped, (await asha.Lists.MonthAsync(list.Id, Nov))!.Status);

        var spending = Assert.Single(await asha.Spending.ForListAsync(list.Id));
        Assert.Equal((905m, SpendSource.Bills), (spending.Total, spending.Source));
        Assert.Equal("Carry bag", Assert.Single(spending.OtherLines).Name);
        Assert.Null(await asha.Bills.PhotoAsync((await asha.Bills.BillAsync(bill.Id))!)); // the photo stays on Ravi's phone
        Assert.NotNull(await ravi.Bills.PhotoAsync((await ravi.Bills.BillAsync(bill.Id))!));

        await asha.Lists.StartMonthAsync(list.Id, Dec);
        Assert.Contains("Toothpaste 200g", await asha.NamesAsync(list.Id, Dec));
    }

    [Fact]
    public async Task Expenses_carry_forward_with_amounts_and_paid_ticks_reach_the_other_phone()
    {
        var (asha, ravi, group) = await GroupOfTwoAsync();
        var list = await asha.Lists.CreateListAsync(group.Id, "Home expenses", ListKind.Expenses);
        var nov = await asha.Lists.StartMonthAsync(list.Id, Nov);
        var rent = await asha.Lists.AddExpenseAsync(list.Id, nov.Id, "Rent", 15000, dueDay: 5);
        await asha.Lists.AddExpenseAsync(list.Id, nov.Id, "Internet", 999);
        await Assert.ThrowsAsync<ListRuleException>(() => asha.Lists.AddExpenseAsync(list.Id, nov.Id, "Bad", 10, dueDay: 40));
        await Rounds(asha, ravi);

        Assert.Equal(ListKind.Expenses, Assert.Single(await ravi.Lists.ListsAsync(group.Id)).Kind);
        await ravi.Lists.SetBoughtAsync(rent.Id, true); // Ravi paid the rent
        await Rounds(ravi, asha);
        Assert.True((await asha.Lists.ItemAsync(rent.Id))!.Bought);

        // Nobody opened December until the 10th: both phones start it offline and agree.
        _clock.Set(new DateTimeOffset(2026, 12, 10, 9, 0, 0, TimeSpan.Zero));
        var a = await asha.Lists.StartMonthAsync(list.Id, Dec);
        await ravi.Lists.StartMonthAsync(list.Id, Dec);
        await Rounds(asha, ravi);

        var dec = await ravi.Lists.ItemsAsync(list.Id, a.Id);
        Assert.Equal(["Rent", "Internet"], dec.Select(i => i.Name));
        Assert.Equal([(15000m, 5, false, false), (999m, (int?)null, false, true)], dec.Select(i => (i.Price!.Value, i.DueDay, i.Bought, i.UnpaidLast)));
        Assert.Equal(dec.Select(i => i.Id), (await asha.Lists.ItemsAsync(list.Id, a.Id)).Select(i => i.Id));

        var summary = Expenses.Summarize(dec, Dec, DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime));
        Assert.Equal((15999m, 0m, 1), (summary.Total, summary.Paid, summary.Overdue)); // rent was due on the 5th

        var history = await asha.Spending.ForListAsync(list.Id);
        Assert.Equal([(15999m, SpendSource.Expenses), (15999m, SpendSource.Expenses)], history.Select(m => (m.Total!.Value, m.Source)));
        Assert.Empty(asha.Failures.Concat(ravi.Failures));
    }

    [Fact]
    public async Task An_expense_added_to_last_month_late_can_be_brought_into_this_month_once()
    {
        var (asha, ravi, group) = await GroupOfTwoAsync();
        var list = await asha.Lists.CreateListAsync(group.Id, "Home expenses", ListKind.Expenses);
        var nov = await asha.Lists.StartMonthAsync(list.Id, Nov);
        await asha.Lists.AddExpenseAsync(list.Id, nov.Id, "Rent", 15000);
        var dec = await asha.Lists.StartMonthAsync(list.Id, Dec);

        // Remembered later: the school fee also goes on every month, and the water can wasn't needed in December.
        await asha.Lists.AddExpenseAsync(list.Id, nov.Id, "School fee", 4500, dueDay: 10);
        await asha.Lists.AddExpenseAsync(list.Id, nov.Id, "Water cans", 300);
        var (from, missing) = await asha.Lists.MissingFromPreviousAsync(list.Id, Dec);
        Assert.Equal(Nov, from!.Period);
        Assert.Equal(["School fee", "Water cans"], missing.Select(m => m.Name));

        await Rounds(asha, ravi);
        Assert.Equal(2, await asha.Lists.CarryMissingAsync(list.Id, Dec));
        Assert.Equal(2, await ravi.Lists.CarryMissingAsync(list.Id, Dec)); // Ravi too, before syncing
        await asha.Lists.DeleteItemAsync((await asha.ItemAsync(list.Id, Dec, "Water cans")).Id);
        await Rounds(asha, ravi);

        Assert.Equal(["Rent", "School fee"], await ravi.NamesAsync(list.Id, Dec));
        Assert.Equal(4500m, (await ravi.ItemAsync(list.Id, Dec, "School fee")).Price);
        Assert.Equal(0, await asha.Lists.CarryMissingAsync(list.Id, Dec)); // removed on purpose: not brought back
        Assert.Equal(dec.Id, (await ravi.Lists.MonthAsync(list.Id, Dec))!.Id);
    }

    [Fact]
    public async Task A_remote_join_into_a_big_family_fits_in_one_relay_message()
    {
        // Found with the real ntfy.sh: the approval (group record with every member's keys) was over the ~3.8 KB
        // message limit already with two long-named members, so it was never sent.
        var (asha, ravi, group) = await GroupOfTwoAsync();
        foreach (var name in new[] { "Meenakshi Sundaram", "Venkata Lakshmi Narasimha", "Grandma Saraswathi Devi", "Uncle Ramakrishna Prasad" })
        {
            var phone = NewPhone(name);
            await phone.Sync.JoinInPersonAsync((InPersonInvite)InviteCodec.Decode(InviteCodec.Encode(await asha.Sync.CreateInPersonInviteAsync(group.Id)), _clock.UtcNow)!);
            await Rounds(asha, phone);
        }

        var cousin = NewPhone("Cousin Subrahmanyam Iyer");
        var invite = (RemoteInvite)InviteCodec.Decode(InviteCodec.Encode(await asha.Sync.CreateRemoteInviteAsync(group.Id)), _clock.UtcNow)!;
        await cousin.Sync.RequestRemoteJoinAsync(invite);
        await asha.SyncAsync();
        await asha.Sync.ApproveJoinAsync(group.Id, Assert.Single(await asha.Sync.PendingJoinsAsync(group.Id)).Member.MemberId);

        Assert.Equal(JoinOutcome.Joined, await cousin.Sync.CheckJoinAsync(invite.InviteId));
        Assert.Equal(7, (await cousin.Families.GetAsync(group.Id))!.ActiveMembers.Count());

        // Removing someone sends the new key to everyone else, with the whole member list: that must fit too.
        await Rounds(asha, ravi, cousin);
        await asha.Sync.RemoveMemberAsync(group.Id, ravi.Id);
        var (list, _) = await GroceriesAsync(asha, group.Id);
        await Rounds(asha, cousin, ravi);
        Assert.Equal(["Milk", "Basmati rice 5kg", "Bananas"], await cousin.NamesAsync(list.Id, Nov));
        Assert.Empty(asha.Failures.Concat(ravi.Failures).Concat(cousin.Failures));
    }

    [Fact]
    public async Task Approving_again_after_the_connection_dropped_still_lets_them_in()
    {
        var (asha, _, group) = await GroupOfTwoAsync();
        var meera = NewPhone("Meera");
        var invite = (RemoteInvite)InviteCodec.Decode(InviteCodec.Encode(await asha.Sync.CreateRemoteInviteAsync(group.Id)), _clock.UtcNow)!;
        await meera.Sync.RequestRemoteJoinAsync(invite);
        await asha.SyncAsync();
        var memberId = Assert.Single(await asha.Sync.PendingJoinsAsync(group.Id)).Member.MemberId;

        _relay.Offline = true;
        await Assert.ThrowsAsync<HttpRequestException>(() => asha.Sync.ApproveJoinAsync(group.Id, memberId));
        _relay.Offline = false;
        Assert.Equal(JoinOutcome.Waiting, await meera.Sync.CheckJoinAsync(invite.InviteId));

        await asha.Sync.ApproveJoinAsync(group.Id, memberId);
        Assert.Equal(JoinOutcome.Joined, await meera.Sync.CheckJoinAsync(invite.InviteId));
        Assert.Empty(await asha.Sync.PendingJoinsAsync(group.Id));
    }

    [Fact]
    public async Task Remote_invite_needs_approval_and_both_phones_show_the_same_safety_code()
    {
        var (asha, ravi, group) = await GroupOfTwoAsync();
        var (list, _) = await GroceriesAsync(asha, group.Id);
        await Rounds(asha, ravi);

        var meera = NewPhone("Meera");
        var text = InviteCodec.ShareText(await asha.Sync.CreateRemoteInviteAsync(group.Id));
        var invite = (RemoteInvite)InviteCodec.Decode(text, _clock.UtcNow)!;
        var meerasCode = await meera.Sync.RequestRemoteJoinAsync(invite);

        await meera.SyncAsync();
        Assert.Equal(JoinOutcome.Waiting, await meera.Sync.CheckJoinAsync(invite.InviteId));
        Assert.Null(await meera.Families.GetAsync(group.Id)); // nothing without approval

        await asha.SyncAsync();
        var pending = Assert.Single(await asha.Sync.PendingJoinsAsync(group.Id));
        Assert.Equal(meerasCode, pending.SafetyCode);
        Assert.Equal("Meera", pending.Member.DisplayName);

        await asha.Sync.ApproveJoinAsync(group.Id, pending.Member.MemberId);
        await Rounds(meera, asha, ravi, meera);

        Assert.Contains("You joined Our home", meera.Notices);
        Assert.Equal(["Milk", "Basmati rice 5kg", "Bananas"], await meera.NamesAsync(list.Id, Nov));
        Assert.Equal(3, (await ravi.Families.GetAsync(group.Id))!.ActiveMembers.Count());
    }

    [Fact]
    public async Task A_rejected_or_forged_remote_join_gets_nothing()
    {
        var (asha, _, group) = await GroupOfTwoAsync();
        var stranger = NewPhone("Stranger");
        var invite = await asha.Sync.CreateRemoteInviteAsync(group.Id);

        var forged = invite with { JoinSecret = InviteCodec.NewJoinSecret() };
        await stranger.Sync.RequestRemoteJoinAsync(forged);
        await asha.SyncAsync();
        Assert.Empty(await asha.Sync.PendingJoinsAsync(group.Id));

        await stranger.Sync.RequestRemoteJoinAsync(invite);
        await asha.SyncAsync();
        var pending = Assert.Single(await asha.Sync.PendingJoinsAsync(group.Id));
        await asha.Sync.RejectJoinAsync(group.Id, pending.Member.MemberId);
        Assert.Equal(JoinOutcome.Rejected, await stranger.Sync.CheckJoinAsync(invite.InviteId));
        Assert.Null(await stranger.Families.GetAsync(group.Id));
    }

    [Fact]
    public async Task Removing_a_member_rotates_keys_and_the_removed_phone_gets_nothing_new()
    {
        var (asha, ravi, group) = await GroupOfTwoAsync();
        var meera = NewPhone("Meera");
        await meera.Sync.JoinInPersonAsync(await asha.Sync.CreateInPersonInviteAsync(group.Id));
        await Rounds(meera, asha, ravi, meera);

        await asha.Sync.RemoveMemberAsync(group.Id, ravi.Id);
        await Rounds(asha, ravi, meera);
        Assert.True(await ravi.Sync.IsRemovedAsync(group.Id));
        Assert.Contains(ravi.Notices, n => n.StartsWith("You were removed", StringComparison.Ordinal));
        Assert.Equal(2, (await meera.Families.GetAsync(group.Id))!.KeyEpoch);

        var secret = await asha.Lists.CreateListAsync(group.Id, "Birthday surprise", ListKind.Todo);
        await Rounds(asha, ravi, meera);
        Assert.NotNull(await meera.Lists.GetListAsync(secret.Id));
        Assert.Null(await ravi.Lists.GetListAsync(secret.Id));
    }

    [Fact]
    public async Task Two_groups_on_one_phone_stay_separate()
    {
        var (asha, ravi, home) = await GroupOfTwoAsync();
        var parents = await ravi.Sync.CreateFamilyAsync("Parents' house");
        var parentsList = await ravi.Lists.CreateListAsync(parents.Id, "Medicines", ListKind.Todo);
        await ravi.Lists.AddItemAsync(parentsList.Id, null, "BP tablets");
        var homeList = await asha.Lists.CreateListAsync(home.Id, "Chores", ListKind.Todo);
        await Rounds(asha, ravi);

        Assert.Equal(2, (await ravi.Families.ListAsync()).Count);
        Assert.Null(await asha.Families.GetAsync(parents.Id));
        Assert.Null(await asha.Lists.GetListAsync(parentsList.Id));
        Assert.Equal("Chores", Assert.Single(await ravi.Lists.ListsAsync(home.Id)).Name);
        Assert.Equal("Medicines", Assert.Single(await ravi.Lists.ListsAsync(parents.Id)).Name);
        Assert.NotNull(homeList);
    }

    [Fact]
    public async Task A_phone_away_longer_than_the_relay_keeps_messages_catches_up_from_the_group()
    {
        var (asha, ravi, group) = await GroupOfTwoAsync();
        var (list, _) = await GroceriesAsync(asha, group.Id);
        await asha.SyncAsync();

        _clock.Advance(TimeSpan.FromDays(2)); // Ravi's phone was off; the relay has forgotten everything

        await ravi.SyncAsync(); // "I was away" → state request
        await asha.SyncAsync(); // answers with everything it holds
        await ravi.SyncAsync(); // gets it

        Assert.Equal(["Milk", "Basmati rice 5kg", "Bananas"], await ravi.NamesAsync(list.Id, Nov));
    }

    [Fact]
    public async Task Two_messages_the_relay_returns_in_the_wrong_order_are_both_applied()
    {
        // Found with the real ntfy.sh: a confirmed bill (one big message) and the month's new status (a small one),
        // sent in the same second, came back small-first, and the bill was then thrown away as a "replay".
        var (asha, ravi, group) = await GroupOfTwoAsync();
        var (list, month) = await GroceriesAsync(asha, group.Id);
        await Rounds(asha, ravi);

        await asha.Lists.SetBoughtAsync((await asha.ItemAsync(list.Id, Nov, "Milk")).Id, true);
        await asha.Sync.FlushAsync(group.Id);
        await asha.Lists.SetStatusAsync(month.Id, MonthStatus.Final);
        await asha.Sync.FlushAsync(group.Id);
        _relay.SwapLastTwo(group.RelayTopic);
        await ravi.SyncAsync();

        Assert.True((await ravi.ItemAsync(list.Id, Nov, "Milk")).Bought);
        Assert.Equal(MonthStatus.Final, (await ravi.Lists.MonthAsync(list.Id, Nov))!.Status);

        // Each message is still accepted only once.
        var both = _relay.All(group.RelayTopic).TakeLast(2).ToList();
        await ravi.Lists.SetBoughtAsync((await ravi.ItemAsync(list.Id, Nov, "Milk")).Id, false);
        foreach (var message in both)
        {
            await _relay.PublishAsync(group.RelayTopic, message.Text);
        }

        await ravi.SyncAsync();
        Assert.False((await ravi.ItemAsync(list.Id, Nov, "Milk")).Bought);
        Assert.Empty(asha.Failures.Concat(ravi.Failures));
    }

    [Fact]
    public async Task Forged_replayed_and_foreign_messages_are_ignored()
    {
        var (asha, ravi, group) = await GroupOfTwoAsync();
        var (list, _) = await GroceriesAsync(asha, group.Id);
        await asha.SyncAsync();

        var attacker = DeviceKeyPair.Create();
        var fake = Envelope.Create(group.Id, "attacker", 1, HybridTimestamp.Zero, Kinds.Changes, new ChangesBody([]));
        await _relay.PublishAsync(group.RelayTopic, WireCodec.Encode(fake, AeadBox.NewKey(), 1, attacker));
        await _relay.PublishAsync(group.RelayTopic, "hello");

        var genuine = _relay.All(group.RelayTopic).First(m => m.Text.StartsWith("fl2.", StringComparison.Ordinal));
        await ravi.SyncAsync();
        var before = await ravi.NamesAsync(list.Id, Nov);
        await _relay.PublishAsync(group.RelayTopic, genuine.Text);
        await ravi.SyncAsync();

        Assert.Equal(before, await ravi.NamesAsync(list.Id, Nov));
        Assert.Empty(ravi.Failures);
    }

    [Fact]
    public async Task Leaving_wipes_the_phone_and_the_admin_rotates_the_key()
    {
        var (asha, ravi, group) = await GroupOfTwoAsync();
        await GroceriesAsync(asha, group.Id);
        await Rounds(asha, ravi);

        await ravi.Sync.LeaveFamilyAsync(group.Id);
        Assert.Null(await ravi.Families.GetAsync(group.Id));
        Assert.Empty(await ravi.Lists.ListsAsync(group.Id));

        await asha.SyncAsync();
        var ashasGroup = (await asha.Families.GetAsync(group.Id))!;
        Assert.False(ashasGroup.IsActiveMember(ravi.Id));
        Assert.Equal(2, ashasGroup.KeyEpoch);
    }

    [Fact]
    public async Task Offline_changes_wait_in_the_outbox_and_go_out_when_back_online()
    {
        var (asha, ravi, group) = await GroupOfTwoAsync();
        _relay.Offline = true;
        var list = await asha.Lists.CreateListAsync(group.Id, "Chores", ListKind.Todo);
        var report = await asha.Sync.SyncFamilyAsync(group.Id);
        Assert.NotEmpty(report.Errors);
        Assert.True(await asha.Sync.PendingCountAsync(group.Id) > 0);

        _relay.Offline = false;
        await Rounds(asha, ravi);
        Assert.Equal(0, await asha.Sync.PendingCountAsync(group.Id));
        Assert.NotNull(await ravi.Lists.GetListAsync(list.Id));
    }

    [Fact]
    public async Task A_record_pointing_into_another_group_is_refused()
    {
        var (asha, ravi, home) = await GroupOfTwoAsync();
        var other = await ravi.Sync.CreateFamilyAsync("Office");
        var officeList = await ravi.Lists.CreateListAsync(other.Id, "Snacks", ListKind.Todo);
        await Rounds(asha, ravi);

        // Asha (a member of "Our home" only) tries to slip an item into Ravi's office list through the home group.
        var handlers = ListSync.Handlers(ravi.ListRepo).ToDictionary(h => h.Type);
        var item = new ListItem { Id = Ids.New(), FamilyId = home.Id, ListId = officeList.Id, Name = "Sneaky", Version = new HybridTimestamp(9_999_999_999_999, 0, "x") };
        Assert.False(await handlers[Vault.RecordTypes.Item].ApplyAsync(home.Id, item.Id, ListSync.Write(item)));
        Assert.Empty(await ravi.Lists.ItemsAsync(officeList.Id, null));
    }

    public void Dispose()
    {
        foreach (var phone in _phones)
        {
            phone.Dispose();
        }
    }
}

public class WireCodecTests
{
    [Fact]
    public void Envelopes_round_trip_and_are_padded_to_buckets()
    {
        using var keys = DeviceKeyPair.Create();
        var groupKey = AeadBox.NewKey();
        var small = Envelope.Create("grp", "me", 1, HybridTimestamp.Zero, Kinds.CheckIn, new CheckInBody("Asha"));
        var bigger = Envelope.Create("grp", "me", 2, HybridTimestamp.Zero, Kinds.CheckIn, new CheckInBody("Asha with a longer name"));

        var a = WireCodec.Encode(small, groupKey, 3, keys);
        var b = WireCodec.Encode(bigger, groupKey, 3, keys);

        Assert.Equal(a.Length, b.Length); // same bucket: sizes don't reveal content
        Assert.StartsWith("fl2.", a, StringComparison.Ordinal);
        Assert.Equal(3, WireCodec.EpochOf(a));
        var decoded = WireCodec.Decode(a, "grp", groupKey)!.Value;
        Assert.Equal("Asha", decoded.Envelope.BodyAs<CheckInBody>()!.Name);
        Assert.True(Signatures.Verify(keys.Public.Signing, decoded.Box, decoded.Signature));
        Assert.Null(WireCodec.Decode(a, "other-group", groupKey));
        Assert.Null(WireCodec.Decode(a, "grp", AeadBox.NewKey()));
    }

    [Fact]
    public void Signed_changes_detect_any_modification()
    {
        using var author = DeviceKeyPair.Create();
        var change = SignedChange.Create("itm", "i1", "grp", new HybridTimestamp(5, 0, "me"), "me", "{\"name\":\"Milk\"}", author);
        Assert.True(change.Verify(author.Public.Signing));
        Assert.False((change with { Payload = "{\"name\":\"HACKED\"}" }).Verify(author.Public.Signing));
        Assert.False((change with { Author = "someone" }).Verify(author.Public.Signing));
    }

    [Fact]
    public void Batches_stay_within_the_relay_limit()
    {
        using var keys = DeviceKeyPair.Create();
        var groupKey = AeadBox.NewKey();
        var changes = Enumerable.Range(0, 60)
            .Select(i => SignedChange.Create("itm", Ids.New(), "grp", new HybridTimestamp(i, 0, "me"), "me", Convert.ToBase64String(AeadBox.NewKey()) + i, keys))
            .ToList();
        var batches = WireCodec.Batch(changes, b => WireCodec.Fits(Envelope.Create("grp", "me", 1, HybridTimestamp.Zero, Kinds.Changes, new ChangesBody([.. b])), groupKey, keys));
        Assert.True(batches.Count > 1);
        Assert.Equal(60, batches.Sum(b => b.Count));
        foreach (var batch in batches)
        {
            var text = WireCodec.Encode(Envelope.Create("grp", "me", 1, HybridTimestamp.Zero, Kinds.Changes, new ChangesBody([.. batch])), groupKey, 1, keys);
            Assert.True(text.Length <= WireCodec.MaxTextLength);
        }
    }

    [Fact]
    public void A_full_list_item_fits_in_one_relay_message()
    {
        using var keys = DeviceKeyPair.Create();
        var item = new ListItem
        {
            Id = Ids.New(), FamilyId = Ids.New(), ListId = Ids.New(), MonthId = Ids.New(), Name = new string('x', 120),
            Notes = new string('n', 500), Unit = "kg", Price = 123456.78m, Version = new HybridTimestamp(1, 0, Ids.New()),
        };
        var change = SignedChange.Create("itm", item.Id, item.FamilyId, item.Version, Ids.New(), ListSync.Write(item), keys);
        Assert.True(WireCodec.Fits(Envelope.Create("grp", "me", 1, HybridTimestamp.Zero, Kinds.Changes, new ChangesBody([change])), AeadBox.NewKey(), keys));
    }
}
