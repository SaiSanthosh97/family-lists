using FamilyLists.App.ViewModels;
using FamilyLists.App.Services;
using FamilyLists.Core.Sync;

namespace FamilyLists.App.Views;

public partial class StartPage : ContentPage
{
    private readonly StartViewModel _viewModel;
    private bool _routed;

    public StartPage(StartViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!_routed)
        {
            _routed = true;
            await _viewModel.RouteAsync();
        }
    }
}

public partial class WelcomePage : ContentPage
{
    public WelcomePage(WelcomeViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}

public partial class ListsPage : VaultPage
{
    public ListsPage(ListsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}

public partial class GroupPage : VaultPage
{
    public GroupPage(GroupViewModel viewModel, SyncScheduler scheduler)
    {
        InitializeComponent();
        BindingContext = viewModel;
        // "Synced … ago" and members' last-seen times change with every sync, even one that brings nothing new.
        Appearing += (_, _) => scheduler.Synced += OnSynced;
        Disappearing += (_, _) => scheduler.Synced -= OnSynced;
    }

    private async void OnSynced(SyncReport report)
    {
        try
        {
            await ((GroupViewModel)BindingContext).LoadAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Android.Util.Log.Warn("FamilyLists", $"Group page refresh failed: {ex.Message}");
        }
    }
}

public partial class SettingsPage : VaultPage
{
    public SettingsPage(SettingsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}

public partial class MonthPage : VaultPage
{
    public MonthPage(MonthViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}

public partial class TodoPage : VaultPage
{
    public TodoPage(TodoViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}

public partial class ItemPage : VaultPage
{
    public ItemPage(ItemViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}

public partial class BillPage : VaultPage
{
    public BillPage(BillViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}

public partial class SpendingPage : VaultPage
{
    public SpendingPage(SpendingViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}

public partial class JoinPage : ContentPage
{
    public JoinPage(JoinViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}

public partial class ImportPage : VaultPage
{
    public ImportPage(ImportViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
