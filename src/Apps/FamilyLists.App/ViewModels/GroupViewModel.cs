using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FamilyLists.App.Services;
using FamilyLists.App.Views;
using FamilyLists.Core.Bills;
using FamilyLists.Core.Families;
using FamilyLists.Core.Sync;
using FamilyLists.Core.Vault;
using QRCoder;

namespace FamilyLists.App.ViewModels;

public sealed record MemberRow(string MemberId, string Name, string Detail, bool CanManage);

public sealed record PendingJoinRow(string MemberId, string Name, string SafetyCode);

/// <summary>
/// The open group (adapted from Travel Vault's family screen): who's in it and when each phone was last seen;
/// inviting (QR in person, or a message with approval after comparing a safety code); approving join requests;
/// removing members (the group key changes); currency; leaving; other groups; sync status.
/// </summary>
public sealed partial class GroupViewModel(VaultHost host, AppSettings settings, SyncScheduler scheduler, Dialogs dialogs) : ObservableObject, ILoads
{
    private Family? _group;

    [ObservableProperty]
    public partial string GroupName { get; set; } = "";

    [ObservableProperty]
    public partial bool HasGroup { get; set; }

    [ObservableProperty]
    public partial bool IsAdmin { get; set; }

    [ObservableProperty]
    public partial string Status { get; set; } = "";

    [ObservableProperty]
    public partial string Currency { get; set; } = "INR";

    [ObservableProperty]
    public partial ImageSource? InviteQr { get; set; }

    [ObservableProperty]
    public partial bool ShowQr { get; set; }

    [ObservableProperty]
    public partial string QrExpiry { get; set; } = "";

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    public ObservableCollection<MemberRow> Members { get; } = [];

    public ObservableCollection<PendingJoinRow> Pending { get; } = [];

    public IReadOnlyList<string> Currencies { get; } = Money.Currencies;

    public async Task LoadAsync()
    {
        var s = host.Services;
        Members.Clear();
        Pending.Clear();
        _group = settings.CurrentGroupId is { } id ? await s.Families.GetAsync(id) : null;
        HasGroup = _group is not null;
        if (_group is null)
        {
            return;
        }

        var me = s.Session.MemberId;
        GroupName = _group.Name;
        IsAdmin = _group.IsAdmin(me);
        Currency = _group.Currency;
        foreach (var member in _group.ActiveMembers.OrderByDescending(m => m.MemberId == me).ThenBy(m => m.DisplayName))
        {
            var role = member.Role == MemberRole.Admin ? "Admin" : "Member";
            string detail;
            if (member.MemberId == me)
            {
                detail = $"{role} · this phone";
            }
            else
            {
                var seen = await s.Sync.LastSeenAsync(_group.Id, member.MemberId);
                detail = seen is { } t ? $"{role} · last seen {Display.Ago(s.Session.Now - t)}" : role;
            }

            Members.Add(new MemberRow(member.MemberId, member.DisplayName, detail, IsAdmin && member.MemberId != me));
        }

        foreach (var join in await s.Sync.PendingJoinsAsync(_group.Id))
        {
            Pending.Add(new PendingJoinRow(join.Member.MemberId, join.Member.DisplayName, join.SafetyCode));
        }

        var last = await s.Sync.LastSyncAsync(_group.Id);
        Status = await s.Sync.IsRemovedAsync(_group.Id) ? "You were removed from this group."
            : !FamilySync.Admitted(_group) ? "Waiting for the person who invited you to let you in…"
            : last is { } l ? $"Synced {Display.Ago(s.Session.Now - l)}"
            : "Not synced yet";
    }

    partial void OnCurrencyChanged(string value)
    {
        if (_group is null || !IsAdmin || value == _group.Currency || string.IsNullOrEmpty(value))
        {
            return;
        }

        _group.SetCurrency(value, host.Services.Session.MemberId);
        _ = SaveGroupAsync();
    }

    private async Task SaveGroupAsync()
    {
        host.Services.Session.Stamp(_group!);
        await host.Services.Families.SaveAsync(_group!);
        await host.Services.Events.PublishAsync(new VaultChange(_group!.Id, RecordTypes.Family, _group.Id));
    }

    [RelayCommand]
    private async Task InviteInPersonAsync()
    {
        if (_group is null)
        {
            return;
        }

        var invite = await host.Services.Sync.CreateInPersonInviteAsync(_group.Id);
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(InviteCodec.Encode(invite), QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data).GetGraphic(12);
        InviteQr = ImageSource.FromStream(() => new MemoryStream(png));
        QrExpiry = $"Works until {invite.ExpiresAt.ToLocalTime():HH:mm}. Don't photograph or share it: it holds the group's key.";
        ShowQr = true;
    }

    [RelayCommand]
    private void HideQr()
    {
        ShowQr = false;
        InviteQr = null;
    }

    [RelayCommand]
    private async Task InviteByMessageAsync()
    {
        if (_group is null)
        {
            return;
        }

        var invite = await host.Services.Sync.CreateRemoteInviteAsync(_group.Id);
        await Share.Default.RequestAsync(new ShareTextRequest(InviteCodec.ShareText(invite), "Invite to " + _group.Name));
        await dialogs.AlertAsync("Invite sent", "When they paste it into Family Lists, their request appears here with a 6-digit code. Call them and check you both see the same code before you approve.");
    }

    [RelayCommand]
    private async Task ApproveAsync(PendingJoinRow row)
    {
        if (_group is null || !await dialogs.ConfirmAsync("Let them in?", $"Only approve if {row.Name} sees the same code: {row.SafetyCode}", "Approve"))
        {
            return;
        }

        IsBusy = true;
        try
        {
            await host.Services.Sync.ApproveJoinAsync(_group.Id, row.MemberId);
            Dialogs.Toast($"{row.Name} can now see this group's lists.");
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or FamilyRuleException)
        {
            await dialogs.AlertAsync("Approve", ex is HttpRequestException ? "No internet connection. Try again." : ex.Message);
        }
        finally
        {
            IsBusy = false;
        }

        await LoadAsync();
    }

    [RelayCommand]
    private async Task RejectAsync(PendingJoinRow row)
    {
        if (_group is not null)
        {
            await host.Services.Sync.RejectJoinAsync(_group.Id, row.MemberId);
            await LoadAsync();
        }
    }

    [RelayCommand]
    private async Task ManageAsync(MemberRow row)
    {
        if (_group is null)
        {
            return;
        }

        const string admin = "Make admin", remove = "Remove from group";
        var choice = await dialogs.ChooseAsync(row.Name, admin, remove);
        try
        {
            if (choice == admin)
            {
                _group.SetRole(row.MemberId, MemberRole.Admin, host.Services.Session.MemberId);
                await SaveGroupAsync();
            }
            else if (choice == remove && await dialogs.ConfirmAsync(remove, $"Remove {row.Name}? Their phone gets nothing new from now on (the group key changes). What it already has stays on it.", "Remove"))
            {
                IsBusy = true;
                await host.Services.Sync.RemoveMemberAsync(_group.Id, row.MemberId);
            }
        }
        catch (Exception ex) when (ex is FamilyRuleException or HttpRequestException)
        {
            await dialogs.AlertAsync(row.Name, ex is HttpRequestException ? "No internet connection. Try again." : ex.Message);
        }
        finally
        {
            IsBusy = false;
        }

        await LoadAsync();
    }

    [RelayCommand]
    private async Task RenameAsync()
    {
        if (_group is not null && await dialogs.PromptAsync("Rename group", "New name", _group.Name, maxLength: 60) is { Length: > 0 } name)
        {
            try
            {
                _group.Rename(name, host.Services.Session.MemberId);
                await SaveGroupAsync();
            }
            catch (FamilyRuleException ex)
            {
                await dialogs.AlertAsync("Rename group", ex.Message);
            }

            await LoadAsync();
        }
    }

    [RelayCommand]
    private async Task LeaveAsync()
    {
        if (_group is null || !await dialogs.ConfirmAsync("Leave group", $"Leave \"{_group.Name}\"? Its lists are deleted from this phone. Someone has to invite you again to come back.", "Leave"))
        {
            return;
        }

        try
        {
            await host.Services.Sync.LeaveFamilyAsync(_group.Id);
            settings.CurrentGroupId = null;
            await LoadAsync();
        }
        catch (FamilyRuleException ex)
        {
            await dialogs.AlertAsync("Leave group", ex.Message);
        }
    }

    [RelayCommand]
    private async Task CreateAnotherAsync()
    {
        if (await dialogs.PromptAsync("New group", "For example \"Parents' house\". Each group's lists are separate.", maxLength: 60) is { Length: > 0 } name)
        {
            var group = await host.Services.Sync.CreateFamilyAsync(name.Trim());
            settings.CurrentGroupId = group.Id;
            await LoadAsync();
        }
    }

    [RelayCommand]
    private Task JoinAsync() => Nav.GoAsync("join");

    [RelayCommand]
    private async Task SyncNowAsync()
    {
        IsBusy = true;
        try
        {
            var report = await scheduler.SyncNowAsync();
            Dialogs.Toast(report.Errors.Count > 0 ? report.Errors[0] : report.Received == 0 ? "Up to date." : $"Got {report.Received} changes.");
        }
        finally
        {
            IsBusy = false;
        }

        await LoadAsync();
    }
}
