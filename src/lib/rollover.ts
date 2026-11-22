// Builds next month's draft list from the most recent month's items.
import { normalizeName } from "./billMatch";

export interface CarrySource {
  name: string;
  qty: number;
  unit: string | null;
  notes: string | null;
  included: boolean;
  position: number;
}

export interface CarriedItem {
  name: string;
  qty: number;
  unit: string | null;
  notes: string | null;
  included: true;
  bought: false;
  source: "carried";
  skipped_last: boolean;
  price: null;
  position: number;
}

/**
 * Everything from last month comes forward, including things found on the
 * bill. Items marked "not needed" last month still come forward (needs change
 * month to month) but are flagged and listed last. Duplicate names collapse
 * into one. Prices are not copied: spending is tracked per month.
 */
export function buildCarryOver(previous: CarrySource[]): CarriedItem[] {
  const ordered = [...previous].sort((a, b) => a.position - b.position);
  const seen = new Map<string, CarriedItem>();

  for (const item of ordered) {
    const name = item.name.trim();
    if (!name) continue;
    const key = normalizeName(name) || name.toLowerCase();
    const existing = seen.get(key);
    if (existing) {
      if (item.included) existing.skipped_last = false;
      continue;
    }
    seen.set(key, {
      name,
      qty: item.qty > 0 ? item.qty : 1,
      unit: item.unit,
      notes: item.notes,
      included: true,
      bought: false,
      source: "carried",
      skipped_last: !item.included,
      price: null,
      position: 0,
    });
  }

  const result = [...seen.values()].sort((a, b) => Number(a.skipped_last) - Number(b.skipped_last));
  result.forEach((item, i) => (item.position = i + 1));
  return result;
}
