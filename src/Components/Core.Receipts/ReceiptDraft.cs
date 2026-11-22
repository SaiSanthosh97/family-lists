namespace FamilyLists.Core.Receipts;

/// <summary>One product line as read from a receipt. Every number may be missing: the review screen fills the gaps.</summary>
/// <param name="RawText">The row as printed (shown small under the name, to help check it).</param>
/// <param name="Name">A cleaned-up product name.</param>
public sealed record ReceiptLine(string RawText, string Name, decimal? Qty, decimal? UnitPrice, decimal? LineTotal);

/// <summary>What a bill reader (on the phone, or Claude) understood from a receipt, before anyone checks it.</summary>
public sealed record ReceiptDraft(
    string? StoreName,
    DateOnly? BillDate,
    decimal? Subtotal,
    decimal? Tax,
    decimal? Discount,
    decimal? Total,
    IReadOnlyList<ReceiptLine> Lines)
{
    public static readonly ReceiptDraft Empty = new(null, null, null, null, null, null, []);

    /// <summary>Which reader produced it ("phone" or "claude"), for the review screen.</summary>
    public string ReadBy { get; init; } = "phone";

    /// <summary>Claude's suggested list item per line (same order as <see cref="Lines"/>), when it was given the list.</summary>
    public IReadOnlyList<string?> SuggestedItemNames { get; init; } = [];
}

/// <summary>Turns a photo (or PDF) of a receipt into a draft.</summary>
public interface IReceiptReader
{
    /// <param name="listItemNames">This month's list, so a smart reader can suggest matches (ignored by the phone reader).</param>
    Task<ReceiptDraft> ReadAsync(byte[] image, string mediaType, IReadOnlyList<string> listItemNames, CancellationToken cancellationToken = default);
}
