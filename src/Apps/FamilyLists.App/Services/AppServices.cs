using FamilyLists.Core.Bills;
using FamilyLists.Core.Lists;
using FamilyLists.Core.Relay;
using FamilyLists.Core.Security;
using FamilyLists.Core.Sync;
using FamilyLists.Core.Vault;
using FamilyLists.Device.Vision;
using FamilyLists.Storage.Sqlite;

namespace FamilyLists.App.Services;

/// <summary>
/// Composition root for the open database: builds every component on top of the session, exactly as the tests wire
/// them (tests/Core.Sync.Tests/Phone.cs). Created when the data opens, disposed when it closes.
/// </summary>
public sealed class AppServices : IDisposable
{
    private const string ClaudeKeySetting = "claude.apiKey";

    public AppServices(OpenedVault vault, HttpClient http, IVaultChangeHandler uiHandler)
    {
        Vault = vault;
        var db = vault.Database;
        Families = new SqliteFamilyRepository(db);
        Settings = new SqliteSettings(db);
        ListRepository = new SqliteListRepository(db);
        BillRepository = new SqliteBillRepository(db);
        Identity = new DeviceIdentityStore(Settings, vault.Session.KeyRing);
        Events = new VaultEvents([]);

        Lists = new ListService(Session, ListRepository, Events);
        var photos = new FileVault(vault.FilesFolder);
        Bills = new BillService(Session, BillRepository, ListRepository, Lists, Events, photos, new SqliteFileKeyStore(db));
        Spending = new SpendingService(ListRepository, BillRepository);
        TextRecognizer = new MlKitTextRecognizer();
        BarcodeScanner = new MlKitBarcodeScanner();
        PdfRasterizer = new AndroidPdfRasterizer();

        Sync = new FamilySync(
            Session, Families, [.. ListSync.Handlers(ListRepository), .. BillSync.Handlers(BillRepository, ListRepository)],
            new SqliteSyncStore(db), Settings, new NtfyRelayFactory(http, "FamilyLists"), Events);
        Sync.DeleteGroupRecords = (groupId, _) =>
        {
            photos.DeleteFamily(groupId);
            return Task.CompletedTask;
        };
        Sync.Failed += ex => Android.Util.Log.Warn("FamilyLists", $"Sync problem: {ex.GetType().Name}: {ex.Message}");

        Events.Add(Sync);     // signs and queues local changes
        Events.Add(uiHandler); // refreshes screens, sends soon
    }

    public OpenedVault Vault { get; }

    public VaultSession Session => Vault.Session;

    public SqliteFamilyRepository Families { get; }

    public SqliteSettings Settings { get; }

    public SqliteListRepository ListRepository { get; }

    public SqliteBillRepository BillRepository { get; }

    public DeviceIdentityStore Identity { get; }

    public VaultEvents Events { get; }

    public ListService Lists { get; }

    public BillService Bills { get; }

    public SpendingService Spending { get; }

    public MlKitTextRecognizer TextRecognizer { get; }

    public MlKitBarcodeScanner BarcodeScanner { get; }

    public AndroidPdfRasterizer PdfRasterizer { get; }

    public FamilySync Sync { get; }

    /// <summary>The person's Anthropic API key, encrypted with this phone's key ring; null when none is set.</summary>
    public async Task<string?> ClaudeKeyAsync() =>
        await Settings.GetAsync(ClaudeKeySetting) is { } stored
        && Session.KeyRing.Unprotect(Convert.FromBase64String(stored), ClaudeKeySetting) is { } plain
            ? System.Text.Encoding.UTF8.GetString(plain)
            : null;

    public Task SetClaudeKeyAsync(string? key) =>
        Settings.SetAsync(ClaudeKeySetting, string.IsNullOrWhiteSpace(key)
            ? null
            : Convert.ToBase64String(Session.KeyRing.Protect(System.Text.Encoding.UTF8.GetBytes(key.Trim()), ClaudeKeySetting)));

    public void Dispose() => Vault.Dispose();
}
