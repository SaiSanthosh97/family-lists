using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FamilyLists.App.Services;
using FamilyLists.App.Views;
using FamilyLists.Core.ClaudeReader;
using FamilyLists.Core.Families;
using FamilyLists.Core.Sync;

namespace FamilyLists.App.ViewModels;

/// <summary>
/// Joining a group (from Travel Vault): scan the QR code on another member's phone (in person, joins at once), or
/// paste an invite message (read the 6-digit code to the person who sent it; joined after they approve).
/// </summary>
public sealed partial class JoinViewModel(VaultHost host, AppSettings settings, SyncScheduler scheduler, Dialogs dialogs) : ObservableObject
{
    private RemoteInvite? _pending;

    [ObservableProperty]
    public partial bool Waiting { get; set; }

    [ObservableProperty]
    public partial string SafetyCode { get; set; } = "";

    [ObservableProperty]
    public partial string WaitingText { get; set; } = "";

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [RelayCommand]
    private async Task ScanAsync()
    {
        try
        {
            if (await MediaPicker.Default.CapturePhotoAsync() is not { } photo)
            {
                return;
            }

            await using var stream = await photo.OpenReadAsync();
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            var codes = await host.Services.BarcodeScanner.ScanAsync(buffer.ToArray());
            var text = codes.Select(c => c.Value).FirstOrDefault(v => v.StartsWith("fl1:", StringComparison.Ordinal));
            if (text is null)
            {
                await dialogs.AlertAsync("Join a group", "No Family Lists invite was found in that photo. Hold the phone steady, close to the code, in good light.");
                return;
            }

            await UseAsync(text);
        }
        catch (PermissionException)
        {
            await dialogs.AlertAsync("Join a group", "Family Lists needs the camera to read the code. You can allow it in Android settings.");
        }
    }

    [RelayCommand]
    private async Task PasteAsync()
    {
        var text = await Clipboard.Default.GetTextAsync();
        if (string.IsNullOrWhiteSpace(text) || !text.Contains("fl1:", StringComparison.Ordinal))
        {
            text = await dialogs.PromptAsync("Join a group", "Paste the whole invite message here.", maxLength: 4000);
        }

        if (!string.IsNullOrWhiteSpace(text))
        {
            await UseAsync(text);
        }
    }

    private async Task UseAsync(string text)
    {
        IsBusy = true;
        try
        {
            var decoded = InviteCodec.Decode(text, DateTimeOffset.UtcNow, out var problem);
            if (problem is not null)
            {
                Android.Util.Log.Info("FamilyLists", $"Invite not accepted: {problem}");
            }

            switch (decoded)
            {
                case InPersonInvite inPerson:
                    var group = await host.Services.Sync.JoinInPersonAsync(inPerson);
                    settings.CurrentGroupId = group.Id;
                    await scheduler.SyncNowAsync();
                    Dialogs.Toast($"Joining {group.Name}… the lists appear in a moment.");
                    await Nav.HomeAsync();
                    break;
                case RemoteInvite remote:
                    _pending = remote;
                    SafetyCode = await host.Services.Sync.RequestRemoteJoinAsync(remote);
                    WaitingText = $"Asked to join {remote.FamilyName}. Call the person who invited you and read them this code. They let you in after checking it.";
                    Waiting = true;
                    break;
                default:
                    await dialogs.AlertAsync("Join a group", "That isn't a valid invite, or it has expired. Ask for a new one.");
                    break;
            }
        }
        catch (HttpRequestException)
        {
            await dialogs.AlertAsync("Join a group", "No internet connection. Try again.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task CheckAsync()
    {
        if (_pending is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            switch (await host.Services.Sync.CheckJoinAsync(_pending.InviteId))
            {
                case JoinOutcome.Joined:
                    settings.CurrentGroupId = _pending.FamilyId;
                    await scheduler.SyncNowAsync();
                    Dialogs.Toast($"You joined {_pending.FamilyName}.");
                    await Nav.HomeAsync();
                    break;
                case JoinOutcome.Rejected:
                    Waiting = false;
                    await dialogs.AlertAsync("Join a group", "Your request was declined.");
                    break;
                case JoinOutcome.Expired:
                    Waiting = false;
                    await dialogs.AlertAsync("Join a group", "The invite expired. Ask for a new one.");
                    break;
                default:
                    Dialogs.Toast("Not approved yet. Check again after they've approved.");
                    break;
            }
        }
        catch (HttpRequestException)
        {
            await dialogs.AlertAsync("Join a group", "No internet connection. Try again.");
        }
        finally
        {
            IsBusy = false;
        }
    }
}

/// <summary>Your name, bill reading with Claude (optional, your own key), updates, about, erase.</summary>
public sealed partial class SettingsViewModel(VaultHost host, AppSettings settings, UpdateService updates, Dialogs dialogs) : ObservableObject, ILoads
{
    [ObservableProperty]
    public partial string Name { get; set; } = "";

    [ObservableProperty]
    public partial bool UseClaude { get; set; }

    [ObservableProperty]
    public partial bool HasClaudeKey { get; set; }

    [ObservableProperty]
    public partial string ClaudeStatus { get; set; } = "";

    [ObservableProperty]
    public partial string Version { get; set; } = "";

    private bool _loading;

    public async Task LoadAsync()
    {
        _loading = true;
        Name = host.Services.Session.DisplayName;
        HasClaudeKey = await host.Services.ClaudeKeyAsync() is not null;
        UseClaude = settings.UseClaude && HasClaudeKey;
        ClaudeStatus = HasClaudeKey
            ? $"Key saved on this phone (encrypted). Bills are read with {ClaudeReceiptReader.DefaultModel}, a few cents each, billed to your Anthropic account."
            : "Off. Bills are read on the phone, for free. With your own Anthropic API key, Claude can read messy bills better.";
        Version = $"Version {UpdateService.CurrentVersion.ToString(3)}";
        _loading = false;
    }

    partial void OnUseClaudeChanged(bool value)
    {
        if (!_loading)
        {
            settings.UseClaude = value && HasClaudeKey;
        }
    }

    [RelayCommand]
    private async Task RenameAsync()
    {
        if (await dialogs.PromptAsync("Your name", "Shown to new people in your groups.", Name, maxLength: 40) is { Length: > 0 } name)
        {
            await host.Services.Identity.RenameAsync(name);
            host.Services.Session.DisplayName = name.Trim();
            await LoadAsync();
        }
    }

    [RelayCommand]
    private async Task SetClaudeKeyAsync()
    {
        var key = await dialogs.PromptAsync("Anthropic API key", "Create one at console.anthropic.com → API keys, then paste it here. It's stored encrypted on this phone only and is used only to read your bills.", maxLength: 300);
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        if (!key.Trim().StartsWith("sk-ant-", StringComparison.Ordinal))
        {
            await dialogs.AlertAsync("Anthropic API key", "That doesn't look like an Anthropic API key (they start with \"sk-ant-\").");
            return;
        }

        await host.Services.SetClaudeKeyAsync(key);
        settings.UseClaude = true;
        await LoadAsync();
    }

    [RelayCommand]
    private async Task RemoveClaudeKeyAsync()
    {
        if (await dialogs.ConfirmAsync("Remove key", "Remove the Anthropic API key from this phone? Bills will be read on the phone again.", "Remove"))
        {
            await host.Services.SetClaudeKeyAsync(null);
            settings.UseClaude = false;
            await LoadAsync();
        }
    }

    [RelayCommand]
    private async Task CheckUpdatesAsync()
    {
        try
        {
            if (await updates.CheckNowAsync() is { } update)
            {
                await updates.OfferAsync(update);
            }
            else
            {
                Dialogs.Toast("You have the latest version.");
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            await dialogs.AlertAsync("Updates", "Couldn't check for updates. Check the internet and try again.");
        }
    }

    [RelayCommand]
    private Task OpenProjectAsync() => Launcher.Default.OpenAsync(UpdateService.RepositoryUrl);

    [RelayCommand]
    private async Task EraseAsync()
    {
        if (!await dialogs.ConfirmAsync("Erase everything", "Delete all lists, bills and groups from this phone? Other phones in your groups keep theirs. This can't be undone.", "Next"))
        {
            return;
        }

        var typed = await dialogs.PromptAsync("Erase everything", "Type ERASE to confirm.", maxLength: 10);
        if (typed?.Trim() != "ERASE")
        {
            return;
        }

        host.EraseEverything();
        await Nav.GoAsync("//welcome");
    }
}
