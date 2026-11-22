namespace FamilyLists.Core.ClaudeReader.Tests;

public class ClaudeReceiptReaderTests
{
    [Fact]
    public void Turns_the_structured_answer_into_a_draft_with_suggested_matches()
    {
        var draft = ClaudeReceiptReader.ToDraft(
            """
            {"store_name":"FreshMart","bill_date":"2026-11-21","subtotal":null,"tax":null,"discount":10,"total":895.5,
             "lines":[
               {"raw_text":"AMUL TAAZA TONED MLK 1L","name":"Toned milk 1L","qty":2,"unit_price":60,"line_total":120,"matched_item_name":"Milk"},
               {"raw_text":"CARRY BAG","name":"Carry bag","qty":1,"unit_price":null,"line_total":5,"matched_item_name":null},
               {"raw_text":"???","name":"  ","qty":null,"unit_price":null,"line_total":null,"matched_item_name":null}
             ]}
            """);

        Assert.Equal(("FreshMart", new DateOnly(2026, 11, 21), 10m, 895.5m), (draft.StoreName, draft.BillDate!.Value, draft.Discount!.Value, draft.Total!.Value));
        Assert.Equal("claude", draft.ReadBy);
        Assert.Equal(2, draft.Lines.Count); // the unreadable line is dropped
        Assert.Equal(("Toned milk 1L", 2m, 60m, 120m), (draft.Lines[0].Name, draft.Lines[0].Qty!.Value, draft.Lines[0].UnitPrice!.Value, draft.Lines[0].LineTotal!.Value));
        Assert.Equal(["Milk", null], draft.SuggestedItemNames);
    }

    [Fact]
    public void A_bad_date_is_dropped_and_broken_json_is_a_readable_error()
    {
        var draft = ClaudeReceiptReader.ToDraft("""{"store_name":null,"bill_date":"21/11/2026","subtotal":null,"tax":null,"discount":null,"total":null,"lines":[]}""");
        Assert.Null(draft.BillDate);
        Assert.Throws<ClaudeReaderException>(() => ClaudeReceiptReader.ToDraft("not json"));
        Assert.Throws<ClaudeReaderException>(() => ClaudeReceiptReader.ToDraft("{}"));
    }

    [Fact]
    public void The_schema_requires_every_field_and_allows_no_extras()
    {
        var schema = ClaudeReceiptReader.Schema;
        Assert.Equal("object", schema["type"].GetString());
        Assert.False(schema["additionalProperties"].GetBoolean());
        Assert.Equal(7, schema["required"].GetArrayLength());
        var line = schema["properties"].GetProperty("lines").GetProperty("items");
        Assert.False(line.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(6, line.GetProperty("required").GetArrayLength());
    }

    [Fact]
    public void The_prompt_includes_this_months_list()
    {
        var prompt = ClaudeReceiptReader.Prompt(["Milk", "Basmati rice 5kg"]);
        Assert.Contains("- Milk", prompt);
        Assert.Contains("- Basmati rice 5kg", prompt);
        Assert.Contains("(the list is empty)", ClaudeReceiptReader.Prompt([]));
    }

    [Fact]
    public async Task Without_a_key_it_asks_for_one_and_calls_nothing()
    {
        var reader = new ClaudeReceiptReader("");
        var error = await Assert.ThrowsAsync<ClaudeReaderException>(() => reader.ReadAsync([1], "image/jpeg", []));
        Assert.Contains("API key", error.Message);
        Assert.Equal("claude-opus-5-5", reader.Model);
    }
}
