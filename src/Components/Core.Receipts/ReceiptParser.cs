using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace FamilyLists.Core.Receipts;

/// <summary>
/// Reads a shop receipt from OCR output without AI. Steps:
/// <list type="number">
/// <item>Words → rows: words whose vertical centres line up form one printed row (ML Kit's own "lines" often split
/// a product name from its price).</item>
/// <item>Each row is classified: a total (grand/net/sub), a discount, a header/footer row to skip (GST, payment,
/// bill number, column headings…), a product name on its own, or a product line with a price at the right.</item>
/// <item>For product lines, the numbers at the end are interpreted: "qty rate amount" when qty × rate ≈ amount,
/// "2 x 30.00", or just the amount. A name printed on the row above its numbers is joined to them.</item>
/// </list>
/// The result is a draft: the review screen shows every line and the difference to the printed total.
/// </summary>
public static partial class ReceiptParser
{
    public static ReceiptDraft Parse(RecognizedText text) =>
        ParseRows(text.Words.Count > 0 ? RowsFromWords(text.Words) : RowsFromText(text.Text));

    // ------------------------------------------------------------------ rows

    internal sealed record Row(string Text, IReadOnlyList<string> Tokens, double Height);

    internal static List<Row> RowsFromWords(IReadOnlyList<LayoutWord> words)
    {
        var rows = new List<List<LayoutWord>>();
        foreach (var page in words.Where(w => !string.IsNullOrWhiteSpace(w.Text)).GroupBy(w => w.Page).OrderBy(g => g.Key))
        {
            var current = new List<LayoutWord>();
            var center = 0.0;
            foreach (var word in page.OrderBy(w => w.CenterY).ThenBy(w => w.Left))
            {
                if (current.Count > 0)
                {
                    var height = Math.Max(Median(current.Select(w => w.Height)), word.Height);
                    if (Math.Abs(word.CenterY - center) > 0.45 * height)
                    {
                        rows.Add(current);
                        current = [];
                    }
                }

                current.Add(word);
                center = current.Average(w => w.CenterY);
            }

            if (current.Count > 0)
            {
                rows.Add(current);
            }
        }

        return rows.Select(r =>
        {
            var ordered = r.OrderBy(w => w.Left).ToList();
            var tokens = ordered.SelectMany(w => w.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToList();
            return new Row(string.Join(' ', tokens), tokens, Median(r.Select(w => w.Height)));
        }).ToList();
    }

    internal static List<Row> RowsFromText(string text) =>
        text.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .Select(l =>
            {
                var tokens = l.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                return new Row(string.Join(' ', tokens), tokens, 1);
            })
            .ToList();

    // ------------------------------------------------------------------ rows → draft

    internal static ReceiptDraft ParseRows(IReadOnlyList<Row> rows)
    {
        var lines = new List<ReceiptLine>();
        DateOnly? date = null;
        decimal? total = null, subtotal = null, discount = null, tax = null;
        var totalRank = -1;
        var firstContentRow = rows.Count;
        Row? pendingName = null;

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var lower = row.Text.ToLowerInvariant();
            date ??= FindDate(row.Text);
            var (nameTokens, numbers) = SplitTrailingNumbers(row.Tokens);
            var amount = numbers.Count > 0 ? numbers[^1].Value : (decimal?)null;

            if (TotalKind(lower) is { } kind)
            {
                firstContentRow = Math.Min(firstContentRow, i);
                pendingName = null;
                if (amount is null)
                {
                    continue;
                }

                switch (kind.Kind)
                {
                    case "total" when kind.Rank >= totalRank:
                        (total, totalRank) = (Math.Abs(amount.Value), kind.Rank);
                        break;
                    case "subtotal":
                        subtotal ??= Math.Abs(amount.Value);
                        break;
                    case "discount":
                        discount = (discount ?? 0) + Math.Abs(amount.Value);
                        break;
                    case "tax":
                        tax ??= Math.Abs(amount.Value);
                        break;
                }

                continue;
            }

            if (IsHeader(lower))
            {
                firstContentRow = Math.Min(firstContentRow, i);
                pendingName = null;
                continue;
            }

            if (IsSkip(lower))
            {
                pendingName = null;
                continue;
            }

            var name = CleanName(nameTokens);
            var looksLikeMoney = numbers.Count > 0 && (numbers[^1].HasCents || numbers.Count >= 2);
            if (!looksLikeMoney)
            {
                // A product name on its own row; its numbers may be on the next row.
                pendingName = Letters(name) >= 3 && i > 0 ? row : null;
                continue;
            }

            var raw = row.Text;
            if (Letters(name) < 2)
            {
                if (pendingName is null)
                {
                    continue;
                }

                name = CleanName(SplitTrailingNumbers(pendingName.Tokens).Name);
                raw = pendingName.Text + " " + row.Text;
            }

            pendingName = null;
            if (Letters(name) < 2)
            {
                continue;
            }

            firstContentRow = Math.Min(firstContentRow, i);
            var (qty, unitPrice, lineTotal) = Interpret(numbers);
            lines.Add(new ReceiptLine(raw, name, qty, unitPrice, lineTotal));
        }

        return new ReceiptDraft(FindStore(rows, firstContentRow), date, subtotal, tax, discount, total, lines);
    }

    // ------------------------------------------------------------------ numbers

    internal readonly record struct Number(decimal Value, bool HasCents);

    /// <summary>Splits "AMUL MILK 1L 2 x 30.00 60.00" into the name part and the numbers at the end (2, 30.00, 60.00).</summary>
    internal static (List<string> Name, List<Number> Numbers) SplitTrailingNumbers(IReadOnlyList<string> tokens)
    {
        var numbers = new List<Number>();
        var end = tokens.Count;
        while (end > 0)
        {
            var token = tokens[end - 1];
            if (token is "x" or "X" or "@" or "*" or "×" or "=" or "₹" or "Rs" or "Rs." or "-")
            {
                end--;
                continue;
            }

            if (ParseNumber(token) is not { } number)
            {
                break;
            }

            numbers.Insert(0, number);
            end--;
        }

        // "2x" glued to the name side: "Milk 2x" → qty token stays as a number only when clearly separate.
        return (tokens.Take(end).ToList(), numbers);
    }

    internal static Number? ParseNumber(string token)
    {
        var t = token.Trim().TrimEnd('-', '/', ':');
        foreach (var prefix in new[] { "₹", "Rs.", "Rs", "RS.", "RS", "INR", "$", "€", "£" })
        {
            if (t.StartsWith(prefix, StringComparison.Ordinal))
            {
                t = t[prefix.Length..];
                break;
            }
        }

        if (t.EndsWith('x') || t.EndsWith('X'))
        {
            t = t[..^1]; // "2x"
        }

        if (!MoneyRegex().IsMatch(t))
        {
            return null;
        }

        var clean = t.Replace(",", "");
        var hasCents = clean.Contains('.') && clean.Length - clean.IndexOf('.') - 1 == 2;
        return decimal.TryParse(clean, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value)
            ? new Number(value, hasCents)
            : null;
    }

    /// <summary>The numbers at the end of a product row → (qty, unit price, line total).</summary>
    internal static (decimal? Qty, decimal? UnitPrice, decimal? LineTotal) Interpret(IReadOnlyList<Number> numbers)
    {
        var total = Math.Abs(numbers[^1].Value);
        var before = numbers.Take(numbers.Count - 1).Select(n => Math.Abs(n.Value)).ToList();

        // Any pair before the amount whose product is the amount: (qty, rate). Prefer the pair nearest the amount.
        for (var b = before.Count - 1; b >= 1; b--)
        {
            for (var a = b - 1; a >= 0; a--)
            {
                foreach (var (q, r) in new[] { (before[a], before[b]), (before[b], before[a]) })
                {
                    if (IsQuantity(q) && Close(q * r, total))
                    {
                        return (q, r, total);
                    }
                }
            }
        }

        if (before.Count >= 1)
        {
            var x = before[^1];
            if (Close(x, total))
            {
                return (1, total, total); // rate = amount: one of it
            }

            if (IsQuantity(x) && x <= 50 && total > x)
            {
                return (x, decimal.Round(total / x, 2), total); // "qty amount"
            }

            if (x > 0 && total / x is var ratio && IsQuantity(decimal.Round(ratio, 3)) && Close(decimal.Round(ratio) * x, total) && ratio >= 1)
            {
                return (decimal.Round(ratio), x, total); // "rate amount"
            }
        }

        return (null, null, total);
    }

    private static bool IsQuantity(decimal q) => q > 0 && q <= 1000 && decimal.Round(q, 3) == q;

    private static bool Close(decimal a, decimal b) => Math.Abs(a - b) <= Math.Max(0.05m, 0.01m * Math.Abs(b));

    // ------------------------------------------------------------------ classification

    private static (string Kind, int Rank)? TotalKind(string lower)
    {
        if (SubtotalRegex().IsMatch(lower))
        {
            return ("subtotal", 0);
        }

        if (TaxTotalRegex().IsMatch(lower))
        {
            return ("tax", 0);
        }

        if (DiscountRegex().IsMatch(lower))
        {
            return ("discount", 0);
        }

        if (GrandTotalRegex().IsMatch(lower))
        {
            return ("total", 3);
        }

        if (TotalRegex().IsMatch(lower) && !NotTotalRegex().IsMatch(lower))
        {
            return ("total", 2);
        }

        return null;
    }

    /// <summary>Column headings: "Item  Qty  Rate  Amount".</summary>
    private static bool IsHeader(string lower) => HeaderWordRegex().Matches(lower).Select(m => m.Value).Distinct().Count() >= 2;

    private static bool IsSkip(string lower) => SkipRegex().IsMatch(lower);

    private static int Letters(string text) => text.Count(char.IsLetter);

    // ------------------------------------------------------------------ names, store, date

    internal static string CleanName(IEnumerable<string> tokens)
    {
        var words = tokens
            .Select(t => t.Trim('*', '#', '|', ':', ';', ',', '"', '\''))
            .Where(t => t.Length > 0)
            .ToList();

        // Leading serial numbers and item codes: "1", "01.", "3)", "8901234567890".
        while (words.Count > 0 && LeadingCodeRegex().IsMatch(words[0]))
        {
            words.RemoveAt(0);
        }

        var text = string.Join(' ', words);
        var allCaps = text.Any(char.IsLetter) && !text.Any(char.IsLower);
        return allCaps ? TitleCase(text) : text;
    }

    private static string TitleCase(string text)
    {
        var result = new StringBuilder(text.Length);
        foreach (var word in text.Split(' '))
        {
            if (result.Length > 0)
            {
                result.Append(' ');
            }

            if (word.Any(char.IsDigit))
            {
                result.Append(word.ToLowerInvariant()); // "1L" → "1l", "500ML" → "500ml"
            }
            else
            {
                result.Append(char.ToUpperInvariant(word[0])).Append(word[1..].ToLowerInvariant());
            }
        }

        return result.ToString();
    }

    /// <summary>The shop's name: the biggest text above the first product or heading (or the first such row).</summary>
    private static string? FindStore(IReadOnlyList<Row> rows, int before)
    {
        var candidates = rows.Take(Math.Min(before, 8))
            .Where(r => r.Text.Count(char.IsLetter) >= 3 && !IsSkip(r.Text.ToLowerInvariant()) && FindDate(r.Text) is null)
            .ToList();
        if (candidates.Count == 0)
        {
            return null;
        }

        var tallest = candidates.MaxBy(r => r.Height)!;
        var name = CleanName(tallest.Tokens);
        return name.Length is > 0 and <= 60 ? name : null;
    }

    internal static DateOnly? FindDate(string text)
    {
        if (IsoDateRegex().Match(text) is { Success: true } iso
            && TryDate(Int(iso.Groups[1].Value), Int(iso.Groups[2].Value), Int(iso.Groups[3].Value)) is { } isoDate)
        {
            return isoDate;
        }

        if (NumericDateRegex().Match(text) is { Success: true } m)
        {
            var a = Int(m.Groups[1].Value);
            var b = Int(m.Groups[2].Value);
            var y = Year(m.Groups[3].Value);
            // Day first (India, UK, most of the world) unless that can't be right.
            return TryDate(y, b, a) ?? TryDate(y, a, b);
        }

        if (NamedMonthRegex().Match(text) is { Success: true } n && MonthNumber(n.Groups[2].Value) is { } month)
        {
            return TryDate(Year(n.Groups[3].Value), month, Int(n.Groups[1].Value));
        }

        return null;
    }

    private static int Int(string s) => int.Parse(s, CultureInfo.InvariantCulture);

    private static int Year(string s) => s.Length == 2 ? 2000 + Int(s) : Int(s);

    private static DateOnly? TryDate(int y, int m, int d) =>
        y is >= 2000 and <= 2100 && m is >= 1 and <= 12 && d >= 1 && d <= DateTime.DaysInMonth(y, m) ? new DateOnly(y, m, d) : null;

    private static int? MonthNumber(string name)
    {
        var key = name.Length >= 3 ? name[..3].ToLowerInvariant() : "";
        var index = Array.IndexOf(["jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec"], key);
        return index >= 0 ? index + 1 : null;
    }

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToList();
        return sorted.Count == 0 ? 0 : sorted[sorted.Count / 2];
    }

    // ------------------------------------------------------------------ patterns

    /// <summary>1234 / 1,234.50 / 1,05,400.00 (Indian grouping) / -10.00.</summary>
    [GeneratedRegex(@"^-?(?:\d{1,3}(?:,\d{2,3})+|\d+)(?:\.\d{1,3})?$")]
    private static partial Regex MoneyRegex();

    [GeneratedRegex(@"\b(?:sub\s*-?\s*total|gross\s*(?:amt|amount|total)?)\b")]
    private static partial Regex SubtotalRegex();

    [GeneratedRegex(@"\b(?:total\s*(?:tax|gst)|(?:tax|gst)\s*(?:amt|amount|total))\b")]
    private static partial Regex TaxTotalRegex();

    [GeneratedRegex(@"\b(?:discount|disc\.?|less|coupon)\b")]
    private static partial Regex DiscountRegex();

    [GeneratedRegex(@"\b(?:grand\s*total|net\s*(?:payable|amount|amt|total|value)|(?:amount|amt|total)\s*payable|bill\s*(?:amount|amt|total)|to\s*pay|total\s*(?:amount|amt|due)|amount\s*due)\b")]
    private static partial Regex GrandTotalRegex();

    [GeneratedRegex(@"\b(?:total|tot)\b")]
    private static partial Regex TotalRegex();

    [GeneratedRegex(@"\btotal\s*(?:qty|quantity|items?|savings?|saved|discount|weight|pcs|mrp)\b|\bno\.?\s*of\s*items\b")]
    private static partial Regex NotTotalRegex();

    [GeneratedRegex(@"\b(?:qty|quantity|rate|amount|amt|price|mrp|item|items|description|particulars|hsn|value)\b")]
    private static partial Regex HeaderWordRegex();

    [GeneratedRegex(@"\b(?:gstin|gst\s*no|fssai|cin|pan|invoice|inv\.?\s*no|bill\s*(?:no|#)|receipt\s*no|txn|transaction|date|time|cashier|counter|terminal|till|phone|ph|mob|mobile|tel|email|www|http|thank|visit|cash|card|upi|paytm|gpay|phonepe|change|balance|tender|tendered|round\s*off|roundoff|rounding|you\s*saved|savings|cgst|sgst|igst|utgst|cess|taxable|vat|e\s*&\s*o\.?\s*e|customer|address|pin|no\.?\s*of\s*items|total\s*qty|items\s*:|qty\s*:)\b")]
    private static partial Regex SkipRegex();

    [GeneratedRegex(@"^(?:\d{1,3}[.)]?|\d{5,})$")]
    private static partial Regex LeadingCodeRegex();

    [GeneratedRegex(@"\b(20\d{2})-(\d{1,2})-(\d{1,2})(?!\d)")]
    private static partial Regex IsoDateRegex();

    [GeneratedRegex(@"\b(\d{1,2})[/\-.](\d{1,2})[/\-.](\d{4}|\d{2})\b")]
    private static partial Regex NumericDateRegex();

    [GeneratedRegex(@"\b(\d{1,2})[\s\-]*([A-Za-z]{3,9})[\s\-,]*(\d{4}|\d{2})\b")]
    private static partial Regex NamedMonthRegex();
}
