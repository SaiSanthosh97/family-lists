using System.Globalization;
using System.Text.RegularExpressions;

namespace FamilyLists.Core.Lists;

/// <summary>One line of a note, ready to become a list item.</summary>
/// <param name="Done">Ticked in the note (☑, [x]): probably already bought, so not picked by default.</param>
public sealed record NoteLine(string Name, decimal Qty, bool Done);

/// <summary>What was found in a note: its title (if it has one) and its lines.</summary>
public sealed record ParsedNote(string? Title, IReadOnlyList<NoteLine> Lines);

/// <summary>
/// Reads a note shared from Google Keep, Samsung Notes or any notes app (or pasted) into list items. Notes apps
/// share plain text: Keep writes checklists as "☐ Milk" / "☑ Eggs" under the title; Samsung Notes and others use
/// bullets, dashes, numbers or nothing at all. A single line with commas ("milk, eggs, bread") is split up.
/// </summary>
public static partial class NoteImport
{
    public const int MaxLines = 300;
    public const int MaxNameLength = 120;

    public static ParsedNote Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new ParsedNote(null, []);
        }

        var raw = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Select(l => l.Trim()).ToList();
        while (raw.Count > 0 && raw[0].Length == 0)
        {
            raw.RemoveAt(0);
        }

        // A title: the first line, when the rest are list lines and it isn't one itself (no checkbox or bullet while
        // the others have them, or followed by an empty line).
        string? title = null;
        var body = raw;
        if (raw.Count >= 2 && !HasMarker(raw[0]))
        {
            var rest = raw.Skip(1).Where(l => l.Length > 0).ToList();
            var restMarked = rest.Count > 0 && rest.Count(HasMarker) * 2 >= rest.Count;
            if (rest.Count > 0 && (raw[1].Length == 0 || restMarked) && raw[0].Length <= 60)
            {
                title = Clean(raw[0]);
                body = raw.Skip(1).ToList();
            }
        }

        var pieces = new List<string>();
        var lines = body.Where(l => l.Length > 0).ToList();
        if (lines.Count == 1 && !HasMarker(lines[0]) && lines[0].Contains(','))
        {
            pieces.AddRange(lines[0].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }
        else
        {
            pieces.AddRange(lines);
        }

        var result = new List<NoteLine>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var piece in pieces.Take(MaxLines))
        {
            var done = DoneRegex().IsMatch(piece);
            var cleaned = Clean(piece);
            if (cleaned.Length == 0 || !cleaned.Any(char.IsLetter))
            {
                continue;
            }

            var (name, qty) = QuickAdd(cleaned);
            name = name.Length > MaxNameLength ? name[..MaxNameLength].TrimEnd() : name;
            var key = ProductNames.Normalize(name);
            if (key.Length == 0 || !seen.Add(key))
            {
                continue;
            }

            result.Add(new NoteLine(name, qty, done));
        }

        return new ParsedNote(title is { Length: > 0 } ? title : null, result);
    }

    /// <summary>"3 onions", "onions x3", "onions - 3", "onions (3)" → ("onions", 3). "Rice 5kg" stays as it is.</summary>
    public static (string Name, decimal Qty) QuickAdd(string text)
    {
        var raw = text.Trim();
        foreach (var regex in new[] { LeadingQtyRegex(), TrailingQtyRegex() })
        {
            var m = regex.Match(raw);
            if (m.Success && decimal.TryParse(m.Groups["qty"].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var qty) && qty is > 0 and <= 1000)
            {
                return (m.Groups["name"].Value.Trim(), qty);
            }
        }

        return (raw, 1);
    }

    private static bool HasMarker(string line) => MarkerRegex().IsMatch(line);

    /// <summary>Removes checkboxes, bullets, numbering and stray symbols at the start.</summary>
    private static string Clean(string line)
    {
        var s = MarkerRegex().Replace(line, "");
        s = s.Trim().TrimEnd('.', ',', ';');
        return WhitespaceRegex().Replace(s, " ").Trim();
    }

    // Checkboxes (Keep: ☐ ☑; others: □ ▢ ✓ ✔ ✅ ☒ ■ [ ] [x]), bullets (- * • · ‣ ○ ● ◦ – —), numbering (1. 1) 1-).
    [GeneratedRegex(@"^\s*(?:(?:[☐☑☒□▢■✓✔✅✗✘]|\[\s?[xX✓✔]?\s?\])\s*|[-*•·‣○●◦–—]\s+|\d{1,3}[.)]\s+)+")]
    private static partial Regex MarkerRegex();

    [GeneratedRegex(@"^\s*(?:[☑☒✓✔✅]|\[\s?[xX✓✔]\s?\])")]
    private static partial Regex DoneRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    // "3 onions", "3x onions", "3 x onions"
    [GeneratedRegex(@"^(?<qty>\d+(?:\.\d+)?)\s*(?:[x×]\s*)?\s(?<name>\D.*)$|^(?<qty>\d+(?:\.\d+)?)[x×](?<name>\D.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex LeadingQtyRegex();

    // "onions x3", "onions × 3", "onions - 3", "onions (3)", "onions: 3"
    [GeneratedRegex(@"^(?<name>.*?\D)\s*(?:[x×]\s*|-\s+|:\s*|\(\s*)(?<qty>\d+(?:\.\d+)?)\s*\)?$", RegexOptions.IgnoreCase)]
    private static partial Regex TrailingQtyRegex();
}
