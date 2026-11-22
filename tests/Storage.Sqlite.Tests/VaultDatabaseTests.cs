using System.Text;
using FamilyLists.Core.Bills;
using FamilyLists.Core.Common;
using FamilyLists.Core.Families;
using FamilyLists.Core.Lists;
using FamilyLists.Core.Security;

namespace FamilyLists.Storage.Sqlite.Tests;

public sealed class VaultDatabaseTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 9, 0, 0, TimeSpan.Zero);
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "fl-db-" + Guid.NewGuid().ToString("N"));
    private readonly byte[] _key = AeadBox.NewKey();

    public VaultDatabaseTests() => Directory.CreateDirectory(_folder);

    private string DbPath => Path.Combine(_folder, "vault.db");

    [Fact]
    public async Task Database_file_is_encrypted_on_disk()
    {
        using (var db = VaultDatabase.Open(DbPath, _key))
        {
            var list = List("grp");
            await new SqliteListRepository(db).SaveListAsync(list);
            await new SqliteListRepository(db).SaveItemAsync(Item(list, null, "SECRET-ITEM-12345"));
        }

        var text = Encoding.ASCII.GetString(await File.ReadAllBytesAsync(DbPath));
        Assert.DoesNotContain("SQLite format 3", text);
        Assert.DoesNotContain("SECRET-ITEM", text);
    }

    [Fact]
    public async Task Wrong_key_is_rejected_and_right_key_reopens()
    {
        using (var db = VaultDatabase.Open(DbPath, _key))
        {
            await new SqliteListRepository(db).SaveListAsync(List("grp"));
        }

        Assert.Throws<WrongKeyException>(() => VaultDatabase.Open(DbPath, AeadBox.NewKey()));
        using var reopened = VaultDatabase.Open(DbPath, _key);
        Assert.Single(await new SqliteListRepository(reopened).ListsAsync("grp"));
    }

    [Fact]
    public async Task Lists_months_and_items_round_trip_in_order_and_deleted_ones_are_hidden()
    {
        using var db = VaultDatabase.Open(DbPath, _key);
        var repo = new SqliteListRepository(db);
        var list = List("grp");
        await repo.SaveListAsync(list);
        var month = new MonthList { Id = ListService.MonthId(list.Id, "2026-11"), FamilyId = "grp", ListId = list.Id, Period = "2026-11", ManualTotal = 1054.50m, Version = new HybridTimestamp(5, 1, "n") };
        await repo.SaveMonthAsync(month);
        var later = Item(list, month.Id, "Rice", position: 2);
        var first = Item(list, month.Id, "Milk", position: 1);
        first.Price = 120.25m;
        var gone = Item(list, month.Id, "Gone", position: 3);
        gone.IsDeleted = true;
        await repo.SaveItemsAsync([later, first, gone]);

        Assert.Equal(1054.50m, (await repo.GetMonthAsync(month.Id))!.ManualTotal);
        Assert.Equal(["Milk", "Rice"], (await repo.ItemsAsync(list.Id, month.Id)).Select(i => i.Name));
        Assert.Equal(120.25m, (await repo.GetItemAsync(first.Id))!.Price);
        Assert.True((await repo.GetItemAsync(gone.Id))!.IsDeleted);
        Assert.Empty(await repo.ItemsAsync(list.Id, null)); // to-do items only (none)
        Assert.Equal(new HybridTimestamp(5, 1, "n"), (await repo.GetMonthAsync(month.Id))!.Version);
    }

    [Fact]
    public async Task Items_claiming_another_groups_list_are_not_shown_on_it()
    {
        using var db = VaultDatabase.Open(DbPath, _key);
        var repo = new SqliteListRepository(db);
        var list = List("grp");
        await repo.SaveListAsync(list);
        await repo.SaveItemAsync(Item(list, null, "Real"));
        var foreign = Item(list, null, "Foreign");
        await repo.SaveItemAsync(new ListItem { Id = foreign.Id, FamilyId = "other", ListId = list.Id, Name = "Foreign", CreatedAt = Now });

        Assert.Equal("Real", Assert.Single(await repo.ItemsAsync(list.Id, null)).Name);
    }

    [Fact]
    public async Task Bills_and_lines_round_trip()
    {
        using var db = VaultDatabase.Open(DbPath, _key);
        var lists = new SqliteListRepository(db);
        var bills = new SqliteBillRepository(db);
        var list = List("grp");
        await lists.SaveListAsync(list);
        var monthId = ListService.MonthId(list.Id, "2026-11");
        await lists.SaveMonthAsync(new MonthList { Id = monthId, FamilyId = "grp", ListId = list.Id, Period = "2026-11" });
        var bill = new Bill { Id = Ids.New(), FamilyId = "grp", ListId = list.Id, MonthId = monthId, StoreName = "FreshMart", BillDate = new DateOnly(2026, 11, 21), Total = 905m, Status = BillStatus.Confirmed, CreatedAt = Now };
        await bills.SaveBillAsync(bill);
        await bills.SaveLineAsync(new BillLine { Id = Ids.New(), FamilyId = "grp", BillId = bill.Id, Position = 2, Name = "Bag", LineTotal = 5, Action = LineAction.Ignore });
        await bills.SaveLineAsync(new BillLine { Id = Ids.New(), FamilyId = "grp", BillId = bill.Id, Position = 1, Name = "Milk", Qty = 2, UnitPrice = 60, LineTotal = 120, Action = LineAction.Match, ItemId = "i1" });

        var loaded = Assert.Single(await bills.BillsOfMonthAsync(monthId));
        Assert.Equal((new DateOnly(2026, 11, 21), 905m, BillStatus.Confirmed), (loaded.BillDate, loaded.Total, loaded.Status));
        Assert.Single(await bills.BillsOfListAsync(list.Id));
        Assert.Equal(["Milk", "Bag"], (await bills.LinesAsync(bill.Id)).Select(l => l.Name));
    }

    [Fact]
    public async Task Group_keys_round_trip_and_leaving_a_group_removes_everything_of_it()
    {
        using var db = VaultDatabase.Open(DbPath, _key);
        var families = new SqliteFamilyRepository(db);
        using var device = DeviceKeyPair.Create();
        var group = Family.Create("Our home", new Member
        {
            MemberId = "me",
            DisplayName = "Me",
            SigningPublicKey = device.Public.Signing,
            AgreementPublicKey = device.Public.Agreement,
        }, Now);
        await families.SaveAsync(group);
        await families.SaveKeyAsync(group.Id, 1, [1, 2, 3]);
        var lists = new SqliteListRepository(db);
        var list = List(group.Id);
        await lists.SaveListAsync(list);
        await lists.SaveItemAsync(Item(list, null, "Milk"));
        var keep = List("another-group");
        await lists.SaveListAsync(keep);

        var loaded = (await families.GetAsync(group.Id))!;
        Assert.Equal((group.RelayTopic, "INR"), (loaded.RelayTopic, loaded.Currency));
        Assert.Equal(device.Public.Signing, loaded.Members[0].SigningPublicKey);
        Assert.Equal([1, 2, 3], await families.GetKeyAsync(group.Id, 1));

        await families.DeleteEverythingAsync(group.Id);
        Assert.Null(await families.GetAsync(group.Id));
        Assert.Null(await families.GetKeyAsync(group.Id, 1));
        Assert.Empty(await lists.ListsAsync(group.Id));
        Assert.Null(await lists.GetItemAsync((await lists.AllItemsAsync(list.Id)).FirstOrDefault()?.Id ?? "none"));
        Assert.Single(await lists.ListsAsync("another-group"));
    }

    [Fact]
    public async Task Settings_and_file_keys()
    {
        using var db = VaultDatabase.Open(DbPath, _key);
        var settings = new SqliteSettings(db);
        await settings.SetAsync("a", "1");
        Assert.Equal("1", await settings.GetAsync("a"));
        await settings.SetAsync("a", null);
        Assert.Null(await settings.GetAsync("a"));

        var keys = new SqliteFileKeyStore(db);
        await keys.SaveAsync("fv1", "grp", [9], Now);
        Assert.Contains("fv1", await keys.PresentAsync("grp"));
        Assert.Equal([9], await keys.GetAsync("fv1"));
    }

    private static ShoppingList List(string familyId) =>
        new() { Id = Ids.New(), FamilyId = familyId, Name = "Groceries", Kind = ListKind.Monthly, CreatedAt = Now };

    private static ListItem Item(ShoppingList list, string? monthId, string name, int position = 1) =>
        new() { Id = Ids.New(), FamilyId = list.FamilyId, ListId = list.Id, MonthId = monthId, Name = name, Position = position, CreatedAt = Now };

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // WAL files can linger briefly on Windows; temp folder cleanup is best effort.
        }
    }
}
