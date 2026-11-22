using System.Globalization;

namespace FamilyLists.Core.Bills;

public enum SpendSource
{
    None,
    Bills,
    Manual,
    Items,
}

/// <summary>Money rules (decimal, so sums are exact to the paisa/cent). Ported from the web version.</summary>
public static class Money
{
    public static decimal Sum(IEnumerable<decimal?> values) => values.Sum(v => v ?? 0);

    /// <summary>
    /// What a month cost: confirmed bill totals if there are any, otherwise a total typed in by hand, otherwise the sum
    /// of prices typed on items.
    /// </summary>
    public static (decimal? Total, SpendSource Source) MonthSpend(IEnumerable<Bill> bills, decimal? manualTotal, IEnumerable<decimal?> itemPrices)
    {
        var confirmed = bills.Where(b => b.Status == BillStatus.Confirmed && !b.IsDeleted && b.Total is not null).ToList();
        if (confirmed.Count > 0)
        {
            return (Sum(confirmed.Select(b => b.Total)), SpendSource.Bills);
        }

        if (manualTotal is not null)
        {
            return (manualTotal, SpendSource.Manual);
        }

        var priced = itemPrices.Where(p => p is not null).ToList();
        return priced.Count > 0 ? (Sum(priced), SpendSource.Items) : (null, SpendSource.None);
    }

    /// <summary>
    /// Printed total minus what the lines add up to (after the bill-wide discount). Non-zero usually means a line was
    /// misread or missed, or tax is printed separately.
    /// </summary>
    public static decimal? Difference(IEnumerable<decimal?> lineTotals, decimal? total, decimal? discount) =>
        total is null ? null : total.Value - (Sum(lineTotals) - (discount ?? 0));

    /// <summary>"₹1,054.00" in the group's currency, formatted for this phone.</summary>
    public static string Format(decimal? amount, string currency, CultureInfo? culture = null)
    {
        if (amount is null)
        {
            return "—";
        }

        var symbol = Symbol(currency);
        var format = (NumberFormatInfo)(culture ?? CultureInfo.CurrentCulture).NumberFormat.Clone();
        format.CurrencySymbol = symbol;
        if (currency == "INR")
        {
            format.CurrencyGroupSizes = [3, 2]; // 1,05,400.00
        }

        return amount.Value.ToString("C2", format);
    }

    /// <summary>Reads "1,054.50" / "1054.5" / "₹ 60" typed by a person; null when empty or not a number.</summary>
    public static decimal? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var clean = new string(text.Where(c => char.IsDigit(c) || c is '.' or '-').ToArray());
        return decimal.TryParse(clean, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    public static string Symbol(string currency) => currency switch
    {
        "INR" => "₹",
        "USD" => "$",
        "EUR" => "€",
        "GBP" => "£",
        "AUD" => "A$",
        "CAD" => "C$",
        "SGD" => "S$",
        "AED" => "AED ",
        "JPY" => "¥",
        _ => currency + " ",
    };

    public static readonly IReadOnlyList<string> Currencies = ["INR", "USD", "EUR", "GBP", "AUD", "CAD", "SGD", "AED", "JPY"];
}
