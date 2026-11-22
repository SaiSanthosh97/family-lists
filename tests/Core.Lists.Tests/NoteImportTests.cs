namespace FamilyLists.Core.Lists.Tests;

/// <summary>Notes as Google Keep, Samsung Notes and other apps share them (Share → Family Lists), or as pasted.</summary>
public class NoteImportTests
{
    private static IEnumerable<(string, decimal, bool)> Rows(ParsedNote note) => note.Lines.Select(l => (l.Name, l.Qty, l.Done));

    [Fact]
    public void Reads_a_google_keep_checklist_with_its_title_and_ticked_items()
    {
        var note = NoteImport.Parse("Groceries\n☐ Milk\n☐ 3 onions\n☑ Eggs x12\n☐ Basmati rice 5kg\n");

        Assert.Equal("Groceries", note.Title);
        Assert.Equal([("Milk", 1m, false), ("onions", 3m, false), ("Eggs", 12m, true), ("Basmati rice 5kg", 1m, false)], Rows(note));
    }

    [Fact]
    public void Reads_a_samsung_notes_text_with_bullets_numbers_and_a_blank_line_after_the_title()
    {
        var note = NoteImport.Parse("Monthly shopping\r\n\r\n• Toor dal 1kg\r\n• Sugar - 2\r\n1. Dish soap\r\n2) Atta (2)\r\n- Bananas\r\n");

        Assert.Equal("Monthly shopping", note.Title);
        Assert.Equal([("Toor dal 1kg", 1m, false), ("Sugar", 2m, false), ("Dish soap", 1m, false), ("Atta", 2m, false), ("Bananas", 1m, false)], Rows(note));
    }

    [Fact]
    public void Plain_lines_have_no_title_and_square_brackets_work_as_checkboxes()
    {
        var note = NoteImport.Parse("[ ] Milk\n[x] Bread\nCurd");
        Assert.Null(note.Title);
        Assert.Equal([("Milk", 1m, false), ("Bread", 1m, true), ("Curd", 1m, false)], Rows(note));

        var plain = NoteImport.Parse("Milk\nBread\nCurd");
        Assert.Null(plain.Title);
        Assert.Equal(3, plain.Lines.Count);
    }

    [Fact]
    public void A_single_line_with_commas_is_split()
    {
        var note = NoteImport.Parse("milk, 6 eggs, bread,  curd ");
        Assert.Equal([("milk", 1m, false), ("eggs", 6m, false), ("bread", 1m, false), ("curd", 1m, false)], Rows(note));
    }

    [Fact]
    public void Duplicates_blank_lines_and_lines_without_letters_are_dropped()
    {
        var note = NoteImport.Parse("☐ Onions\n\n☐ onion\n☐ ----\n☐ 123\n☐ Tomatoes");
        Assert.Equal(["Onions", "Tomatoes"], note.Lines.Select(l => l.Name));
    }

    [Theory]
    [InlineData("3 onions", "onions", 3)]
    [InlineData("3x onions", "onions", 3)]
    [InlineData("onions x3", "onions", 3)]
    [InlineData("onions × 3", "onions", 3)]
    [InlineData("onions - 3", "onions", 3)]
    [InlineData("onions (3)", "onions", 3)]
    [InlineData("onions: 3", "onions", 3)]
    [InlineData("Basmati rice 5kg", "Basmati rice 5kg", 1)]
    [InlineData("5kg rice", "5kg rice", 1)]
    [InlineData("Vitamin B12", "Vitamin B12", 1)]
    [InlineData("Milk 1L", "Milk 1L", 1)]
    public void Reads_quantities_written_in_common_ways(string text, string name, decimal qty) =>
        Assert.Equal((name, qty), NoteImport.QuickAdd(text));

    [Fact]
    public void Empty_or_huge_input_is_safe()
    {
        Assert.Empty(NoteImport.Parse(null).Lines);
        Assert.Empty(NoteImport.Parse("   \n  ").Lines);
        var huge = NoteImport.Parse(string.Join('\n', Enumerable.Range(0, 1000).Select(i => $"Item {i} thing{i}x")));
        Assert.Equal(NoteImport.MaxLines, huge.Lines.Count);
        Assert.All(NoteImport.Parse(new string('a', 500)).Lines, l => Assert.True(l.Name.Length <= NoteImport.MaxNameLength));
    }
}
