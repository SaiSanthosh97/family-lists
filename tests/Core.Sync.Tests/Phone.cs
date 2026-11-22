using FamilyLists.Core.Bills;
using FamilyLists.Core.Common;
using FamilyLists.Core.Families;
using FamilyLists.Core.Lists;
using FamilyLists.Core.Relay;
using FamilyLists.Core.Security;
using FamilyLists.Core.Vault;
using FamilyLists.Storage.Sqlite;

namespace FamilyLists.Core.Sync.Tests;

/// <summary>One simulated phone: its own encrypted database, photo folder, keys and services, wired like the app.</summary>
internal sealed class Phone : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "fl-phone-" + Guid.NewGuid().ToString("N"));

    public Phone(string name, ManualClock clock, IRelayFactory relay, SyncOptions? options = null)
    {
        Name = name;
        Directory.CreateDirectory(_folder);
        var (ring, _) = KeyRing.CreateNew(new InMemoryKeyVault());
        Session = new VaultSession(Ids.New(), name, ring, DeviceKeyPair.Create(), clock);
        Database = VaultDatabase.Open(Path.Combine(_folder, "vault.db"), ring.DatabaseKey());

        Families = new SqliteFamilyRepository(Database);
        ListRepo = new SqliteListRepository(Database);
        BillRepo = new SqliteBillRepository(Database);
        Events = new VaultEvents([]);
        Events.HandlerFailed += ex => throw new InvalidOperationException("Handler failed", ex);

        Lists = new ListService(Session, ListRepo, Events);
        var photos = new FileVault(Path.Combine(_folder, "photos"));
        Bills = new BillService(Session, BillRepo, ListRepo, Lists, Events, photos, new SqliteFileKeyStore(Database));
        Spending = new SpendingService(ListRepo, BillRepo);
        Sync = new FamilySync(
            Session, Families, [.. ListSync.Handlers(ListRepo), .. BillSync.Handlers(BillRepo, ListRepo)],
            new SqliteSyncStore(Database), new SqliteSettings(Database), relay, Events, options);
        Sync.DeleteGroupRecords = (familyId, _) =>
        {
            photos.DeleteFamily(familyId);
            return Task.CompletedTask;
        };
        Events.Add(Sync);
        Sync.Notice += (_, message) => Notices.Add(message);
        Sync.Failed += ex => Failures.Add(ex);
    }

    public string Name { get; }

    public VaultSession Session { get; }

    public VaultDatabase Database { get; }

    public SqliteFamilyRepository Families { get; }

    public SqliteListRepository ListRepo { get; }

    public SqliteBillRepository BillRepo { get; }

    public VaultEvents Events { get; }

    public ListService Lists { get; }

    public BillService Bills { get; }

    public SpendingService Spending { get; }

    public FamilySync Sync { get; }

    public List<string> Notices { get; } = [];

    public List<Exception> Failures { get; } = [];

    public string Id => Session.MemberId;

    public Task SyncAsync() => Sync.SyncAllAsync();

    /// <summary>Names of a month's items, in list order.</summary>
    public async Task<List<string>> NamesAsync(string listId, string period) =>
        await Lists.MonthAsync(listId, period) is { } month
            ? (await Lists.ItemsAsync(listId, month.Id)).Select(i => i.Name).ToList()
            : [];

    public async Task<ListItem> ItemAsync(string listId, string period, string name) =>
        (await Lists.ItemsAsync(listId, (await Lists.MonthAsync(listId, period))!.Id)).Single(i => i.Name == name);

    public void Dispose()
    {
        Database.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
