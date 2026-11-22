using FamilyLists.Core.AppUpdater;

namespace FamilyLists.App.Services;

/// <summary>
/// Free distribution: the APK is published on GitHub Releases; this checks for a newer one (at most daily, on Wi-Fi),
/// downloads it verified against GitHub's SHA-256, and hands it to Android's installer. From Travel Vault.
/// </summary>
public sealed class UpdateService(HttpClient http, AppSettings settings, Dialogs dialogs)
{
    /// <summary>The GitHub repository the releases come from (must stay public: the app reads it without a login).</summary>
    public const string Owner = "SaiSanthosh97";
    public const string Repository = "family-lists";
    public static readonly Uri RepositoryUrl = new($"https://github.com/{Owner}/{Repository}");

    private const string ApkMimeType = "application/vnd.android.package-archive";
    private readonly GitHubReleaseChecker _checker = new(http, Owner, Repository, ".apk");

    public static Version CurrentVersion => AppInfo.Current.Version;

    private static string DownloadFolder => Path.Combine(FileSystem.CacheDirectory, "updates");

    private static bool OnWifi =>
        Connectivity.Current.NetworkAccess == NetworkAccess.Internet && Connectivity.Current.ConnectionProfiles.Contains(ConnectionProfile.WiFi);

    public async Task<AvailableUpdate?> CheckNowAsync()
    {
        var update = await _checker.CheckAsync(CurrentVersion);
        settings.LastUpdateCheck = DateTimeOffset.UtcNow;
        return update;
    }

    /// <summary>When the app opens: at most once a day, on Wi-Fi, quietly.</summary>
    public async Task OfferIfDueAsync()
    {
        if (!OnWifi || settings.LastUpdateCheck is { } last && DateTimeOffset.UtcNow - last < TimeSpan.FromDays(1))
        {
            return;
        }

        try
        {
            if (await CheckNowAsync() is { } update)
            {
                await OfferAsync(update);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
        }
    }

    public async Task OfferAsync(AvailableUpdate update)
    {
        if (!await dialogs.ConfirmAsync("Update", $"Family Lists {update.Version.ToString(3)} is available. Install it now?", "Install", "Later"))
        {
            return;
        }

        if (Platform.AppContext.PackageManager?.CanRequestPackageInstalls() != true)
        {
            await dialogs.AlertAsync("Update", "Android will ask you to allow Family Lists to install updates. Turn it on, come back, and tap Install again.");
            var intent = new Android.Content.Intent(Android.Provider.Settings.ActionManageUnknownAppSources, Android.Net.Uri.Parse($"package:{AppInfo.Current.PackageName}"));
            intent.AddFlags(Android.Content.ActivityFlags.NewTask);
            Platform.AppContext.StartActivity(intent);
            return;
        }

        try
        {
            if (Directory.Exists(DownloadFolder))
            {
                Directory.Delete(DownloadFolder, recursive: true);
            }

            Directory.CreateDirectory(DownloadFolder);
            var path = Path.Combine(DownloadFolder, $"family-lists-{update.Version.ToString(3)}.apk");
            await new UpdateDownloader(http).DownloadAsync(update, path);
            await Launcher.Default.OpenAsync(new OpenFileRequest("Update", new ReadOnlyFile(path, ApkMimeType)));
        }
        catch (DamagedDownloadException)
        {
            await dialogs.AlertAsync("Update", "The download was damaged, so it wasn't installed. Try again later.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            await dialogs.AlertAsync("Update", "The update couldn't be downloaded. Check the internet and try again.");
        }
    }
}
