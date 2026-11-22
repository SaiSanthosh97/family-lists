using System.Text.RegularExpressions;

namespace FamilyLists.Core.Lists;

/// <summary>
/// Product name normalising and fuzzy matching: collapses duplicates when a month is copied, and matches bill lines
/// to list items ("Toned milk 1L" → "Milk"). Ported from the web version.
/// </summary>
public static partial class ProductNames
{
    private static readonly HashSet<string> Stop = ["the", "a", "an", "of", "and", "with", "fresh", "pack", "organic", "pkt"];

    public static IReadOnlyList<string> Tokens(string name)
    {
        var text = SizeRegex().Replace(name.ToLowerInvariant(), " ");
        text = NonWordRegex().Replace(text, " ");
        return text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => !Stop.Contains(w) && !w.All(char.IsAsciiDigit))
            .Select(Singular)
            .ToList();
    }

    /// <summary>Order-independent key: "Toned-Milk 500ml" → "milk toned".</summary>
    public static string Normalize(string name) => string.Join(' ', Tokens(name).Order(StringComparer.Ordinal));

    /// <summary>0..1: how likely two product names describe the same thing.</summary>
    public static double Similarity(string a, string b)
    {
        var ta = Tokens(a).ToHashSet();
        var tb = Tokens(b).ToHashSet();
        if (ta.Count == 0 || tb.Count == 0)
        {
            return 0;
        }

        var shared = ta.Count(tb.Contains);
        if (shared == 0)
        {
            foreach (var x in ta)
            {
                foreach (var y in tb)
                {
                    if (x.Length >= 4 && y.Length >= 4 && (x.StartsWith(y, StringComparison.Ordinal) || y.StartsWith(x, StringComparison.Ordinal)))
                    {
                        return 0.5;
                    }
                }
            }

            return 0;
        }

        var smaller = Math.Min(ta.Count, tb.Count);
        var union = ta.Union(tb).Count();
        // Containment matters more than overlap: "Milk" is contained in "Toned milk".
        return 0.7 * shared / smaller + 0.3 * shared / union;
    }

    /// <summary>The best candidate for a bill line, or null when nothing is close enough.</summary>
    public static T? BestMatch<T>(string lineName, IEnumerable<T> candidates, Func<T, string> name, double threshold = 0.6)
        where T : class
    {
        T? best = null;
        var bestScore = 0.0;
        foreach (var candidate in candidates)
        {
            var score = Similarity(lineName, name(candidate));
            if (score > bestScore)
            {
                best = candidate;
                bestScore = score;
            }
        }

        return bestScore >= threshold ? best : null;
    }

    /// <summary>Candidates ordered by how well they match (for the picker).</summary>
    public static IReadOnlyList<T> Rank<T>(string lineName, IEnumerable<T> candidates, Func<T, string> name) =>
        candidates
            .Select(c => (Item: c, Score: Similarity(lineName, name(c))))
            .OrderByDescending(x => x.Score)
            .ThenBy(x => name(x.Item), StringComparer.CurrentCultureIgnoreCase)
            .Select(x => x.Item)
            .ToList();

    private static string Singular(string word)
    {
        if (word.Length > 4 && word.EndsWith("ies", StringComparison.Ordinal))
        {
            return word[..^3] + "y";
        }

        if (word.Length > 4 && word.EndsWith("oes", StringComparison.Ordinal))
        {
            return word[..^2];
        }

        if (word.Length > 3 && word.EndsWith('s') && !word.EndsWith("ss", StringComparison.Ordinal))
        {
            return word[..^1];
        }

        return word;
    }

    [GeneratedRegex(@"\b\d+(?:[.,]\d+)?\s*(?:kg|g|gm|gms|mg|l|ltr|lt|ml|cl|oz|lb|lbs|pcs|pc|pk|x)\b")]
    private static partial Regex SizeRegex();

    [GeneratedRegex(@"[^\p{L}\p{N}\s]")]
    private static partial Regex NonWordRegex();
}
