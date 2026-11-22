export function formatMoney(amount: number | null | undefined, currency: string): string {
  if (amount === null || amount === undefined || Number.isNaN(amount)) return "—";
  try {
    return new Intl.NumberFormat(undefined, { style: "currency", currency }).format(amount);
  } catch {
    return `${currency} ${amount.toFixed(2)}`;
  }
}

export function toNumber(v: unknown): number | null {
  if (v === null || v === undefined || v === "") return null;
  const n = typeof v === "number" ? v : Number(String(v).replace(/,/g, ""));
  return Number.isFinite(n) ? n : null;
}

/** Sum that stays exact to the cent. */
export function sumMoney(values: Array<number | string | null | undefined>): number {
  const cents = values.reduce<number>((acc, v) => acc + Math.round((toNumber(v) ?? 0) * 100), 0);
  return cents / 100;
}

export interface SpendInputs {
  bills: Array<{ status: string; total: number | string | null }>;
  manualTotal: number | string | null;
  itemPrices: Array<number | string | null>;
}

export type SpendSource = "bills" | "manual" | "items" | "none";

/**
 * What a month cost: confirmed bill totals if there are any, otherwise a total
 * typed in by hand, otherwise the sum of prices typed on items.
 */
export function monthSpend({ bills, manualTotal, itemPrices }: SpendInputs): {
  total: number | null;
  source: SpendSource;
} {
  const confirmed = bills.filter((b) => b.status === "confirmed" && toNumber(b.total) !== null);
  if (confirmed.length) return { total: sumMoney(confirmed.map((b) => b.total)), source: "bills" };
  const manual = toNumber(manualTotal);
  if (manual !== null) return { total: manual, source: "manual" };
  const priced = itemPrices.filter((p) => toNumber(p) !== null);
  if (priced.length) return { total: sumMoney(priced), source: "items" };
  return { total: null, source: "none" };
}

/**
 * Printed total minus what the kept lines add up to (after bill-wide
 * discount). Non-zero usually means a line was misread or tax is separate.
 */
export function billDifference(
  lines: Array<{ line_total: number | string | null; action: string }>,
  bill: { total: number | string | null; discount: number | string | null },
): number | null {
  const total = toNumber(bill.total);
  if (total === null) return null;
  const linesSum = sumMoney(lines.map((l) => l.line_total));
  const expected = sumMoney([linesSum, -(toNumber(bill.discount) ?? 0)]);
  return sumMoney([total, -expected]);
}
