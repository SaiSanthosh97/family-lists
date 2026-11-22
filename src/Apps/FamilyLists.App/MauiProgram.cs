using FamilyLists.App.Services;
using FamilyLists.App.ViewModels;
using FamilyLists.App.Views;
using FamilyLists.Device.BackgroundWork;
using Microsoft.Extensions.Logging;

namespace FamilyLists.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

#if DEBUG
        builder.Logging.AddDebug();
#endif

        var services = builder.Services;
        services.AddSingleton(_ => new HttpClient { Timeout = TimeSpan.FromSeconds(30) });
        services.AddSingleton<AppSettings>();
        services.AddSingleton<VaultHost>();
        services.AddSingleton<SyncScheduler>();
        services.AddSingleton<UpdateService>();
        services.AddSingleton<BillReading>();
        services.AddSingleton<Dialogs>();

        // Screens (view models are transient: each visit starts fresh; the tabs are singletons).
        services.AddTransient<StartViewModel>();
        services.AddTransient<StartPage>();
        services.AddTransient<WelcomeViewModel>();
        services.AddTransient<WelcomePage>();
        services.AddSingleton<ListsViewModel>();
        services.AddSingleton<ListsPage>();
        services.AddSingleton<GroupViewModel>();
        services.AddSingleton<GroupPage>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<SettingsPage>();
        services.AddTransient<MonthViewModel>();
        services.AddTransient<MonthPage>();
        services.AddTransient<TodoViewModel>();
        services.AddTransient<TodoPage>();
        services.AddTransient<ItemViewModel>();
        services.AddTransient<ItemPage>();
        services.AddTransient<BillViewModel>();
        services.AddTransient<BillPage>();
        services.AddTransient<SpendingViewModel>();
        services.AddTransient<SpendingPage>();
        services.AddTransient<JoinViewModel>();
        services.AddTransient<JoinPage>();

        var app = builder.Build();
        BillReading.CleanUp();

        // Runs also when Android starts the app without a screen (the background job, after a restart).
        var scheduler = app.Services.GetRequiredService<SyncScheduler>();
        scheduler.Start();
        BackgroundWork.Initialize(scheduler.BackgroundAsync);
        return app;
    }
}
