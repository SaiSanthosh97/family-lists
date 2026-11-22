using FamilyLists.Core.Common;

namespace FamilyLists.Core.Lists;

public enum ListKind
{
    /// <summary>Groceries: one list per month, copied forward.</summary>
    Monthly,

    /// <summary>A plain shared checklist.</summary>
    Todo,
}

/// <summary>A list in a group ("Groceries", "Weekend chores").</summary>
public sealed class ShoppingList : IVersioned
{
    public required string Id { get; init; }

    public required string FamilyId { get; init; }

    public required string Name { get; set; }

    public ListKind Kind { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public HybridTimestamp Version { get; set; }

    public string UpdatedBy { get; set; } = "";

    public DateTimeOffset UpdatedAt { get; set; }

    public bool IsDeleted { get; set; }
}

public enum MonthStatus
{
    /// <summary>Copied from last month; the family reviews it.</summary>
    Draft,

    /// <summary>Agreed: this is the shopping list.</summary>
    Final,

    /// <summary>Shopping done (a bill was added, or "Done, no bill").</summary>
    Shopped,
}

/// <summary>One month of a monthly list. Its id is derived from (list, month), so two phones agree on it.</summary>
public sealed class MonthList : IVersioned
{
    public required string Id { get; init; }

    public required string FamilyId { get; init; }

    public required string ListId { get; init; }

    /// <summary>"YYYY-MM".</summary>
    public required string Period { get; init; }

    public MonthStatus Status { get; set; }

    /// <summary>What the month cost when there is no bill, typed in by hand.</summary>
    public decimal? ManualTotal { get; set; }

    public HybridTimestamp Version { get; set; }

    public string UpdatedBy { get; set; } = "";

    public DateTimeOffset UpdatedAt { get; set; }

    public bool IsDeleted { get; set; }
}

public enum ItemSource
{
    /// <summary>Copied from last month.</summary>
    Carried,

    /// <summary>Typed in.</summary>
    Added,

    /// <summary>Found on a bill but not on the list.</summary>
    Bill,
}

/// <summary>An item on a month's list (or on a to-do list, where <see cref="MonthId"/> is null).</summary>
public sealed class ListItem : IVersioned
{
    public required string Id { get; init; }

    public required string FamilyId { get; init; }

    public required string ListId { get; init; }

    /// <summary>The month it belongs to; null on to-do lists.</summary>
    public string? MonthId { get; init; }

    public required string Name { get; set; }

    public decimal Qty { get; set; } = 1;

    public string? Unit { get; set; }

    public string? Notes { get; set; }

    /// <summary>Needed this month (unticked while reviewing = "not needed").</summary>
    public bool Included { get; set; } = true;

    /// <summary>In the cart (groceries) or done (to-dos).</summary>
    public bool Bought { get; set; }

    public ItemSource Source { get; set; } = ItemSource.Added;

    /// <summary>Marked "not needed" last month; still copied, listed last.</summary>
    public bool SkippedLast { get; set; }

    /// <summary>What it cost this month (from the bill, or typed in).</summary>
    public decimal? Price { get; set; }

    public int Position { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public HybridTimestamp Version { get; set; }

    public string UpdatedBy { get; set; } = "";

    public DateTimeOffset UpdatedAt { get; set; }

    public bool IsDeleted { get; set; }
}

public sealed class ListRuleException(string message) : Exception(message);

/// <summary>
/// Records created by "Start {month}" get this fixed version. Two phones that start the same month offline produce
/// identical copies (same ids, same seed version), and any real edit (a newer version) always wins over a seed, so
/// a phone that starts the month late can't bring back an item someone already removed.
/// </summary>
public static class Seeds
{
    public static readonly HybridTimestamp Version = new(1, 0, "seed");
}
