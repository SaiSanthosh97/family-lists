using FamilyLists.App.Services;

namespace FamilyLists.App;

/// <summary>App lifecycle: live sync while the app is on screen, quiet when it isn't (background sync takes over).</summary>
public partial class App : Application
{
    private readonly VaultHost _host;
    private readonly SyncScheduler _scheduler;

    public App(VaultHost host, SyncScheduler scheduler)
    {
        InitializeComponent();
        _host = host;
        _scheduler = scheduler;
        host.Notice += Dialogs.Toast;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new AppShell());
        window.Stopped += (_, _) => _scheduler.SetForeground(false);
        window.Resumed += (_, _) => AfterOpen();
        return window;
    }

    /// <summary>Called once the data is open and the main screens are showing.</summary>
    public void AfterOpen()
    {
        if (_host.IsOpen)
        {
            _scheduler.SetForeground(true);
        }
    }
}
