using System.Globalization;
using FamilyLists.Core.Bills;

namespace FamilyLists.App.Services;

/// <summary>Pop-up dialogs, so view models don't depend on pages (same pattern as Use It Before and Travel Vault).</summary>
public sealed class Dialogs
{
    private static Page Page => Shell.Current?.CurrentPage ?? Application.Current!.Windows[0].Page!;

    public Task AlertAsync(string title, string message) => Page.DisplayAlertAsync(title, message, "OK");

    public Task<bool> ConfirmAsync(string title, string message, string accept, string? cancel = null) =>
        Page.DisplayAlertAsync(title, message, accept, cancel ?? "Cancel");

    /// <returns>The typed text, or null when cancelled.</returns>
    public Task<string?> PromptAsync(string title, string message, string? initialValue = null, Keyboard? keyboard = null, int maxLength = 200) =>
        Page.DisplayPromptAsync(title, message, "OK", "Cancel", initialValue: initialValue ?? "", maxLength: maxLength, keyboard: keyboard ?? Keyboard.Default);

    /// <returns>The chosen option, or null when cancelled.</returns>
    public async Task<string?> ChooseAsync(string title, params string[] options)
    {
        var choice = await Page.DisplayActionSheetAsync(title, "Cancel", null, options);
        return choice is null || choice == "Cancel" ? null : choice;
    }

    /// <summary>A short message at the bottom of the screen (Android toast).</summary>
    public static void Toast(string text) => MainThread.BeginInvokeOnMainThread(() =>
        Android.Widget.Toast.MakeText(Platform.AppContext, text, Android.Widget.ToastLength.Long)?.Show());
}

/// <summary>Shell navigation in one place.</summary>
public static class Nav
{
    public static Task GoAsync(string route) => MainThread.InvokeOnMainThreadAsync(() => Shell.Current.GoToAsync(route));

    public static Task BackAsync() => MainThread.InvokeOnMainThreadAsync(() => Shell.Current.GoToAsync(".."));

    public static Task HomeAsync() => GoAsync("//main/lists");

    public static Task MonthAsync(string listId, string? period = null) =>
        GoAsync($"month?list={Uri.EscapeDataString(listId)}" + (period is null ? "" : $"&period={period}"));

    public static Task TodoAsync(string listId) => GoAsync($"todo?list={Uri.EscapeDataString(listId)}");

    public static Task ItemAsync(string itemId) => GoAsync($"item?id={Uri.EscapeDataString(itemId)}");

    public static Task BillAsync(string billId) => GoAsync($"bill?id={Uri.EscapeDataString(billId)}");

    private static bool _openingImport;

    /// <summary>
    /// Opens Import for a shared note, once: both the share itself and the Lists screen (after a cold start) ask.
    /// If Import is already showing, it takes the new note instead.
    /// </summary>
    public static Task ImportAsync() => MainThread.InvokeOnMainThreadAsync(async () =>
    {
        if (Shell.Current?.CurrentPage is Views.ImportPage { BindingContext: ViewModels.ImportViewModel open })
        {
            open.TakeShared();
            return;
        }

        if (_openingImport || Shell.Current is null)
        {
            return;
        }

        _openingImport = true;
        try
        {
            await Shell.Current.GoToAsync("import");
        }
        finally
        {
            _openingImport = false;
        }
    });

    public static Task SpendingAsync(string listId, string? period = null) =>
        GoAsync($"spending?list={Uri.EscapeDataString(listId)}" + (period is null ? "" : $"&period={period}"));
}

/// <summary>Text helpers for screens.</summary>
public static class Display
{
    public static string Ago(TimeSpan span) => span.TotalMinutes switch
    {
        < 1 => "just now",
        < 60 => $"{(int)span.TotalMinutes} min ago",
        < 60 * 24 => $"{(int)span.TotalHours} h ago",
        _ => $"{(int)span.TotalDays} days ago",
    };

    public static string Money(decimal? amount, string currency) => Core.Bills.Money.Format(amount, currency);

    /// <summary>"×3", "1.5 kg", or "" for one of something.</summary>
    public static string Qty(decimal qty, string? unit) =>
        unit is { Length: > 0 } ? $"{qty.ToString("0.###", CultureInfo.CurrentCulture)} {unit}"
        : qty == 1 ? ""
        : "×" + qty.ToString("0.###", CultureInfo.CurrentCulture);

    /// <summary>"3 onions" or "onions x3" → ("onions", 3).</summary>
    public static (string Name, decimal Qty) ParseQuickAdd(string text)
    {
        var raw = text.Trim();
        var lead = System.Text.RegularExpressions.Regex.Match(raw, @"^(\d+(?:\.\d+)?)\s+(.+)$");
        if (lead.Success && decimal.TryParse(lead.Groups[1].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var q1))
        {
            return (lead.Groups[2].Value.Trim(), q1);
        }

        var trail = System.Text.RegularExpressions.Regex.Match(raw, @"^(.+?)\s*[x×]\s*(\d+(?:\.\d+)?)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (trail.Success && decimal.TryParse(trail.Groups[2].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var q2))
        {
            return (trail.Groups[1].Value.Trim(), q2);
        }

        return (raw, 1);
    }

    public static string SpendSourceText(SpendSource source, int bills) => source switch
    {
        SpendSource.Bills => bills == 1 ? "from 1 bill" : $"from {bills} bills",
        SpendSource.Manual => "entered by hand",
        SpendSource.Items => "from item prices",
        _ => "no total yet",
    };
}
