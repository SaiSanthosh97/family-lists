using FamilyLists.Core.Common;
using FamilyLists.Core.Vault;
using FamilyLists.Device.BackgroundWork;
using FamilyLists.Device.Security;
using FamilyLists.Storage.Sqlite;

namespace FamilyLists.App.Services;

/// <summary>
/// Owns the encrypted database for the whole app process: create (first launch), open (every start, also in the
/// background for sync, which needs no user action because the Keystore key isn't tied to unlocking the phone),
/// erase. Screens reach the services through <see cref="Services"/> once open. Adapted from Travel Vault.
/// </summary>
public sealed class VaultHost(AppSettings settings, HttpClient http) : IVaultChangeHandler
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly AndroidKeystoreVault _keyVault = new();
    private AppServices? _services;
    private CancellationTokenSource? _debounce;

    public static string Folder => Path.Combine(FileSystem.AppDataDirectory, "vault");

    private VaultBootstrapper Boot => new(Folder, _keyVault, SystemClock.Instance);

    public bool IsSetUp => Boot.IsSetUp;

    public bool IsOpen => _services is not null;

    public AppServices Services => _services ?? throw new InvalidOperationException("The app data is not open.");

    /// <summary>Something changed (on the main thread). Screens reload when they're visible.</summary>
    public event Action<VaultChange>? Changed;

    /// <summary>Messages for the user from sync ("Ravi joined Our home").</summary>
    public event Action<string>? Notice;

    /// <summary>Local changes were made: the scheduler sends them soon.</summary>
    public event Action? NeedsSend;

    public async Task<bool> EnsureOpenAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_services is not null)
            {
                return true;
            }

            if (await Boot.UnlockAsync() is not { } vault)
            {
                return false;
            }

            Attach(vault);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task CreateAsync(string displayName)
    {
        await _gate.WaitAsync();
        try
        {
            Attach(await Boot.CreateAsync(displayName));
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Destroys the Keystore key (everything becomes unreadable), deletes all files and settings.</summary>
    public void EraseEverything()
    {
        _services?.Dispose();
        _services = null;
        BackgroundWork.SetEnabled(false);
        Boot.EraseEverything();
        settings.Clear();
    }

    public Task HandleAsync(VaultChange change, CancellationToken cancellationToken = default)
    {
        MainThread.BeginInvokeOnMainThread(() => Changed?.Invoke(change));
        if (!change.FromSync)
        {
            // A tap can make several changes; send once they've settled. Not awaited: the tap itself must not wait.
            _debounce?.Cancel();
            var cts = _debounce = new CancellationTokenSource();
            _ = SendSoonAsync(cts.Token);
        }

        return Task.CompletedTask;
    }

    private async Task SendSoonAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(1.5), ct);
            NeedsSend?.Invoke();
        }
        catch (TaskCanceledException)
        {
        }
    }

    private void Attach(OpenedVault vault)
    {
        _services = new AppServices(vault, http, this);
        _services.Sync.Notice += (_, message) => MainThread.BeginInvokeOnMainThread(() => Notice?.Invoke(message));
        BackgroundWork.SetEnabled(true);
    }
}
