using FamilyLists.Core.Common;

namespace FamilyLists.Core.Vault;

/// <summary>A record as sync sends it: its group, its JSON and its version.</summary>
public sealed record SyncedRecord(string FamilyId, string Payload, HybridTimestamp Version);

/// <summary>
/// One kind of record that family sync carries (lists, months, items, bills, bill lines). Sync itself knows nothing
/// about groceries: it signs, encrypts, sends and verifies; each record kind loads and applies its own JSON.
/// (In Travel Vault this was a hard-coded switch over documents, travelers and trips.)
/// </summary>
public interface ISyncedRecords
{
    /// <summary>The short tag on the wire, one of <see cref="RecordTypes"/>.</summary>
    string Type { get; }

    /// <summary>The record to send after a local change, or null when there is nothing to send (missing, or a draft).</summary>
    Task<SyncedRecord?> LoadAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies a record received from another phone (already verified: right group, signed by an active member, newer
    /// than the change log). Must still check the JSON itself: same id, same group, and that it doesn't point into
    /// another group's data. Returns false when it was refused or is older than the local copy.
    /// </summary>
    Task<bool> ApplyAsync(string familyId, string id, string payload, CancellationToken cancellationToken = default);
}
