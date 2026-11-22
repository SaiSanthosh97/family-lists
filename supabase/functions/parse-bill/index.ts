// Supabase Edge Function: read an uploaded grocery bill with Claude and store
// the line items for review. Called from the app as
//   supabase.functions.invoke("parse-bill", { body: { bill_id } })
//
// It runs with the caller's own login, so row-level security decides whether
// they may read the bill and its photo. The Anthropic key lives only here
// (set with `supabase secrets set ANTHROPIC_API_KEY=...`).
import Anthropic from "npm:@anthropic-ai/sdk@1.12.0";
import { createClient } from "npm:@supabase/supabase-js@2.119.2";
import { encodeBase64 } from "jsr:@std/encoding@1/base64";

const MODEL = Deno.env.get("BILL_MODEL") ?? "claude-opus-5-5";

const CORS = {
  "Access-Control-Allow-Origin": "*",
  "Access-Control-Allow-Headers": "authorization, x-client-info, apikey, content-type",
  "Access-Control-Allow-Methods": "POST, OPTIONS",
};

const nullableNumber = { anyOf: [{ type: "number" }, { type: "null" }] };
const nullableString = { anyOf: [{ type: "string" }, { type: "null" }] };

const BILL_SCHEMA = {
  type: "object",
  additionalProperties: false,
  required: ["store_name", "bill_date", "currency", "subtotal", "tax", "discount", "total", "lines"],
  properties: {
    store_name: nullableString,
    bill_date: { anyOf: [{ type: "string", format: "date" }, { type: "null" }] },
    currency: { ...nullableString, description: "ISO 4217 code if it can be told from the bill" },
    subtotal: nullableNumber,
    tax: nullableNumber,
    discount: { ...nullableNumber, description: "Bill-level discount as a positive number" },
    total: nullableNumber,
    lines: {
      type: "array",
      items: {
        type: "object",
        additionalProperties: false,
        required: ["raw_text", "name", "qty", "unit_price", "line_total", "matched_item_name"],
        properties: {
          raw_text: { type: "string", description: "The line exactly as printed" },
          name: { type: "string", description: "Short everyday product name" },
          qty: nullableNumber,
          unit_price: nullableNumber,
          line_total: nullableNumber,
          matched_item_name: nullableString,
        },
      },
    },
  },
} as const;

interface ParsedLine {
  raw_text: string;
  name: string;
  qty: number | null;
  unit_price: number | null;
  line_total: number | null;
  matched_item_name: string | null;
}
interface ParsedBill {
  store_name: string | null;
  bill_date: string | null;
  currency: string | null;
  subtotal: number | null;
  tax: number | null;
  discount: number | null;
  total: number | null;
  lines: ParsedLine[];
}

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { ...CORS, "Content-Type": "application/json" },
  });
}

function mediaTypeFor(path: string, blobType: string) {
  if (blobType && blobType !== "application/octet-stream") return blobType;
  const ext = path.split(".").pop()?.toLowerCase();
  return ext === "pdf" ? "application/pdf" : ext === "png" ? "image/png" : ext === "webp" ? "image/webp" : "image/jpeg";
}

const num = (v: unknown) => (typeof v === "number" && Number.isFinite(v) ? Math.round(v * 1000) / 1000 : null);

function buildPrompt(listNames: string[]) {
  const list = listNames.length ? listNames.map((n) => `- ${n}`).join("\n") : "(the list is empty)";
  return `This is a photo or scan of a shopping receipt. Read it and extract every purchased product line.

Rules:
- One entry per purchased product line. Leave out subtotal, tax, total, payment, change, loyalty-points and other summary lines.
- If a discount is printed directly under a product, subtract it from that product's line_total. Put bill-wide discounts in "discount" as a positive number.
- "name" is a short, everyday product name a family would write on a shopping list (e.g. "AMUL TAAZA TONED MLK 1L" -> "Toned milk 1L", "BAN CAVENDISH" -> "Bananas"). Keep size or variety when it helps tell products apart.
- "qty" is the number of units, or the weight if sold by weight. "unit_price" is the price per unit or per kg. "line_total" is what was charged for the line.
- Numbers are plain numbers without currency symbols or thousands separators. Use null for anything you cannot read.
- "bill_date" is YYYY-MM-DD.
- "matched_item_name": if the product is the same thing as one of the items on the household's shopping list below, copy that list item's name exactly. Otherwise null. Match by meaning (e.g. "Toned milk 1L" matches "Milk"), but never match two different kinds of product.

The household's shopping list for this month:
${list}`;
}

Deno.serve(async (req) => {
  if (req.method === "OPTIONS") return new Response("ok", { headers: CORS });
  if (req.method !== "POST") return json({ error: "Method not allowed" }, 405);

  const authHeader = req.headers.get("Authorization");
  if (!authHeader) return json({ error: "Not signed in" }, 401);

  let billId: string | undefined;
  try {
    billId = (await req.json())?.bill_id;
  } catch {
    /* fall through */
  }
  if (!billId) return json({ error: "bill_id is required" }, 400);

  const supabase = createClient(Deno.env.get("SUPABASE_URL")!, Deno.env.get("SUPABASE_ANON_KEY")!, {
    global: { headers: { Authorization: authHeader } },
    auth: { persistSession: false },
  });

  // RLS: returns nothing unless the caller belongs to the bill's group.
  const { data: bill, error: billErr } = await supabase
    .from("bills")
    .select("id, period_id, storage_path, status")
    .eq("id", billId)
    .maybeSingle();
  if (billErr) return json({ error: billErr.message }, 500);
  if (!bill) return json({ error: "Bill not found" }, 404);
  if (bill.status === "confirmed") return json({ error: "Bill already confirmed" }, 409);

  const fail = async (message: string, status = 500) => {
    await supabase.from("bills").update({ status: "failed", error: message }).eq("id", bill.id);
    return json({ error: message }, status);
  };

  await supabase.from("bills").update({ status: "parsing", error: null }).eq("id", bill.id);

  const { data: file, error: dlErr } = await supabase.storage.from("bills").download(bill.storage_path);
  if (dlErr || !file) return fail(`Could not read the uploaded file: ${dlErr?.message ?? "missing"}`);

  const { data: items } = await supabase
    .from("items")
    .select("id, name")
    .eq("period_id", bill.period_id)
    .order("position");
  const listItems = items ?? [];

  const mediaType = mediaTypeFor(bill.storage_path, file.type);
  const data = encodeBase64(new Uint8Array(await file.arrayBuffer()));
  const fileBlock =
    mediaType === "application/pdf"
      ? ({ type: "document", source: { type: "base64", media_type: "application/pdf", data } } as const)
      : ({
          type: "image",
          source: { type: "base64", media_type: mediaType as "image/jpeg" | "image/png" | "image/webp", data },
        } as const);

  // The SDK throws in its constructor when the key is missing, so check first;
  // otherwise the bill would be left in "parsing" forever.
  const apiKey = Deno.env.get("ANTHROPIC_API_KEY");
  if (!apiKey) return fail("Bill reading isn't set up yet: the Anthropic API key is missing.", 500);
  const anthropic = new Anthropic({ apiKey });

  let parsed: ParsedBill;
  try {
    const response = await anthropic.beta.messages.create({
      model: MODEL,
      max_tokens: 16000,
      // If a safety classifier declines, retry on Anthropic's recommended fallback model.
      betas: ["server-side-fallback-2026-07-01"],
      fallbacks: "default",
      output_config: {
        effort: "medium",
        format: { type: "json_schema", schema: BILL_SCHEMA },
      },
      messages: [
        {
          role: "user",
          content: [fileBlock, { type: "text", text: buildPrompt(listItems.map((i) => i.name)) }],
        },
      ],
    });

    if (response.stop_reason === "refusal") return fail("The bill could not be read (request declined).", 422);
    if (response.stop_reason === "max_tokens") return fail("The bill is too long to read in one go.", 422);

    const text = response.content
      .map((block) => (block.type === "text" ? block.text : ""))
      .join("");
    parsed = JSON.parse(text) as ParsedBill;
    if (!Array.isArray(parsed?.lines)) throw new Error("Response had no lines");
  } catch (err) {
    if (err instanceof Anthropic.RateLimitError) return fail("Too many bills at once — try again in a minute.", 429);
    if (err instanceof Anthropic.AuthenticationError) return fail("The Anthropic API key is missing or invalid.", 500);
    if (err instanceof Anthropic.APIError) return fail(`Bill reading service error: ${err.message}`, 502);
    if (err instanceof SyntaxError) return fail("Could not understand the bill reader's answer.", 502);
    return fail(err instanceof Error ? err.message : String(err));
  }

  // Map Claude's suggested list match back to item ids.
  const byName = new Map(listItems.map((i) => [i.name.trim().toLowerCase(), i.id]));
  const lines = parsed.lines
    .filter((l) => l && typeof l.name === "string" && l.name.trim())
    .map((l, index) => {
      const matched = l.matched_item_name ? byName.get(l.matched_item_name.trim().toLowerCase()) ?? null : null;
      return {
        bill_id: bill.id,
        position: index + 1,
        raw_text: l.raw_text ?? null,
        name: l.name.trim().slice(0, 120),
        qty: num(l.qty),
        unit_price: num(l.unit_price),
        line_total: num(l.line_total),
        matched_item_id: matched,
        action: matched ? "match" : "add",
      };
    });

  // Re-parsing replaces earlier lines.
  await supabase.from("bill_lines").delete().eq("bill_id", bill.id);
  if (lines.length) {
    const { error: insErr } = await supabase.from("bill_lines").insert(lines);
    if (insErr) return fail(`Could not save bill lines: ${insErr.message}`);
  }

  const validDate = parsed.bill_date && /^\d{4}-\d{2}-\d{2}$/.test(parsed.bill_date) ? parsed.bill_date : null;
  const { error: updErr } = await supabase
    .from("bills")
    .update({
      status: "parsed",
      error: null,
      store_name: parsed.store_name?.slice(0, 120) ?? null,
      bill_date: validDate,
      subtotal: num(parsed.subtotal),
      tax: num(parsed.tax),
      discount: num(parsed.discount),
      total: num(parsed.total),
      raw_json: parsed,
    })
    .eq("id", bill.id);
  if (updErr) return fail(updErr.message);

  return json({ ok: true, lines: lines.length });
});
