namespace FamilyLists.Core.Common;

/// <summary>Source of the current time. Tests use a fixed clock so time-based rules are repeatable.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public static readonly SystemClock Instance = new();

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

/// <summary>A clock that only moves when told to. For tests.</summary>
public sealed class ManualClock(DateTimeOffset start) : IClock
{
    public DateTimeOffset UtcNow { get; private set; } = start;

    public void Advance(TimeSpan by) => UtcNow += by;

    public void Set(DateTimeOffset to) => UtcNow = to;
}

/// <summary>New record ids: time-ordered GUIDs (version 7), so ids created later sort later in indexes.</summary>
public static class Ids
{
    public static string New() => Guid.CreateVersion7().ToString("N");

    /// <summary>
    /// The same id on every phone for the same inputs (a SHA-256 of the parts). Used where two phones may create
    /// "the same" record independently, e.g. both start December's list while offline: the copies then converge
    /// into one record instead of becoming duplicates.
    /// </summary>
    public static string Derive(params string[] parts)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(string.Join('\u001f', parts)));
        return Convert.ToHexStringLower(bytes)[..32];
    }

    public static bool IsValid(string? id) =>
        id is { Length: 32 } && id.All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f'));
}
