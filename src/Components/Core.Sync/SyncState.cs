using System.Text.Json;
using FamilyLists.Core.Common;
using FamilyLists.Core.Vault;

namespace FamilyLists.Core.Sync;

public sealed record OutboxItem(long Id, string FamilyId, string Kind, string Payload);

/// <summary>
/// Sync bookkeeping (implemented in Storage.Sqlite, inside the encrypted database):
/// the change log holds the latest signed change per record (what this phone republishes when asked),
/// the outbox holds local changes not yet delivered to the relay.
/// </summary>
public interface ISyncStore
{
    /// <summary>Keeps the change if it is newer than the stored one for the same record.</summary>
    Task SaveChangeAsync(SignedChange change, CancellationToken cancellationToken = default);

    Task<SignedChange?> GetChangeAsync(string type, string id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SignedChange>> ListChangesAsync(string familyId, CancellationToken cancellationToken = default);

    Task EnqueueAsync(string familyId, string kind, string payload, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OutboxItem>> OutboxAsync(string familyId, CancellationToken cancellationToken = default);

    Task RemoveOutboxAsync(IEnumerable<long> ids, CancellationToken cancellationToken = default);

    Task DeleteFamilyAsync(string familyId, CancellationToken cancellationToken = default);
}

/// <summary>Tunable numbers.</summary>
public sealed record SyncOptions
{
    /// <summary>ntfy.sh keeps messages ~12 h; after 11 h away, ask the family to republish.</summary>
    public TimeSpan AwayThreshold { get; init; } = TimeSpan.FromHours(11);

    public TimeSpan CheckInEvery { get; init; } = TimeSpan.FromHours(6);

    /// <summary>After a key rotation, keep reading the old topic this long for stragglers.</summary>
    public TimeSpan OldTopicKeptFor { get; init; } = TimeSpan.FromDays(3);
}

/// <summary>Typed helpers over the settings store for sync bookmarks.</summary>
internal sealed class SyncSettings(ISettingsStore settings)
{
    public async Task<T?> GetJsonAsync<T>(string key, CancellationToken ct)
    {
        var text = await settings.GetAsync(key, ct);
        return text is null ? default : JsonSerializer.Deserialize<T>(text, JsonDefaults.Options);
    }

    public Task SetJsonAsync<T>(string key, T value, CancellationToken ct) =>
        settings.SetAsync(key, JsonSerializer.Serialize(value, JsonDefaults.Options), ct);

    public async Task<DateTimeOffset?> GetTimeAsync(string key, CancellationToken ct) =>
        DateTimeOffset.TryParse(await settings.GetAsync(key, ct), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var t) ? t : null;

    public Task SetTimeAsync(string key, DateTimeOffset value, CancellationToken ct) =>
        settings.SetAsync(key, value.ToString("O", System.Globalization.CultureInfo.InvariantCulture), ct);

    public async Task<long> GetLongAsync(string key, CancellationToken ct) =>
        long.TryParse(await settings.GetAsync(key, ct), out var v) ? v : 0;

    public Task SetLongAsync(string key, long value, CancellationToken ct) =>
        settings.SetAsync(key, value.ToString(System.Globalization.CultureInfo.InvariantCulture), ct);

    public Task<string?> GetAsync(string key, CancellationToken ct) => settings.GetAsync(key, ct);

    public Task SetAsync(string key, string? value, CancellationToken ct) => settings.SetAsync(key, value, ct);
}

/// <summary>Setting keys, in one place.</summary>
internal static class Keys
{
    public static string LastId(string familyId, string topic) => $"family.{familyId}.lastId.{topic}";

    public static string LastSync(string familyId) => $"family.{familyId}.lastSync";

    public static string LastCheckIn(string familyId) => $"family.{familyId}.lastCheckIn";

    public static string MySeq(string familyId) => $"family.{familyId}.seq";

    /// <summary>Before the replay window: the highest number seen (still read once, as the window's floor).</summary>
    public static string TheirSeq(string familyId, string memberId) => $"family.{familyId}.seq.{memberId}";

    public static string TheirWindow(string familyId, string memberId) => $"family.{familyId}.window.{memberId}";

    public static string Seen(string familyId, string memberId) => $"family.{familyId}.seen.{memberId}";

    public static string Removed(string familyId) => $"family.{familyId}.removed";

    public static string OldTopics(string familyId) => $"family.{familyId}.oldTopics";

    public static string Invites(string familyId) => $"family.{familyId}.invites";

    public static string PendingJoins(string familyId) => $"family.{familyId}.pendingJoins";

    public const string OutgoingJoins = "joins.outgoing";
}

public sealed record OldTopic(string Topic, DateTimeOffset Until);

/// <summary>
/// Replay protection for one sender, as a sliding window (like IPsec's): a message number is accepted once, in any
/// order, as long as it is above <see cref="Floor"/>. ntfy.sh can return two messages sent in the same second in
/// either order, so "must be higher than the last one" would throw away the earlier of the two.
/// </summary>
public sealed class ReplayWindow
{
    public const int Size = 256;

    /// <summary>Everything at or below this number counts as seen.</summary>
    public long Floor { get; set; }

    public long Max { get; set; }

    /// <summary>Numbers above <see cref="Floor"/> already accepted.</summary>
    public List<long> Seen { get; set; } = [];

    /// <summary>True (and remembered) when this number hasn't been seen; false for a replay or one too old to tell.</summary>
    public bool Accept(long seq)
    {
        if (seq <= Floor || Seen.Contains(seq))
        {
            return false;
        }

        Seen.Add(seq);
        Max = Math.Max(Max, seq);
        Floor = Math.Max(Floor, Max - Size);
        Seen.RemoveAll(s => s <= Floor);
        return true;
    }
}

/// <summary>An invite this (admin) phone created and still accepts.</summary>
public sealed record StoredInvite(string InviteId, bool Remote, string? JoinTopic, byte[] JoinSecret, DateTimeOffset ExpiresAt, bool Used = false);

/// <summary>A remote join waiting for the admin to compare the safety code and approve.</summary>
public sealed record PendingJoin(string InviteId, MemberCard Member, string SafetyCode, DateTimeOffset ReceivedAt);

/// <summary>A join this phone requested and is waiting on.</summary>
public sealed record OutgoingJoin(Families.RemoteInvite Invite, DateTimeOffset RequestedAt);
