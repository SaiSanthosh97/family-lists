using System.Globalization;
using FamilyLists.App.Services;
using FamilyLists.Core.Vault;

namespace FamilyLists.App.Views;

/// <summary>A view model that loads its data when its page appears.</summary>
public interface ILoads
{
    Task LoadAsync();
}

/// <summary>
/// Base page (from Travel Vault): loads its view model when shown, and reloads it when something changes while the
/// page is visible (another phone ticked an item, a sync brought a bill).
/// </summary>
public class VaultPage : ContentPage
{
    private bool _visible;
    private CancellationTokenSource? _reload;

    protected static VaultHost Host => IPlatformApplication.Current!.Services.GetRequiredService<VaultHost>();

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _visible = true;
        Host.Changed += OnVaultChanged;
        await SafeLoadAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _visible = false;
        Host.Changed -= OnVaultChanged;
    }

    /// <summary>
    /// OnAppearing is "async void": an exception there would crash the whole app. A screen that fails to load shows a
    /// message instead and is logged, so the rest of the app keeps working.
    /// </summary>
    private async Task SafeLoadAsync()
    {
        try
        {
            if (BindingContext is ILoads loads && Host.IsOpen)
            {
                await loads.LoadAsync();
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Android.Util.Log.Error("FamilyLists", $"{GetType().Name} failed to load: {ex}");
            Dialogs.Toast("Something went wrong showing this screen.");
        }
    }

    /// <summary>A sync often brings many records at once: reload once they've settled.</summary>
    private async void OnVaultChanged(VaultChange change)
    {
        if (!_visible || !change.FromSync)
        {
            return;
        }

        _reload?.Cancel();
        var cts = _reload = new CancellationTokenSource();
        try
        {
            await Task.Delay(250, cts.Token);
            await SafeLoadAsync();
        }
        catch (TaskCanceledException)
        {
        }
    }
}

/// <summary>True for a non-empty string (show a label only when it has text).</summary>
public sealed class NotEmptyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is string { Length: > 0 };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Inverts a bool.</summary>
public sealed class NotConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
}

/// <summary>True when a count is above zero (show a section only when it has items).</summary>
public sealed class AnyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is int and > 0;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Strike through text when true (an item in the cart, a done task).</summary>
public sealed class StrikeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? TextDecorations.Strikethrough : TextDecorations.None;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
