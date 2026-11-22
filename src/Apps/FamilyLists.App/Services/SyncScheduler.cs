using FamilyLists.Core.Relay;
using FamilyLists.Core.Sync;

namespace FamilyLists.App.Services;

/// <summary>
/// When sync runs:
/// <list type="bullet">
/// <item>while the app is on screen: it listens on the open group's relay topic and syncs within a second or two of
/// any message (ticks in the shop show up almost at once), plus a full sync every minute;</item>
/// <item>1.5 s after a local change (send only);</item>
/// <item>about every 15 minutes in the background (Android JobScheduler), also when the app is closed;</item>
/// <item>on "Sync now".</item>
/// </list>
/// </summary>
public sealed class SyncScheduler(VaultHost host, AppSettings settings)
{
    /// <summary>
    /// The live stream needs its own client: Android's default handler can hold back a streamed response, and the
    /// shared client's 30 s timeout would cut it off. Dead connections are caught by the relay's idle timeout instead.
    /// </summary>
    private static readonly NtfyRelayFactory LiveRelays = new(
        new HttpClient(new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(20) }) { Timeout = Timeout.InfiniteTimeSpan },
        "FamilyLists");

    private readonly SemaphoreSlim _gate = new(1, 1);
    private IDispatcherTimer? _timer;
    private CancellationTokenSource? _watch;
    private string? _watching;
    private CancellationTokenSource? _soon;

    public event Action<SyncReport>? Synced;

    public void Start() => host.NeedsSend += () => _ = SendAsync();

    public void SetForeground(bool visible)
    {
        if (!visible)
        {
            _timer?.Stop();
            StopWatching();
            return;
        }

        if (_timer is null && Application.Current?.Dispatcher is { } dispatcher)
        {
            _timer = dispatcher.CreateTimer();
            _timer.Interval = TimeSpan.FromMinutes(1);
            _timer.Tick += (_, _) => _ = RunAsync();
        }

        _timer?.Start();
        _ = RunAsync();
        WatchCurrentGroup();
    }

    /// <summary>Call when the open group changes, so the live listener follows it.</summary>
    public void WatchCurrentGroup()
    {
        var groupId = settings.CurrentGroupId;
        if (groupId is null || groupId == _watching || !host.IsOpen)
        {
            return;
        }

        StopWatching();
        _watching = groupId;
        var cts = _watch = new CancellationTokenSource();
        _ = Task.Run(() => WatchLoopAsync(groupId, cts.Token));
    }

    public Task<SyncReport> SyncNowAsync() => RunAsync();

    /// <summary>The background job (also runs with no screen, after the data opens itself).</summary>
    public async Task BackgroundAsync(CancellationToken cancellationToken)
    {
        if (await host.EnsureOpenAsync())
        {
            await host.Services.Sync.SyncAllAsync(cancellationToken);
        }
    }

    private void StopWatching()
    {
        _watch?.Cancel();
        _watch = null;
        _watching = null;
    }

    /// <summary>Keeps one streaming connection to the group's topic; reconnects after drops, with a pause.</summary>
    private async Task WatchLoopAsync(string groupId, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (await host.Services.Sync.TopicAsync(groupId, ct) is not { } topic)
                {
                    return;
                }

                Android.Util.Log.Info("FamilyLists", "Live sync: listening");
                await LiveRelays.For(topic.Server).WatchAsync(topic.Topic, () =>
                {
                    Android.Util.Log.Info("FamilyLists", "Live sync: message");
                    SyncSoon(groupId);
                    return Task.CompletedTask;
                }, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Android.Util.Log.Info("FamilyLists", $"Live sync connection dropped: {ex.Message}");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(15), ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>
    /// Several messages arrive together (one per changed item); sync once they've settled. ntfy.sh announces a message
    /// on the stream slightly before a fetch can return it, so a fetch that finds nothing is tried once more.
    /// </summary>
    private void SyncSoon(string groupId)
    {
        _soon?.Cancel();
        var cts = _soon = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cts.Token);
                if ((await RunAsync(groupId)).Received == 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(3), cts.Token);
                    await RunAsync(groupId);
                }
            }
            catch (OperationCanceledException)
            {
            }
        });
    }

    private async Task SendAsync()
    {
        if (!host.IsOpen || Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
        {
            return;
        }

        await _gate.WaitAsync();
        try
        {
            foreach (var group in await host.Services.Families.ListAsync())
            {
                await host.Services.Sync.FlushAsync(group.Id);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Android.Util.Log.Warn("FamilyLists", $"Sending failed: {ex.Message}");
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<SyncReport> RunAsync(string? groupId = null)
    {
        if (!host.IsOpen || Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
        {
            return new SyncReport(0, 0, ["No internet connection."]);
        }

        await _gate.WaitAsync();
        try
        {
            var report = groupId is null ? await host.Services.Sync.SyncAllAsync() : await host.Services.Sync.SyncFamilyAsync(groupId);
            Android.Util.Log.Info("FamilyLists", $"Synced: received {report.Received}, sent {report.Sent}, errors {report.Errors.Count}");
            MainThread.BeginInvokeOnMainThread(() => Synced?.Invoke(report));
            return report;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Android.Util.Log.Warn("FamilyLists", $"Sync failed: {ex.Message}");
            return new SyncReport(0, 0, ["Sync failed. It will try again."]);
        }
        finally
        {
            _gate.Release();
        }
    }
}
