using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using FamilyLists.App.Services;
using FamilyLists.Device.Files;

namespace FamilyLists.App;

/// <summary>
/// The only activity. It is also the Android share target: a bill photo or PDF shared from WhatsApp, Gmail or a file
/// manager ("Share → Family Lists") arrives here. It's read at once (the sender's permission is temporary) and waits
/// in <see cref="Inbox"/> until a month's list is open, which then offers to add it.
/// </summary>
[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTask, Exported = true,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
[IntentFilter([Intent.ActionSend], Categories = [Intent.CategoryDefault], DataMimeType = "application/pdf")]
[IntentFilter([Intent.ActionSend], Categories = [Intent.CategoryDefault], DataMimeType = "image/*")]
[IntentFilter([Intent.ActionSend], Categories = [Intent.CategoryDefault], DataMimeType = "text/plain")]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        _ = HandleAsync(Intent);
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        _ = HandleAsync(intent);
    }

    private static async Task HandleAsync(Intent? intent)
    {
        if (intent is null || !SharedContent.IsShare(intent))
        {
            return;
        }

        var items = await SharedContent.ReadAsync(intent);
        intent.SetAction(Intent.ActionMain); // don't take the same share again after a rotation

        // A note from Google Keep / Samsung Notes: shared text, or a .txt file ("Share as text file").
        if (items.FirstOrDefault(i => i.IsText || LooksLikeText(i)) is { } note)
        {
            Inbox.PutNote(System.Text.Encoding.UTF8.GetString(note.Content).TrimStart('﻿'));
            OpenImportIfReady();
            return;
        }

        if (items.FirstOrDefault(i => !i.IsText) is { } file)
        {
            Inbox.Put(file.Content, file.FileName);
        }
    }

    /// <summary>A shared file that is plain text (not a photo or PDF): a .txt name, or bytes with no binary content.</summary>
    private static bool LooksLikeText(SharedItem item)
    {
        if (item.Content.Length is 0 or > 1_000_000 || BillReading.MediaType(item.Content) is not null)
        {
            return false;
        }

        return item.FileName?.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) == true || !item.Content.AsSpan(0, Math.Min(item.Content.Length, 4096)).Contains((byte)0);
    }

    /// <summary>When the app is already open, go straight to Import; otherwise the Lists screen does it once ready.</summary>
    private static void OpenImportIfReady() => MainThread.BeginInvokeOnMainThread(async () =>
    {
        var host = IPlatformApplication.Current?.Services.GetService<VaultHost>();
        if (host?.IsOpen == true && Shell.Current?.CurrentState.Location.OriginalString.Contains("main", StringComparison.Ordinal) == true)
        {
            await Nav.ImportAsync();
        }
    });
}
