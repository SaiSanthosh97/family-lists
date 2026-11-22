using FamilyLists.Core.Common;

namespace FamilyLists.Core.Bills;

public enum BillStatus
{
    /// <summary>Read, being checked on the phone that added it. Not sent to the group yet.</summary>
    Draft,

    /// <summary>Checked and added to the month: prices are on the items. Sent to the group.</summary>
    Confirmed,
}

/// <summary>A shop bill for one month of a monthly list. Its lines are separate records (each must fit one relay message).</summary>
public sealed class Bill : IVersioned
{
    public required string Id { get; init; }

    public required string FamilyId { get; init; }

    public required string ListId { get; init; }

    public required string MonthId { get; init; }

    public string? StoreName { get; set; }

    public DateOnly? BillDate { get; set; }

    public decimal? Subtotal { get; set; }

    public decimal? Tax { get; set; }

    public decimal? Discount { get; set; }

    public decimal? Total { get; set; }

    public BillStatus Status { get; set; }

    /// <summary>"phone", "claude" or "manual".</summary>
    public string ReadBy { get; set; } = "phone";

    /// <summary>The photo's file id; the photo itself stays (encrypted) on the phone that took it.</summary>
    public string? PhotoId { get; set; }

    /// <summary>Whose phone has the photo.</summary>
    public string? PhotoOn { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public HybridTimestamp Version { get; set; }

    public string UpdatedBy { get; set; } = "";

    public DateTimeOffset UpdatedAt { get; set; }

    public bool IsDeleted { get; set; }
}

public enum LineAction
{
    /// <summary>The same product as an item on the list.</summary>
    Match,

    /// <summary>Bought but not on the list: becomes a new item (so next month's list has it).</summary>
    Add,

    /// <summary>Not a product (bag, deposit, fee): counted in the bill total only.</summary>
    Ignore,
}

/// <summary>One line of a bill.</summary>
public sealed class BillLine : IVersioned
{
    public required string Id { get; init; }

    public required string FamilyId { get; init; }

    public required string BillId { get; init; }

    public int Position { get; set; }

    public string? RawText { get; set; }

    public required string Name { get; set; }

    public decimal? Qty { get; set; }

    public decimal? UnitPrice { get; set; }

    public decimal? LineTotal { get; set; }

    public LineAction Action { get; set; }

    /// <summary>The list item this line is (Match), or the item it created (Add).</summary>
    public string? ItemId { get; set; }

    public HybridTimestamp Version { get; set; }

    public string UpdatedBy { get; set; } = "";

    public DateTimeOffset UpdatedAt { get; set; }

    public bool IsDeleted { get; set; }
}

public sealed class BillRuleException(string message) : Exception(message);

/// <summary>Storage for bills and their lines (implemented in Storage.Sqlite).</summary>
public interface IBillRepository
{
    /// <summary>Includes deleted bills.</summary>
    Task<Bill?> GetBillAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>Bills of a month, not deleted, newest first.</summary>
    Task<IReadOnlyList<Bill>> BillsOfMonthAsync(string monthId, CancellationToken cancellationToken = default);

    /// <summary>Bills of a list (all months), not deleted.</summary>
    Task<IReadOnlyList<Bill>> BillsOfListAsync(string listId, CancellationToken cancellationToken = default);

    Task SaveBillAsync(Bill bill, CancellationToken cancellationToken = default);

    /// <summary>Includes deleted lines.</summary>
    Task<BillLine?> GetLineAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>Lines of a bill, not deleted, in order.</summary>
    Task<IReadOnlyList<BillLine>> LinesAsync(string billId, CancellationToken cancellationToken = default);

    Task SaveLineAsync(BillLine line, CancellationToken cancellationToken = default);
}
