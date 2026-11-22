using FamilyLists.Core.ClaudeReader;
using FamilyLists.Core.Receipts;
using FamilyLists.Device.Vision;

namespace FamilyLists.App.Services;

/// <summary>
/// Getting a bill into the app: take a photo, choose one, or choose a PDF; then read it (on the phone with ML Kit and
/// the receipt rules, or with Claude when the person turned that on and added their API key) into a draft bill.
/// Photos are shrunk and turned upright first (a 12 MP photo becomes ~300 KB); the kept copy is encrypted.
/// </summary>
public sealed class BillReading(VaultHost host, AppSettings settings, Dialogs dialogs)
{
    private const int MaxPdfPages = 3;

    private static string Temp => Path.Combine(FileSystem.CacheDirectory, "bills");

    /// <summary>Asks where the bill comes from. Null when cancelled.</summary>
    public async Task<(byte[] Bytes, string? Name)?> PickAsync()
    {
        const string camera = "Take a photo", gallery = "Choose a photo", file = "Choose a PDF or image file";
        var choice = await dialogs.ChooseAsync("Add a bill", camera, gallery, file);
        try
        {
            FileResult? result = choice switch
            {
                camera => await MediaPicker.Default.CapturePhotoAsync(),
                gallery => (await MediaPicker.Default.PickPhotosAsync(new MediaPickerOptions { SelectionLimit = 1 }))?.FirstOrDefault(),
                file => await FilePicker.Default.PickAsync(new PickOptions
                {
                    PickerTitle = "Choose the bill",
                    FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                    {
                        [DevicePlatform.Android] = ["application/pdf", "image/*"],
                    }),
                }),
                _ => null,
            };
            if (result is null)
            {
                return null;
            }

            await using var stream = await result.OpenReadAsync();
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            return buffer.Length is > 0 and <= 25 * 1024 * 1024 ? (buffer.ToArray(), result.FileName) : null;
        }
        catch (PermissionException)
        {
            await dialogs.AlertAsync("Add a bill", "Family Lists needs permission to use the camera or your photos. You can allow it in Android settings.");
            return null;
        }
    }

    /// <summary>Reads the bill into a draft for <paramref name="monthId"/>. Returns the bill id, or null when it couldn't be read.</summary>
    public async Task<string?> ReadIntoDraftAsync(string monthId, byte[] bytes, CancellationToken ct = default)
    {
        var s = host.Services;
        var mediaType = MediaType(bytes);
        if (mediaType is null)
        {
            await dialogs.AlertAsync("Add a bill", "That file isn't a photo or a PDF.");
            return null;
        }

        var month = await s.ListRepository.GetMonthAsync(monthId, ct) ?? throw new InvalidOperationException("Month not found.");
        var names = (await s.Lists.ItemsAsync(month.ListId, month.Id, ct)).Select(i => i.Name).ToList();
        var keep = mediaType == "application/pdf" ? bytes : Shrink(bytes, 1600, 82) ?? bytes;

        ReceiptDraft? draft = null;
        if (settings.UseClaude && await s.ClaudeKeyAsync() is { } key)
        {
            try
            {
                draft = await new ClaudeReceiptReader(key).ReadAsync(keep, mediaType == "application/pdf" ? mediaType : "image/jpeg", names, ct);
            }
            catch (ClaudeReaderException ex)
            {
                if (!await dialogs.ConfirmAsync("Claude couldn't read the bill", ex.Message + "\n\nRead it on the phone instead?", "Read on phone"))
                {
                    return null;
                }
            }
        }

        draft ??= await ReadOnPhoneAsync(bytes, mediaType, ct);
        var bill = await s.Bills.CreateDraftAsync(monthId, draft, keep, ct);
        return bill.Id;
    }

    private async Task<ReceiptDraft> ReadOnPhoneAsync(byte[] bytes, string mediaType, CancellationToken ct)
    {
        var s = host.Services;
        var pages = mediaType == "application/pdf"
            ? await s.PdfRasterizer.RenderPagesAsync(bytes, MaxPdfPages, ct)
            : [Shrink(bytes, 2400, 90) ?? bytes];

        var words = new List<LayoutWord>();
        var text = new List<string>();
        for (var i = 0; i < pages.Count; i++)
        {
            var read = await s.TextRecognizer.ReadWordsAsync(pages[i], ct);
            words.AddRange(read.Words.Select(w => w with { Page = i }));
            text.Add(read.Text);
        }

        return ReceiptParser.Parse(new RecognizedText(string.Join('\n', text), words));
    }

    /// <summary>A smaller, upright JPEG copy of a photo (Use It Before's PhotoShrinker), or null if it isn't a readable image.</summary>
    private static byte[]? Shrink(byte[] image, int maxSide, int quality)
    {
        Directory.CreateDirectory(Temp);
        var source = Path.Combine(Temp, Guid.NewGuid().ToString("N"));
        var target = source + ".jpg";
        try
        {
            File.WriteAllBytes(source, image);
            return PhotoShrinker.SaveAsJpeg(source, target, maxSide, quality) ? File.ReadAllBytes(target) : null;
        }
        finally
        {
            File.Delete(source);
            File.Delete(target);
        }
    }

    /// <summary>The real file type from its first bytes (never from the name).</summary>
    public static string? MediaType(byte[] b) => b switch
    {
        [0x25, 0x50, 0x44, 0x46, ..] => "application/pdf",
        [0xFF, 0xD8, 0xFF, ..] => "image/jpeg",
        [0x89, 0x50, 0x4E, 0x47, ..] => "image/png",
        [0x52, 0x49, 0x46, 0x46, _, _, _, _, 0x57, 0x45, 0x42, 0x50, ..] => "image/webp",
        [_, _, _, _, 0x66, 0x74, 0x79, 0x70, ..] => "image/heic",
        _ => null,
    };

    /// <summary>Opens a bill photo/PDF in the phone's viewer. The temporary copy is deleted on the next start.</summary>
    public static async Task ViewAsync(byte[] bytes)
    {
        var folder = Path.Combine(FileSystem.CacheDirectory, "view");
        Directory.CreateDirectory(folder);
        var type = MediaType(bytes) ?? "image/jpeg";
        var path = Path.Combine(folder, "bill" + (type == "application/pdf" ? ".pdf" : ".jpg"));
        await File.WriteAllBytesAsync(path, bytes);
        await Launcher.Default.OpenAsync(new OpenFileRequest("Bill", new ReadOnlyFile(path, type)));
    }

    /// <summary>Deletes temporary copies left from viewing or reading bills.</summary>
    public static void CleanUp()
    {
        foreach (var folder in new[] { Temp, Path.Combine(FileSystem.CacheDirectory, "view") })
        {
            try
            {
                if (Directory.Exists(folder))
                {
                    Directory.Delete(folder, recursive: true);
                }
            }
            catch (IOException)
            {
            }
        }
    }
}
