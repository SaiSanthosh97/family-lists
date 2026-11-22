using System.Globalization;

namespace FamilyLists.Core.Lists;

/// <summary>A "period" is one calendar month, written "YYYY-MM" (sorts correctly as text).</summary>
public static class Period
{
    public static string Of(DateOnly date) => $"{date.Year:D4}-{date.Month:D2}";

    public static string Shift(string period, int months)
    {
        var first = FirstDay(period).AddMonths(months);
        return Of(first);
    }

    public static DateOnly FirstDay(string period) =>
        TryParse(period, out var date) ? date : throw new FormatException($"Not a month: '{period}'.");

    public static bool TryParse(string? period, out DateOnly firstDay)
    {
        firstDay = default;
        return period is { Length: 7 } && period[4] == '-'
            && int.TryParse(period.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var y)
            && int.TryParse(period.AsSpan(5, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var m)
            && y is >= 2000 and <= 2200 && m is >= 1 and <= 12
            && (firstDay = new DateOnly(y, m, 1)) != default;
    }

    /// <summary>"November 2026" (always the full year: "Nov 26" reads like a date).</summary>
    public static string Label(string period, CultureInfo? culture = null) =>
        FirstDay(period).ToString("MMMM yyyy", culture ?? CultureInfo.CurrentCulture);

    /// <summary>"Nov 2026".</summary>
    public static string ShortLabel(string period, CultureInfo? culture = null) =>
        FirstDay(period).ToString("MMM yyyy", culture ?? CultureInfo.CurrentCulture);

    /// <summary>"November".</summary>
    public static string MonthName(string period, CultureInfo? culture = null) =>
        FirstDay(period).ToString("MMMM", culture ?? CultureInfo.CurrentCulture);
}
