import { useEffect, useMemo, useState } from "react";
import { Navigate, useNavigate, useParams } from "react-router-dom";
import { useQuery } from "@tanstack/react-query";
import { supabase } from "../lib/supabase";
import { useGroup } from "../api/groups";
import { useItems } from "../api/lists";
import {
  useBill,
  useBillPhotoUrl,
  useConfirmBill,
  useDeleteBill,
  useRetryParse,
  type ReviewedBill,
  type ReviewedLine,
} from "../api/bills";
import { must } from "../api/util";
import { billDifference, formatMoney, sumMoney, toNumber } from "../lib/money";
import { rankMatches, suggestMatch } from "../lib/billMatch";
import type { Bill, BillLine, Group, Item, Period } from "../lib/types";
import { periodLabel } from "../lib/period";
import { Badge, BottomBar, Button, Card, ErrorBox, Field, Header, Input, Page, PageSpinner, Spinner } from "../components/ui";

export default function BillReview() {
  const { groupId = "", billId = "" } = useParams();
  const group = useGroup(groupId);
  const bill = useBill(billId);
  const period = useQuery({
    queryKey: ["period", bill.data?.bill.period_id],
    enabled: !!bill.data,
    queryFn: async () =>
      must(await supabase.from("list_periods").select("*").eq("id", bill.data!.bill.period_id).single()) as Period,
  });

  if (bill.isLoading || group.isLoading || (bill.data && period.isLoading)) return <PageSpinner />;
  if (!bill.data || !group.data || !period.data) return <Navigate to={`/g/${groupId}`} replace />;

  const back = `/g/${groupId}/l/${period.data.list_id}?m=${period.data.period}`;
  const b = bill.data.bill;

  return (
    <>
      <Header title={b.store_name ?? "Bill"} subtitle={periodLabel(period.data.period)} back={back} />
      <Page>
        {b.status === "uploaded" || b.status === "parsing" ? (
          <Card className="space-y-3 py-10 text-center">
            <Spinner />
            <p className="font-semibold">Reading your bill…</p>
            <p className="text-sm text-stone-600">This usually takes 10–30 seconds. You can leave this page; it carries on.</p>
          </Card>
        ) : b.status === "failed" ? (
          <FailedBill bill={b} back={back} />
        ) : (
          <ReviewForm
            key={b.id + b.status}
            bill={b}
            lines={bill.data.lines}
            period={period.data}
            group={group.data}
            back={back}
          />
        )}
      </Page>
    </>
  );
}

function FailedBill({ bill, back }: { bill: Bill; back: string }) {
  const retry = useRetryParse(bill.id);
  const del = useDeleteBill(bill);
  const navigate = useNavigate();
  return (
    <Card className="space-y-4">
      <p className="font-semibold">We couldn't read this bill</p>
      <p className="text-sm text-stone-600">{bill.error ?? "Something went wrong."}</p>
      <p className="text-sm text-stone-600">
        Try again, take a sharper photo in good light, or skip it and type the total in by hand on the month's page.
      </p>
      <PhotoLink path={bill.storage_path} />
      <div className="flex flex-wrap gap-2">
        <Button busy={retry.isPending} onClick={() => retry.mutate()}>
          Try again
        </Button>
        <Button variant="danger" busy={del.isPending} onClick={() => del.mutate(undefined, { onSuccess: () => navigate(back) })}>
          Delete this bill
        </Button>
      </div>
      <ErrorBox error={retry.error ?? del.error} />
    </Card>
  );
}

function PhotoLink({ path }: { path: string }) {
  const url = useBillPhotoUrl(path);
  if (!url.data) return null;
  return (
    <a href={url.data} target="_blank" rel="noreferrer" className="inline-block text-sm font-medium text-brand-700">
      View the uploaded bill ↗
    </a>
  );
}

type Choice = "add" | "ignore" | `item:${string}`;

interface Row {
  id: string;
  raw_text: string | null;
  name: string;
  qty: string;
  unit_price: string;
  line_total: string;
  choice: Choice;
}

const str = (v: number | null) => (v === null || v === undefined ? "" : String(v));

function ReviewForm({ bill, lines, period, group, back }: { bill: Bill; lines: BillLine[]; period: Period; group: Group; back: string }) {
  const items = useItems(period.list_id, period.id);
  const confirm = useConfirmBill(bill, period.list_id);
  const del = useDeleteBill(bill);
  const navigate = useNavigate();
  const readOnly = bill.status === "confirmed";
  const currency = group.currency;
  const listItems = items.data ?? [];

  const [head, setHead] = useState({
    store_name: bill.store_name ?? "",
    bill_date: bill.bill_date ?? "",
    tax: str(bill.tax),
    discount: str(bill.discount),
    total: str(bill.total),
  });
  const [rows, setRows] = useState<Row[]>([]);
  const [ready, setReady] = useState(false);

  // Build editable rows once the list's items are known (for fallback matching).
  useEffect(() => {
    if (ready || items.isLoading) return;
    const unclaimed = (items.data ?? []).filter((i) => !lines.some((l) => l.matched_item_id === i.id));
    setRows(
      lines.map((l) => {
        let choice: Choice = l.action === "ignore" ? "ignore" : l.matched_item_id ? `item:${l.matched_item_id}` : "add";
        if (!readOnly && choice === "add") {
          const guess = suggestMatch(l.name, unclaimed);
          if (guess) choice = `item:${guess.id}`;
        }
        return {
          id: l.id,
          raw_text: l.raw_text,
          name: l.name,
          qty: str(l.qty),
          unit_price: str(l.unit_price),
          line_total: str(l.line_total),
          choice,
        };
      }),
    );
    setReady(true);
  }, [items.isLoading, items.data, lines, ready, readOnly]);

  const setRow = (id: string, patch: Partial<Row>) => setRows((rs) => rs.map((r) => (r.id === id ? { ...r, ...patch } : r)));

  const linesSum = sumMoney(rows.map((r) => r.line_total));
  const diff = billDifference(
    rows.map((r) => ({ line_total: r.line_total, action: r.choice })),
    { total: head.total, discount: head.discount },
  );
  const counts = {
    matched: rows.filter((r) => r.choice.startsWith("item:")).length,
    added: rows.filter((r) => r.choice === "add").length,
    ignored: rows.filter((r) => r.choice === "ignore").length,
  };

  function submit() {
    const reviewedBill: ReviewedBill = {
      store_name: head.store_name.trim() || null,
      bill_date: head.bill_date || null,
      subtotal: toNumber(bill.subtotal),
      tax: toNumber(head.tax),
      discount: toNumber(head.discount),
      total: toNumber(head.total) ?? linesSum,
    };
    const reviewedLines: ReviewedLine[] = rows.map((r) => ({
      id: r.id,
      name: r.name.trim() || r.raw_text || "Item",
      qty: toNumber(r.qty),
      unit_price: toNumber(r.unit_price),
      line_total: toNumber(r.line_total),
      action: r.choice === "add" ? "add" : r.choice === "ignore" ? "ignore" : "match",
      matched_item_id: r.choice.startsWith("item:") ? r.choice.slice(5) : null,
    }));
    confirm.mutate({ bill: reviewedBill, lines: reviewedLines }, { onSuccess: () => navigate(back) });
  }

  if (!ready) return <PageSpinner />;

  return (
    <>
      {readOnly ? (
        <Card className="mb-4 border-brand-200 bg-brand-50 text-sm text-brand-800">
          This bill has been added to {periodLabel(period.period)}. Prices are on the items, and new products were added
          to the list.
        </Card>
      ) : (
        <p className="mb-4 text-sm text-stone-600">
          Check what was read from the bill. Products that aren't on your list are added to it, so next month's list
          includes them.
        </p>
      )}

      <Card className="mb-4 space-y-3">
        <div className="grid grid-cols-2 gap-3">
          <Field label="Store">
            <Input disabled={readOnly} value={head.store_name} onChange={(e) => setHead({ ...head, store_name: e.target.value })} />
          </Field>
          <Field label="Date">
            <Input disabled={readOnly} type="date" value={head.bill_date} onChange={(e) => setHead({ ...head, bill_date: e.target.value })} />
          </Field>
          <Field label={`Total (${currency})`}>
            <Input disabled={readOnly} inputMode="decimal" value={head.total} onChange={(e) => setHead({ ...head, total: e.target.value })} />
          </Field>
          <Field label="Bill discount">
            <Input disabled={readOnly} inputMode="decimal" value={head.discount} onChange={(e) => setHead({ ...head, discount: e.target.value })} placeholder="0" />
          </Field>
        </div>
        {diff !== null && Math.abs(diff) >= 0.01 && (
          <p className="rounded-lg bg-amber-50 px-3 py-2 text-sm text-amber-900">
            The lines add up to {formatMoney(linesSum, currency)}, which is {formatMoney(Math.abs(diff), currency)}{" "}
            {diff > 0 ? "less" : "more"} than the total. Check for a misread price, or a tax/fee line.
          </p>
        )}
        <PhotoLink path={bill.storage_path} />
      </Card>

      <div className="mb-2 flex flex-wrap gap-2 text-xs">
        <Badge tone="green">{counts.matched} on your list</Badge>
        <Badge tone="blue">{counts.added} new to add</Badge>
        {counts.ignored > 0 && <Badge>{counts.ignored} skipped</Badge>}
      </div>

      <div className="space-y-2">
        {rows.map((r) => (
          <LineEditor key={r.id} row={r} items={listItems} readOnly={readOnly} currency={currency} onChange={(p) => setRow(r.id, p)} />
        ))}
        {rows.length === 0 && <Card className="text-center text-stone-500">No product lines were found on this bill.</Card>}
      </div>

      <ErrorBox error={confirm.error ?? del.error} />

      {!readOnly && (
        <>
          <div className="mt-6">
            <Button variant="danger" busy={del.isPending} onClick={() => window.confirm("Delete this bill?") && del.mutate(undefined, { onSuccess: () => navigate(back) })}>
              Delete bill
            </Button>
          </div>
          <BottomBar>
            <Button className="flex-1" busy={confirm.isPending} onClick={submit}>
              Add to {periodLabel(period.period, "short")} · {formatMoney(toNumber(head.total) ?? linesSum, currency)}
            </Button>
          </BottomBar>
        </>
      )}
    </>
  );
}

function LineEditor({
  row,
  items,
  readOnly,
  currency,
  onChange,
}: {
  row: Row;
  items: Item[];
  readOnly: boolean;
  currency: string;
  onChange: (patch: Partial<Row>) => void;
}) {
  const ranked = useMemo(() => rankMatches(row.name, items), [row.name, items]);
  const ignored = row.choice === "ignore";
  return (
    <Card className={`space-y-2 py-3 ${ignored ? "opacity-60" : ""}`}>
      <div className="flex items-start gap-2">
        <div className="min-w-0 flex-1">
          <Input
            disabled={readOnly}
            value={row.name}
            onChange={(e) => onChange({ name: e.target.value })}
            aria-label="Product name"
            className="py-1.5 font-medium"
          />
          {row.raw_text && row.raw_text !== row.name && (
            <div className="mt-1 truncate font-mono text-xs text-stone-400">{row.raw_text}</div>
          )}
        </div>
        <div className="w-28 shrink-0">
          <Input
            disabled={readOnly}
            inputMode="decimal"
            value={row.line_total}
            onChange={(e) => onChange({ line_total: e.target.value })}
            aria-label={`Amount (${currency})`}
            className="py-1.5 text-right"
          />
        </div>
      </div>
      <div className="flex items-center gap-2">
        <div className="w-20 shrink-0">
          <Input
            disabled={readOnly}
            inputMode="decimal"
            value={row.qty}
            onChange={(e) => onChange({ qty: e.target.value })}
            aria-label="Quantity"
            placeholder="Qty"
            className="py-1.5 text-sm"
          />
        </div>
        <select
          disabled={readOnly}
          value={row.choice}
          onChange={(e) => onChange({ choice: e.target.value as Choice })}
          aria-label="What is this line?"
          className={`min-w-0 flex-1 rounded-xl border px-2 py-2 text-sm ${
            row.choice === "add" ? "border-sky-300 bg-sky-50" : row.choice === "ignore" ? "border-stone-300 bg-stone-50" : "border-brand-200 bg-brand-50"
          }`}
        >
          <option value="add">➕ New — add to the list</option>
          <option value="ignore">Skip (bag, deposit, fee…)</option>
          <optgroup label="Same as list item">
            {ranked.map((i) => (
              <option key={i.id} value={`item:${i.id}`}>
                ✓ {i.name}
              </option>
            ))}
          </optgroup>
        </select>
      </div>
    </Card>
  );
}
