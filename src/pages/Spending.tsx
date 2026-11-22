import { useMemo, useState } from "react";
import { Navigate, useParams, useSearchParams } from "react-router-dom";
import { useGroup } from "../api/groups";
import { useList } from "../api/lists";
import { useSpending, type MonthSpending } from "../api/spending";
import { normalizeName } from "../lib/billMatch";
import { formatMoney, sumMoney, toNumber } from "../lib/money";
import { periodLabel } from "../lib/period";
import { Card, Empty, ErrorBox, Header, Page, PageSpinner } from "../components/ui";

export default function Spending() {
  const { groupId = "", listId = "" } = useParams();
  const [params, setParams] = useSearchParams();
  const group = useGroup(groupId);
  const list = useList(listId);
  const spending = useSpending(listId);

  if (group.isLoading || list.isLoading || spending.isLoading) return <PageSpinner />;
  if (!group.data || !list.data) return <Navigate to={`/g/${groupId}`} replace />;

  const months = spending.data ?? [];
  const currency = group.data.currency;
  const selected = months.find((m) => m.period.period === params.get("m")) ?? [...months].reverse().find((m) => m.total !== null) ?? months[months.length - 1];

  return (
    <>
      <Header title="Spending" subtitle={list.data.name} back={`/g/${groupId}/l/${listId}`} />
      <Page>
        <ErrorBox error={spending.error} />
        {months.length === 0 ? (
          <Empty title="Nothing yet">Spending shows up here once you've shopped and added a bill or a total.</Empty>
        ) : (
          <div className="space-y-6">
            <MonthChart months={months.slice(-12)} currency={currency} selected={selected?.period.period} onSelect={(p) => setParams({ m: p }, { replace: true })} />
            {selected && <MonthBreakdown month={selected} currency={currency} />}
            <ProductHistory months={months} currency={currency} />
          </div>
        )}
      </Page>
    </>
  );
}

function MonthChart({
  months,
  currency,
  selected,
  onSelect,
}: {
  months: MonthSpending[];
  currency: string;
  selected?: string;
  onSelect: (period: string) => void;
}) {
  const max = Math.max(...months.map((m) => m.total ?? 0), 1);
  const withTotals = months.filter((m) => m.total !== null);
  const average = withTotals.length ? sumMoney(withTotals.map((m) => m.total)) / withTotals.length : null;

  return (
    <Card>
      <div className="mb-3 flex items-baseline justify-between">
        <h2 className="font-semibold">By month</h2>
        {average !== null && <span className="text-sm text-stone-500">Average {formatMoney(average, currency)}</span>}
      </div>
      <div className="flex h-40 items-end gap-1.5" role="list">
        {months.map((m) => {
          const h = m.total ? Math.max(4, (m.total / max) * 100) : 0;
          const active = m.period.period === selected;
          return (
            <button
              key={m.period.id}
              role="listitem"
              onClick={() => onSelect(m.period.period)}
              className="group flex h-full min-w-0 flex-1 flex-col items-center justify-end gap-1"
              aria-label={`${periodLabel(m.period.period)}: ${m.total === null ? "no total" : formatMoney(m.total, currency)}`}
            >
              <div
                className={`w-full max-w-10 rounded-t-md transition-colors ${active ? "bg-brand-700" : "bg-brand-200 group-hover:bg-brand-500"} ${m.total === null ? "border border-dashed border-stone-300 bg-transparent" : ""}`}
                style={{ height: m.total === null ? "4px" : `${h}%` }}
              />
              <span className={`text-[11px] ${active ? "font-semibold text-stone-900" : "text-stone-500"}`}>
                {periodLabel(m.period.period, "short").split(" ")[0]}
              </span>
            </button>
          );
        })}
      </div>
    </Card>
  );
}

function MonthBreakdown({ month, currency }: { month: MonthSpending; currency: string }) {
  const priced = month.items
    .filter((i) => i.price !== null)
    .map((i) => ({ id: i.id, name: i.name, qty: Number(i.qty), unit: i.unit, amount: Number(i.price) }));
  const others = month.otherLines
    .filter((l) => toNumber(l.line_total) !== null)
    .map((l) => ({ id: l.id, name: l.name, qty: null as number | null, unit: null, amount: Number(l.line_total) }));
  const rows = [...priced, ...others].sort((a, b) => b.amount - a.amount);
  const rowsTotal = sumMoney(rows.map((r) => r.amount));
  const unpriced = month.items.filter((i) => i.price === null && i.bought).length;
  const base = month.total ?? rowsTotal;
  const sourceText = { bills: "from bills", manual: "entered by hand", items: "sum of item prices", none: "no total yet" }[month.source];

  return (
    <Card>
      <div className="mb-4">
        <div className="text-sm text-stone-500">{periodLabel(month.period.period)}</div>
        <div className="text-3xl font-bold">{month.total === null ? "—" : formatMoney(month.total, currency)}</div>
        <div className="text-xs text-stone-500">{sourceText}</div>
      </div>

      {rows.length === 0 ? (
        <p className="text-sm text-stone-500">
          No per-product prices for this month. Upload the bill, or open items and type what they cost.
        </p>
      ) : (
        <ul className="space-y-2.5">
          {rows.map((r) => (
            <li key={r.id}>
              <div className="flex items-baseline justify-between gap-3 text-sm">
                <span className="min-w-0 truncate">
                  {r.name}
                  {r.qty !== null && r.qty !== 1 && <span className="text-stone-500"> · {r.unit ? `${r.qty} ${r.unit}` : `×${r.qty}`}</span>}
                </span>
                <span className="shrink-0 font-medium tabular-nums">{formatMoney(r.amount, currency)}</span>
              </div>
              <div className="mt-1 h-1.5 overflow-hidden rounded-full bg-stone-100">
                <div className="h-full rounded-full bg-brand-500" style={{ width: `${base ? Math.min(100, (r.amount / base) * 100) : 0}%` }} />
              </div>
            </li>
          ))}
        </ul>
      )}

      {(unpriced > 0 || (month.total !== null && rows.length > 0 && Math.abs(month.total - rowsTotal) >= 0.01)) && (
        <div className="mt-4 space-y-1 border-t border-stone-100 pt-3 text-xs text-stone-500">
          {rows.length > 0 && month.total !== null && Math.abs(month.total - rowsTotal) >= 0.01 && (
            <p>
              Products add up to {formatMoney(rowsTotal, currency)}; the difference ({formatMoney(month.total - rowsTotal, currency)}) is
              tax, discounts or unpriced items.
            </p>
          )}
          {unpriced > 0 && <p>{unpriced} bought item(s) have no price.</p>}
        </div>
      )}
    </Card>
  );
}

function ProductHistory({ months, currency }: { months: MonthSpending[]; currency: string }) {
  const products = useMemo(() => {
    const byKey = new Map<string, { name: string; points: Array<{ period: string; amount: number; qty: number }> }>();
    for (const m of months) {
      for (const i of m.items) {
        if (i.price === null) continue;
        const key = normalizeName(i.name) || i.name.toLowerCase();
        const entry = byKey.get(key) ?? { name: i.name, points: [] };
        entry.points.push({ period: m.period.period, amount: Number(i.price), qty: Number(i.qty) });
        byKey.set(key, entry);
      }
    }
    return [...byKey.entries()]
      .map(([key, v]) => ({ key, ...v, total: sumMoney(v.points.map((p) => p.amount)) }))
      .sort((a, b) => b.total - a.total);
  }, [months]);
  const [key, setKey] = useState<string>("");
  const chosen = products.find((p) => p.key === key) ?? products[0];

  if (!products.length) return null;

  return (
    <Card>
      <h2 className="mb-3 font-semibold">Product history</h2>
      <select
        value={chosen?.key}
        onChange={(e) => setKey(e.target.value)}
        className="mb-3 w-full rounded-xl border border-stone-300 bg-white px-3 py-2.5"
        aria-label="Product"
      >
        {products.map((p) => (
          <option key={p.key} value={p.key}>
            {p.name} — {formatMoney(p.total, currency)} total
          </option>
        ))}
      </select>
      {chosen && (
        <ul className="divide-y divide-stone-100 text-sm">
          {[...chosen.points].reverse().map((p) => (
            <li key={p.period} className="flex justify-between py-2">
              <span>{periodLabel(p.period)}</span>
              <span className="tabular-nums">
                {formatMoney(p.amount, currency)}
                {p.qty !== 1 && <span className="text-stone-500"> · {p.qty} units</span>}
              </span>
            </li>
          ))}
        </ul>
      )}
    </Card>
  );
}
