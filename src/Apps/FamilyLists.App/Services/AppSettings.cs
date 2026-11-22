namespace FamilyLists.App.Services;

/// <summary>
/// UI preferences (Android SharedPreferences). Only non-sensitive values live here: which group is open, switches,
/// update-check time. Lists, bills, keys and the Claude API key are in the encrypted database.
/// </summary>
public sealed class AppSettings
{
    private static IPreferences P => Preferences.Default;

    public string? CurrentGroupId
    {
        get => P.Get<string?>("group.current", null);
        set => P.Set("group.current", value);
    }

    /// <summary>Read bills with Claude (needs an API key) instead of on the phone.</summary>
    public bool UseClaude
    {
        get => P.Get("bills.claude", false);
        set => P.Set("bills.claude", value);
    }

    public DateTimeOffset? LastUpdateCheck
    {
        get => P.Get("update.lastCheck", 0L) is var ms and > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(ms) : null;
        set => P.Set("update.lastCheck", value?.ToUnixTimeMilliseconds() ?? 0L);
    }

    public void Clear() => P.Clear();
}
