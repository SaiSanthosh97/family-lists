namespace FamilyLists.Core.Receipts.Tests;

/// <summary>
/// Synthetic receipts laid out the way ML Kit reports them: each printed row is several words with boxes (name on
/// the left, numbers in columns on the right), sometimes slightly tilted.
/// </summary>
public class ReceiptParserTests
{
    /// <summary>Builds one printed row: words at x positions on a baseline y (each word ~10 px per character, 20 px tall).</summary>
    private static IEnumerable<LayoutWord> Row(double y, params (string Text, double X)[] cells) =>
        cells.SelectMany(cell =>
        {
            var x = cell.X;
            return cell.Text.Split(' ').Select(word =>
            {
                var w = new LayoutWord(word, x, y, x + word.Length * 10, y + 20);
                x += word.Length * 10 + 8;
                return w;
            }).ToList();
        });

    private static ReceiptDraft Read(params IEnumerable<LayoutWord>[] rows) =>
        ReceiptParser.Parse(new RecognizedText("", rows.SelectMany(r => r).ToList()));

    [Fact]
    public void Reads_an_indian_supermarket_bill_with_qty_rate_amount_columns()
    {
        var bigTitle = new[] { new LayoutWord("D", 200, 10, 230, 60), new LayoutWord("MART", 240, 10, 380, 60) };
        var draft = Read(
            bigTitle,
            Row(80, ("Avenue Supermarts Ltd, Whitefield", 100)),
            Row(110, ("GSTIN 29AACCA8432H1ZQ", 100)),
            Row(140, ("Bill No: 4521", 20), ("Date: 21/11/2026 10:32", 300)),
            Row(180, ("Particulars", 20), ("Qty", 330), ("Rate", 400), ("Amount", 480)),
            Row(210, ("AMUL TAAZA TONED MILK 500ML", 20), ("2", 340), ("28.00", 400), ("56.00", 490)),
            Row(240, ("INDIA GATE BASMATI 5KG", 20), ("1", 340), ("650.00", 390), ("650.00", 480)),
            Row(270, ("ONION", 20), ("1.500", 320), ("40.00", 400), ("60.00", 490)),
            Row(300, ("COLGATE STRONG TEETH 200G", 20), ("1", 340), ("95.00", 400), ("95.00", 490)),
            Row(330, ("CARRY BAG", 20), ("1", 340), ("5.00", 410), ("5.00", 500)),
            Row(370, ("Total Qty: 6.5", 20)),
            Row(400, ("Total", 20), ("866.00", 480)),
            Row(430, ("CGST 2.5%", 20), ("10.83", 490)),
            Row(460, ("SGST 2.5%", 20), ("10.83", 490)),
            Row(490, ("Net Amount", 20), ("866.00", 480)),
            Row(520, ("Cash", 20), ("900.00", 480)),
            Row(550, ("Change", 20), ("34.00", 490)),
            Row(590, ("Thank you, visit again", 100)));

        Assert.Equal("D Mart", draft.StoreName);
        Assert.Equal(new DateOnly(2026, 11, 21), draft.BillDate);
        Assert.Equal(866m, draft.Total);
        Assert.Equal(
            [
                ("Amul Taaza Toned Milk 500ml", 2m, 28m, 56m),
                ("India Gate Basmati 5kg", 1m, 650m, 650m),
                ("Onion", 1.5m, 40m, 60m),
                ("Colgate Strong Teeth 200g", 1m, 95m, 95m),
                ("Carry Bag", 1m, 5m, 5m),
            ],
            draft.Lines.Select(l => (l.Name, l.Qty ?? 0, l.UnitPrice ?? 0, l.LineTotal ?? 0)));
    }

    [Fact]
    public void Same_size_print_keeps_the_top_row_as_the_shop_and_reads_a_rate_when_the_qty_was_lost()
    {
        // Found on the emulator: ML Kit gave the address row (with g's hanging below) a taller box than the shop name
        // in capitals, and missed the "2" in "Milk 1L  2  33.00  66.00".
        var shop = new[] { new LayoutWord("FRESH", 20, 10, 70, 30), new LayoutWord("MART", 80, 10, 120, 30), new LayoutWord("SUPERMARKET", 130, 10, 240, 30) };
        var address = new[] { new LayoutWord("12", 40, 50, 60, 74), new LayoutWord("MG", 68, 50, 88, 74), new LayoutWord("Road,", 96, 50, 146, 74), new LayoutWord("Bengaluru", 154, 50, 244, 74) };
        var draft = Read(
            shop,
            address,
            Row(100, ("Item", 20), ("Qty", 330), ("Rate", 400), ("Amt", 480)),
            Row(130, ("Milk 1L", 20), ("33.00", 400), ("66.00", 490)),
            Row(160, ("Bananas", 20), ("2", 340), ("60.00", 490)),
            Row(200, ("TOTAL", 20), ("126.00", 480)));

        Assert.Equal("Fresh Mart Supermarket", draft.StoreName);
        Assert.Equal(
            [("Milk 1L", 2m, 33m, 66m), ("Bananas", 2m, 30m, 60m)],
            draft.Lines.Select(l => (l.Name, l.Qty ?? 0, l.UnitPrice ?? 0, l.LineTotal ?? 0)));
    }

    [Fact]
    public void Joins_a_product_name_to_the_numbers_printed_on_the_row_below()
    {
        var draft = Read(
            Row(10, ("MORE SUPERMARKET", 100)),
            Row(60, ("AMUL BUTTER 100G", 20)),
            Row(85, ("2 x 56.00", 40), ("112.00", 480)),
            Row(110, ("TATA SALT 1KG", 20)),
            Row(135, ("1 x 28.00", 40), ("28.00", 490)),
            Row(180, ("GRAND TOTAL", 20), ("₹140.00", 470)));

        Assert.Equal([("Amul Butter 100g", 2m, 56m, 112m), ("Tata Salt 1kg", 1m, 28m, 28m)], draft.Lines.Select(l => (l.Name, l.Qty ?? 0, l.UnitPrice ?? 0, l.LineTotal ?? 0)));
        Assert.Equal(140m, draft.Total);
    }

    [Fact]
    public void A_slightly_tilted_photo_still_keeps_each_price_on_its_product_row()
    {
        var tilted = new List<LayoutWord>
        {
            new("SUGAR", 20, 100, 80, 120), new("1KG", 90, 101, 120, 121), new("48.00", 480, 107, 530, 127),
            new("TEA", 20, 140, 50, 160), new("POWDER", 60, 141, 120, 161), new("250G", 130, 142, 170, 162), new("120.00", 470, 147, 530, 167),
        };
        var draft = ReceiptParser.Parse(new RecognizedText("", tilted));
        Assert.Equal([("Sugar 1kg", 48m), ("Tea Powder 250g", 120m)], draft.Lines.Select(l => (l.Name, l.LineTotal ?? 0)));
    }

    [Fact]
    public void Plain_text_without_positions_is_read_line_by_line()
    {
        var draft = ReceiptParser.Parse(new RecognizedText(
            """
            Green Grocer
            Date: 2026-11-21
            Bananas 1 kg 60.00
            Eggs 12 x 7.00 84.00
            Sub Total 144.00
            Less: Discount -10.00
            Total 134.00
            """,
            []));

        Assert.Equal("Green Grocer", draft.StoreName);
        Assert.Equal(new DateOnly(2026, 11, 21), draft.BillDate);
        Assert.Equal([("Bananas 1 kg", 60m), ("Eggs", 84m)], draft.Lines.Select(l => (l.Name, l.LineTotal ?? 0)));
        Assert.Equal((12m, 7m), (draft.Lines[1].Qty ?? 0, draft.Lines[1].UnitPrice ?? 0));
        Assert.Equal((144m, 10m, 134m), (draft.Subtotal ?? 0, draft.Discount ?? 0, draft.Total ?? 0));
    }

    [Fact]
    public void Product_names_that_contain_skip_words_as_parts_of_words_are_kept()
    {
        var draft = ReceiptParser.Parse(new RecognizedText(
            """
            Seedless grapes 500g 80.00
            Cashew nuts 200g 240.00
            Dates 500g 150.00
            Table salt 1kg 28.00
            Total 498.00
            """,
            []));
        Assert.Equal(4, draft.Lines.Count);
        Assert.Equal(498m, draft.Total);
    }

    [Fact]
    public void Nothing_readable_gives_an_empty_draft()
    {
        var draft = ReceiptParser.Parse(new RecognizedText("", []));
        Assert.Empty(draft.Lines);
        Assert.Null(draft.Total);
    }

    [Theory]
    [InlineData("Date: 21/11/2026", 2026, 11, 21)]
    [InlineData("21-11-26 10:30", 2026, 11, 21)]
    [InlineData("11/25/2026", 2026, 11, 25)]
    [InlineData("2026-11-21T10:00", 2026, 11, 21)]
    [InlineData("21 Nov 2026", 2026, 11, 21)]
    [InlineData("21-Nov-26", 2026, 11, 21)]
    public void Finds_dates_day_first_unless_impossible(string text, int y, int m, int d) =>
        Assert.Equal(new DateOnly(y, m, d), ReceiptParser.FindDate(text));

    [Theory]
    [InlineData("1,05,400.00", 105400)]
    [InlineData("₹60.00", 60)]
    [InlineData("Rs.45.50", 45.5)]
    [InlineData("2x", 2)]
    [InlineData("-10.00", -10)]
    public void Reads_amounts_in_common_formats(string token, double expected) =>
        Assert.Equal((decimal)expected, ReceiptParser.ParseNumber(token)!.Value.Value);

    [Theory]
    [InlineData("1L")]
    [InlineData("abc")]
    [InlineData("12:30")]
    public void Non_numbers_are_not_amounts(string token) => Assert.Null(ReceiptParser.ParseNumber(token));
}
