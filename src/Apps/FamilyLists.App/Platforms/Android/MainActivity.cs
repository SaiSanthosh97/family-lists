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
        if (items.FirstOrDefault(i => !i.IsText) is { } file)
        {
            Inbox.Put(file.Content, file.FileName);
        }
    }
}
