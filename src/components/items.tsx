import { useEffect, useState, type FormEvent, type ReactNode } from "react";
import type { Item } from "../lib/types";
import { formatMoney, toNumber } from "../lib/money";
import { Badge, Button, ErrorBox, Field, Input, Sheet } from "./ui";

export function qtyLabel(item: Pick<Item, "qty" | "unit">) {
  const q = Number(item.qty);
  if (item.unit) return `${q} ${item.unit}`;
  return q === 1 ? "" : `×${q}`;
}

// The Add button is never disabled while a save is in flight: browsers ignore
// Enter in a form whose submit button is disabled, which drops quick entries.
export function AddItemBar({ onAdd, placeholder = "Add an item…" }: { onAdd: (name: string, qty: number) => void; placeholder?: string }) {
  const [text, setText] = useState("");

  function submit(e: FormEvent) {
    e.preventDefault();
    const raw = text.trim();
    if (!raw) return;
    // "3 onions" or "onions x3" → quantity 3
    const lead = raw.match(/^(\d+(?:\.\d+)?)\s+(.+)$/);
    const trail = raw.match(/^(.+?)\s*[x×]\s*(\d+(?:\.\d+)?)$/i);
    const [name, qty] = lead ? [lead[2], Number(lead[1])] : trail ? [trail[1], Number(trail[2])] : [raw, 1];
    onAdd(name, qty);
    setText("");
  }

  return (
    <form onSubmit={submit} className="flex gap-2">
      <Input value={text} onChange={(e) => setText(e.target.value)} placeholder={placeholder} maxLength={120} enterKeyHint="done" />
      <Button type="submit" disabled={!text.trim()} aria-label="Add item">
        Add
      </Button>
    </form>
  );
}

export function Check({ checked, tone = "green" }: { checked: boolean; tone?: "green" | "stone" }) {
  return (
    <span
      aria-hidden
      className={`flex h-6 w-6 shrink-0 items-center justify-center rounded-md border-2 transition-colors ${
        checked ? (tone === "green" ? "border-brand-600 bg-brand-600 text-white" : "border-stone-400 bg-stone-400 text-white") : "border-stone-300 bg-white"
      }`}
    >
      {checked && (
        <svg width="14" height="14" viewBox="0 0 14 14">
          <path d="m3 7 3 3 5-6" stroke="currentColor" strokeWidth="2.2" fill="none" strokeLinecap="round" strokeLinejoin="round" />
        </svg>
      )}
    </span>
  );
}

/** A row with a tappable checkbox area and a tappable details area. */
export function ItemRow({
  item,
  checked,
  onToggle,
  onOpen,
  strike,
  currency,
  showPrice,
  extra,
  toggleLabel,
}: {
  item: Item;
  checked: boolean;
  onToggle: () => void;
  onOpen: () => void;
  strike?: boolean;
  currency?: string;
  showPrice?: boolean;
  extra?: ReactNode;
  toggleLabel: string;
}) {
  const qty = qtyLabel(item);
  return (
    <li className="flex items-stretch border-b border-stone-100 last:border-0">
      <button type="button" onClick={onToggle} aria-pressed={checked} aria-label={`${toggleLabel}: ${item.name}`} className="flex items-center py-3 pr-3 pl-1">
        <Check checked={checked} />
      </button>
      <button type="button" onClick={onOpen} className="flex min-w-0 flex-1 items-center gap-2 py-3 text-left">
        <span className={`min-w-0 flex-1 ${strike ? "text-stone-400 line-through" : ""}`}>
          <span className="block truncate">{item.name}</span>
          {(item.notes || extra) && (
            <span className="mt-0.5 flex flex-wrap items-center gap-1 text-xs text-stone-500">
              {extra}
              {item.notes && <span className="truncate">{item.notes}</span>}
            </span>
          )}
        </span>
        {qty && <span className="shrink-0 text-sm text-stone-500">{qty}</span>}
        {showPrice && currency && item.price !== null && (
          <span className="shrink-0 text-sm font-medium text-stone-700">{formatMoney(Number(item.price), currency)}</span>
        )}
      </button>
    </li>
  );
}

export function SourceBadge({ item }: { item: Item }) {
  if (item.skipped_last) return <Badge tone="amber">skipped last month</Badge>;
  if (item.source === "bill") return <Badge tone="blue">from bill</Badge>;
  if (item.source === "added") return <Badge tone="green">new</Badge>;
  return null;
}

export function ItemEditor({
  item,
  onClose,
  onSave,
  onDelete,
  saving,
  error,
  currency,
  monthly,
  onMove,
}: {
  item: Item | null;
  onClose: () => void;
  onSave: (patch: Partial<Item>) => void;
  onDelete: () => void;
  saving?: boolean;
  error?: unknown;
  currency?: string;
  monthly: boolean;
  onMove?: (dir: -1 | 1) => void;
}) {
  const [name, setName] = useState("");
  const [qty, setQty] = useState("1");
  const [unit, setUnit] = useState("");
  const [price, setPrice] = useState("");
  const [notes, setNotes] = useState("");

  useEffect(() => {
    if (!item) return;
    setName(item.name);
    setQty(String(Number(item.qty)));
    setUnit(item.unit ?? "");
    setPrice(item.price === null ? "" : String(item.price));
    setNotes(item.notes ?? "");
  }, [item]);

  if (!item) return null;

  function submit(e: FormEvent) {
    e.preventDefault();
    const q = toNumber(qty);
    onSave({
      name: name.trim() || item!.name,
      qty: q && q > 0 ? q : 1,
      unit: unit.trim() || null,
      notes: notes.trim() || null,
      ...(monthly ? { price: toNumber(price) } : {}),
    });
  }

  return (
    <Sheet open onClose={onClose} title="Edit item">
      <form onSubmit={submit} className="space-y-4">
        <Field label="Name">
          <Input value={name} onChange={(e) => setName(e.target.value)} maxLength={120} required />
        </Field>
        <div className="grid grid-cols-2 gap-3">
          <Field label="Quantity">
            <Input inputMode="decimal" value={qty} onChange={(e) => setQty(e.target.value)} />
          </Field>
          <Field label="Unit (optional)">
            <Input value={unit} onChange={(e) => setUnit(e.target.value)} placeholder="kg, L, packs" maxLength={20} />
          </Field>
        </div>
        {monthly && (
          <Field label={`Amount spent${currency ? ` (${currency})` : ""}`} hint="Optional. Filled in for you when you upload the bill.">
            <Input inputMode="decimal" value={price} onChange={(e) => setPrice(e.target.value)} placeholder="0.00" />
          </Field>
        )}
        <Field label="Notes (optional)">
          <Input value={notes} onChange={(e) => setNotes(e.target.value)} placeholder="Brand, size, where to buy…" maxLength={200} />
        </Field>
        {onMove && (
          <div className="flex gap-2">
            <Button type="button" variant="secondary" className="flex-1" onClick={() => onMove(-1)}>
              Move up
            </Button>
            <Button type="button" variant="secondary" className="flex-1" onClick={() => onMove(1)}>
              Move down
            </Button>
          </div>
        )}
        <ErrorBox error={error} />
        <div className="flex gap-2">
          <Button
            type="button"
            variant="danger"
            onClick={() => {
              const msg = monthly
                ? `Remove "${item.name}" for good? It won't be copied to next month.`
                : `Delete "${item.name}"?`;
              if (confirm(msg)) onDelete();
            }}
          >
            {monthly ? "Remove for good" : "Delete"}
          </Button>
          <Button type="submit" className="flex-1" busy={saving}>
            Save
          </Button>
        </div>
      </form>
    </Sheet>
  );
}
