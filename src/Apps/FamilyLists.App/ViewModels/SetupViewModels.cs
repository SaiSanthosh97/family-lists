using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FamilyLists.App.Services;

namespace FamilyLists.App.ViewModels;

/// <summary>Decides where the app starts: first-launch setup, or the lists.</summary>
public sealed class StartViewModel(VaultHost host, UpdateService updates)
{
    public async Task RouteAsync()
    {
        if (!host.IsSetUp)
        {
            await Nav.GoAsync("//welcome");
            return;
        }

        if (!await host.EnsureOpenAsync())
        {
            // The Keystore key is gone (factory reset, app data copied): start over and rejoin the group.
            await Nav.GoAsync("//welcome?lost=1");
            return;
        }

        await Nav.HomeAsync();
        ((App)Application.Current!).AfterOpen();
        _ = updates.OfferIfDueAsync();
    }
}

/// <summary>
/// First launch: "your name", and either a first group for the household or "I was invited". There are no accounts:
/// this phone gets its own keys, and a new phone gets its lists back by joining the group again.
/// </summary>
public sealed partial class WelcomeViewModel(VaultHost host, AppSettings settings, Dialogs dialogs) : ObservableObject, IQueryAttributable
{
    [ObservableProperty]
    public partial string Name { get; set; } = "";

    [ObservableProperty]
    public partial string GroupName { get; set; } = "Home";

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial bool KeyLost { get; set; }

    public void ApplyQueryAttributes(IDictionary<string, object> query) => KeyLost = query.ContainsKey("lost");

    [RelayCommand]
    private Task StartAsync() => SetUpAsync(createGroup: true);

    [RelayCommand]
    private Task InvitedAsync() => SetUpAsync(createGroup: false);

    private async Task SetUpAsync(bool createGroup)
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            await dialogs.AlertAsync("Welcome", "Please type your name first. The people in your group see it.");
            return;
        }

        if (createGroup && string.IsNullOrWhiteSpace(GroupName))
        {
            await dialogs.AlertAsync("Welcome", "Please give your group a name, for example \"Home\".");
            return;
        }

        if (KeyLost && !await dialogs.ConfirmAsync("Start over?", "This phone's lists can't be opened any more. Start again and join your group again to get them back.", "Start over"))
        {
            return;
        }

        IsBusy = true;
        try
        {
            if (KeyLost)
            {
                host.EraseEverything();
            }

            await host.CreateAsync(Name.Trim());
            if (createGroup)
            {
                var group = await host.Services.Sync.CreateFamilyAsync(GroupName.Trim());
                settings.CurrentGroupId = group.Id;
            }

            await Nav.HomeAsync();
            ((App)Application.Current!).AfterOpen();
            if (!createGroup)
            {
                await Nav.GoAsync("join");
            }
        }
        catch (Exception ex) when (ex is Core.Families.FamilyRuleException or ArgumentException)
        {
            await dialogs.AlertAsync("Welcome", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
