using System.Globalization;
using System.Text.Json;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Beta;
using Anthropic.Models.Beta.Messages;
using FamilyLists.Core.Receipts;

namespace FamilyLists.Core.ClaudeReader;

/// <summary>Something went wrong reading a bill with Claude; the message is written for the person holding the phone.</summary>
public sealed class ClaudeReaderException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Reads a receipt with Claude, using the person's own Anthropic API key (stored encrypted on this phone only). Off by
/// default: the app reads bills on the phone unless a key is added in Settings. The result is a draft, checked on the
/// review screen like any other.
/// <para>
/// Request: the photo (or PDF) + a short instruction + this month's list (so Claude can suggest matches), with a JSON
/// schema as structured output, so the reply always parses. A refusal is retried server-side on Anthropic's
/// recommended fallback model.
/// </para>
/// </summary>
public sealed class ClaudeReceiptReader(string apiKey) : IReceiptReader
{
    public const string DefaultModel = "claude-opus-5-5";

    public string Model { get; init; } = DefaultModel;

    public async Task<ReceiptDraft> ReadAsync(byte[] image, string mediaType, IReadOnlyList<string> listItemNames, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ClaudeReaderException("Add your Anthropic API key in Settings first.");
        }

        var data = Convert.ToBase64String(image);
        BetaContentBlockParam file = mediaType == "application/pdf"
            ? new BetaRequestDocumentBlock { Source = new BetaBase64PdfSource { Data = data } }
            : new BetaImageBlockParam { Source = new BetaBase64ImageSource { Data = data, MediaType = ImageType(mediaType) } };

        var parameters = new MessageCreateParams
        {
            Model = Model,
            MaxTokens = 16000,
            Betas = ["server-side-fallback-2026-07-01"],
            Fallbacks = new Default(),
            OutputConfig = new BetaOutputConfig
            {
                Effort = Effort.Medium,
                Format = new BetaJsonOutputFormat { Schema = Schema },
            },
            Messages =
            [
                new BetaUserMessageParam
                {
                    Content = new List<BetaContentBlockParam> { file, new BetaTextBlockParam { Text = Prompt(listItemNames) } },
                },
            ],
        };

        BetaMessage response;
        try
        {
            var client = new AnthropicClient { ApiKey = apiKey.Trim() };
            response = await client.Beta.Messages.Create(parameters, cancellationToken);
        }
        catch (AnthropicUnauthorizedException ex)
        {
            throw new ClaudeReaderException("The Anthropic API key isn't valid. Check it in Settings.", ex);
        }
        catch (AnthropicForbiddenException ex)
        {
            throw new ClaudeReaderException("This API key isn't allowed to use Claude. Check it in the Anthropic Console.", ex);
        }
        catch (AnthropicRateLimitException ex)
        {
            throw new ClaudeReaderException("Too many requests right now. Try again in a minute.", ex);
        }
        catch (AnthropicBadRequestException ex)
        {
            throw new ClaudeReaderException($"Claude couldn't take this bill: {ex.Message}", ex);
        }
        catch (Anthropic5xxException ex)
        {
            throw new ClaudeReaderException("Claude is having trouble right now. Try again later, or read the bill on the phone.", ex);
        }
        catch (AnthropicIOException ex)
        {
            throw new ClaudeReaderException("No connection to Claude. Check the internet and try again.", ex);
        }
        catch (AnthropicApiException ex)
        {
            throw new ClaudeReaderException($"Claude couldn't read the bill: {ex.Message}", ex);
        }

        if (response.StopReason == "refusal")
        {
            throw new ClaudeReaderException("Claude declined to read this image. Read it on the phone instead.");
        }

        if (response.StopReason == "max_tokens")
        {
            throw new ClaudeReaderException("This bill is too long to read in one go.");
        }

        var json = string.Concat(response.Content.Select(b => b.TryPickText(out var t) ? t.Text : ""));
        return ToDraft(json);
    }

    /// <summary>Turns Claude's JSON (shape: <see cref="Schema"/>) into a draft. Public for tests.</summary>
    public static ReceiptDraft ToDraft(string json)
    {
        ParsedBill? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<ParsedBill>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new ClaudeReaderException("Claude's answer couldn't be understood.", ex);
        }

        if (parsed?.Lines is null)
        {
            throw new ClaudeReaderException("Claude's answer had no lines.");
        }

        var lines = parsed.Lines.Where(l => !string.IsNullOrWhiteSpace(l.Name)).ToList();
        return new ReceiptDraft(
            string.IsNullOrWhiteSpace(parsed.StoreName) ? null : parsed.StoreName.Trim(),
            DateOnly.TryParseExact(parsed.BillDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null,
            parsed.Subtotal,
            parsed.Tax,
            parsed.Discount,
            parsed.Total,
            lines.Select(l => new ReceiptLine(l.RawText ?? l.Name!, l.Name!.Trim(), l.Qty, l.UnitPrice, l.LineTotal)).ToList())
        {
            ReadBy = "claude",
            SuggestedItemNames = lines.Select(l => string.IsNullOrWhiteSpace(l.MatchedItemName) ? null : l.MatchedItemName).ToList(),
        };
    }

    internal static string Prompt(IReadOnlyList<string> listItemNames)
    {
        var list = listItemNames.Count == 0 ? "(the list is empty)" : string.Join("\n", listItemNames.Take(300).Select(n => "- " + n));
        return $"""
            This is a photo or PDF of a shop receipt (often from India). Read it into the JSON schema.

            - One entry in "lines" per purchased product, in printed order. Leave out totals, tax summaries, payment and change lines.
              Bags, deposits and fees are lines too (they were paid for).
            - "raw_text": the line as printed. "name": a short everyday product name a family would write on a shopping list
              ("AMUL TAAZA TONED MLK 1L" -> "Toned milk 1L"); keep the size or variety when it helps tell products apart.
            - "qty": the number of units, or the weight if sold by weight. "unit_price": price per unit or per kg. "line_total": what was charged for the line.
            - Numbers are plain numbers without currency symbols or thousands separators. Use null for anything you can't read.
            - "bill_date" is YYYY-MM-DD. "discount" is a bill-wide discount as a positive number.
            - "matched_item_name": if the product is the same thing as an item on the household's list below, copy that item's
              name exactly; otherwise null. Match by meaning ("Toned milk 1L" matches "Milk"), never two different kinds of product.

            The household's list for this month:
            {list}
            """;
    }

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private static string ImageType(string mediaType) => mediaType is "image/png" or "image/webp" or "image/gif" ? mediaType : "image/jpeg";

    /// <summary>Structured-output schema: every field required, nullable ones as anyOf [type, null].</summary>
    internal static readonly Dictionary<string, JsonElement> Schema = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
        """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["store_name", "bill_date", "subtotal", "tax", "discount", "total", "lines"],
          "properties": {
            "store_name": { "anyOf": [{ "type": "string" }, { "type": "null" }] },
            "bill_date": { "anyOf": [{ "type": "string" }, { "type": "null" }] },
            "subtotal": { "anyOf": [{ "type": "number" }, { "type": "null" }] },
            "tax": { "anyOf": [{ "type": "number" }, { "type": "null" }] },
            "discount": { "anyOf": [{ "type": "number" }, { "type": "null" }] },
            "total": { "anyOf": [{ "type": "number" }, { "type": "null" }] },
            "lines": {
              "type": "array",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "required": ["raw_text", "name", "qty", "unit_price", "line_total", "matched_item_name"],
                "properties": {
                  "raw_text": { "type": "string" },
                  "name": { "type": "string" },
                  "qty": { "anyOf": [{ "type": "number" }, { "type": "null" }] },
                  "unit_price": { "anyOf": [{ "type": "number" }, { "type": "null" }] },
                  "line_total": { "anyOf": [{ "type": "number" }, { "type": "null" }] },
                  "matched_item_name": { "anyOf": [{ "type": "string" }, { "type": "null" }] }
                }
              }
            }
          }
        }
        """)!;

    private sealed record ParsedLine(string? RawText, string? Name, decimal? Qty, decimal? UnitPrice, decimal? LineTotal, string? MatchedItemName);

    private sealed record ParsedBill(string? StoreName, string? BillDate, decimal? Subtotal, decimal? Tax, decimal? Discount, decimal? Total, List<ParsedLine>? Lines);
}
