import { useMemo, useRef, useState, type ReactNode } from "react";
import { Link, useNavigate, useSearchParams } from "react-router-dom";
import { useItemMutations, useItems, usePeriods, useStartPeriod, useUpdatePeriod } from "../api/lists";
import { useBills, useUploadBill } from "../api/bills";
import { currentPeriod, monthName, periodLabel, shiftPeriod } from "../lib/period";
import { formatMoney, monthSpend, toNumber } from "../lib/money";
import type { Bill, Group, Item, List, Period } from "../lib/types";
import { AddItemBar, ItemEditor, ItemRow, SourceBadge } from "../components/items";
import {
  Badge,
  BottomBar,
  Button,
  Card,
  ChartIcon,
  Empty,
  ErrorBox,
  Field,
  Header,
  IconButton,
  Input,
  Page,
  PageSpinner,
  Sheet,
} from "../components/ui";
import { DotsIcon, ListMenu } from "../components/ListMenu";

export default function MonthView({ list, group }: { list: List; group: Group }) {
  const [params, setParams] = useSearchParams();
  const now = currentPeriod();
  const selected = params.get("m") ?? now;
  const periods = usePeriods(list.id);
  const [menu, setMenu] = useState(false);

  const period = periods.data?.find((p) => p.period === selected);
  const earliest = periods.data?.[periods.data.length - 1]?.period ?? now;
  const canGoBack = selected > earliest;
  const canGoForward = selected < now;
  const go = (delta: number) => setParams({ m: shiftPeriod(selected, delta) }, { replace: true });

  return (
    <>
      <Header
        title={list.name}
        subtitle={periodLabel(selected)}
        back={`/g/${group.id}`}
        right={
          <>
            <IconButton label="Spending" to={`/g/${group.id}/l/${list.id}/spending`}>
              <ChartIcon />
            </IconButton>
            <IconButton label="List options" onClick={() => setMenu(true)}>
              <DotsIcon />
            </IconButton>
          </>
        }
      />
      <Page>
        <div className="mb-4 flex items-center justify-between rounded-2xl bg-white px-2 py-1.5 shadow-sm ring-1 ring-stone-200">
          <Button variant="ghost" className="px-3" disabled={!canGoBack} onClick={() => go(-1)} aria-label="Previous month">
            ‹
          </Button>
          <div className="text-center">
            <div className="font-semibold">{periodLabel(selected)}</div>
            {period && <StatusLabel status={period.status} />}
          </div>
          <Button variant="ghost" className="px-3" disabled={!canGoForward} onClick={() => go(1)} aria-label="Next month">
            ›
          </Button>
        </div>

        <ErrorBox error={periods.error} />
        {periods.isLoading ? (
          <PageSpinner />
        ) : period ? (
          <PeriodContent key={period.id} list={list} group={group} period={period} />
        ) : selected === now ? (
          <StartMonth list={list} period={now} previous={periods.data?.[0]} />
        ) : (
          <Empty title={`No list for ${periodLabel(selected)}`} />
        )}
      </Page>
      <ListMenu list={list} open={menu} onClose={() => setMenu(false)} />
    </>
  );
}

function StatusLabel({ status }: { status: Period["status"] }) {
  if (status === "draft") return <Badge tone="blue">Reviewing</Badge>;
  if (status === "final") return <Badge tone="green">Final list · shopping</Badge>;
  return <Badge>Shopped</Badge>;
}

function StartMonth({ list, period, previous }: { list: List; period: string; previous: Period | undefined }) {
  const start = useStartPeriod(list.id);
  const prevItems = useItems(list.id, previous?.id ?? null);
  const count = previous ? (prevItems.data?.length ?? 0) : 0;

  return (
    <Card className="space-y-3 text-center">
      <p className="text-lg font-semibold">Time for {monthName(period)}'s list</p>
      <p className="text-stone-600">
        {previous
          ? `We'll copy all ${count} items from ${periodLabel(previous.period)}, including the extras you bought. Then you can review it together: untick what you don't need and add anything new.`
          : "Start your first list. Next month it will be copied forward for you automatically."}
      </p>
      <Button className="w-full" busy={start.isPending} onClick={() => start.mutate(period)}>
        {previous ? `Start ${monthName(period)} list` : "Start the list"}
      </Button>
      <ErrorBox error={start.error} />
    </Card>
  );
}

function PeriodContent({ list, group, period }: { list: List; group: Group; period: Period }) {
  const items = useItems(list.id, period.id);
  const bills = useBills(period.id);
  const { add, update, remove } = useItemMutations(list.id, period.id);
  const updatePeriod = useUpdatePeriod(period);
  const [editing, setEditing] = useState<Item | null>(null);
  const [showSkipped, setShowSkipped] = useState(false);
  const currency = group.currency;

  const all = items.data ?? [];
  const needed = all.filter((i) => i.included);
  const notNeeded = all.filter((i) => !i.included);
  const inCart = needed.filter((i) => i.bought);
  const toBuy = needed.filter((i) => !i.bought);

  const editor = (
    <ItemEditor
      item={editing}
      monthly
      currency={currency}
      onClose={() => setEditing(null)}
      saving={update.isPending}
      error={update.error}
      onSave={(patch) => editing && update.mutate({ id: editing.id, patch }, { onSuccess: () => setEditing(null) })}
      onDelete={() => editing && remove.mutate(editing.id, { onSuccess: () => setEditing(null) })}
    />
  );

  const addBar = (
    <div className="mb-4">
      <AddItemBar onAdd={(name, qty) => add.mutate({ name, qty })} />
      <ErrorBox error={add.error ?? update.error ?? remove.error} />
    </div>
  );

  if (items.isLoading) return <PageSpinner />;

  // ───── Draft: review what's needed this month ─────
  if (period.status === "draft") {
    return (
      <>
        <p className="mb-3 text-sm text-stone-600">
          Untick anything you don't need this month and add anything new. Once you both agree, finalize the list.
        </p>
        {addBar}
        {needed.length === 0 && notNeeded.length === 0 ? (
          <Empty title="The list is empty">Add the first item above.</Empty>
        ) : (
          <Card className="py-0">
            <ul>
              {needed.map((i) => (
                <ItemRow
                  key={i.id}
                  item={i}
                  checked
                  toggleLabel="Not needed this month"
                  onToggle={() => update.mutate({ id: i.id, patch: { included: false } })}
                  onOpen={() => setEditing(i)}
                  extra={<SourceBadge item={i} />}
                />
              ))}
            </ul>
            {needed.length === 0 && <p className="py-4 text-center text-sm text-stone-500">Nothing ticked yet.</p>}
          </Card>
        )}
        {notNeeded.length > 0 && (
          <section className="mt-6">
            <h2 className="mb-2 text-sm font-semibold tracking-wide text-stone-500 uppercase">
              Not needed this month ({notNeeded.length})
            </h2>
            <Card className="py-0 opacity-80">
              <ul>
                {notNeeded.map((i) => (
                  <ItemRow
                    key={i.id}
                    item={i}
                    checked={false}
                    strike
                    toggleLabel="Needed this month"
                    onToggle={() => update.mutate({ id: i.id, patch: { included: true } })}
                    onOpen={() => setEditing(i)}
                  />
                ))}
              </ul>
            </Card>
            <p className="mt-2 text-xs text-stone-500">
              These still come back next month. Open an item and choose "Remove for good" to drop it.
            </p>
          </section>
        )}
        <BottomBar>
          <Button
            className="flex-1"
            disabled={needed.length === 0}
            busy={updatePeriod.isPending}
            onClick={() => updatePeriod.mutate({ status: "final" })}
          >
            Finalize list ({needed.length} {needed.length === 1 ? "item" : "items"})
          </Button>
        </BottomBar>
        {editor}
      </>
    );
  }

  // ───── Final: shopping mode ─────
  if (period.status === "final") {
    return (
      <>
        <div className="mb-3 flex items-center justify-between">
          <p className="text-sm text-stone-600">
            <strong>{inCart.length}</strong> of {needed.length} in the cart
          </p>
          <Button variant="ghost" className="px-2 py-1 text-sm" onClick={() => updatePeriod.mutate({ status: "draft" })}>
            Edit list
          </Button>
        </div>
        <div className="mb-4 h-2 overflow-hidden rounded-full bg-stone-200">
          <div
            className="h-full rounded-full bg-brand-600 transition-all"
            style={{ width: `${needed.length ? (inCart.length / needed.length) * 100 : 0}%` }}
          />
        </div>
        {addBar}
        <Card className="py-0">
          <ul>
            {[...toBuy, ...inCart].map((i) => (
              <ItemRow
                key={i.id}
                item={i}
                checked={i.bought}
                strike={i.bought}
                toggleLabel={i.bought ? "Take out of cart" : "Put in cart"}
                onToggle={() => update.mutate({ id: i.id, patch: { bought: !i.bought } })}
                onOpen={() => setEditing(i)}
                currency={currency}
                showPrice
              />
            ))}
          </ul>
        </Card>
        {notNeeded.length > 0 && (
          <button className="mt-4 text-sm text-stone-500 underline" onClick={() => setShowSkipped((s) => !s)}>
            {showSkipped ? "Hide" : "Show"} {notNeeded.length} not needed this month
          </button>
        )}
        {showSkipped && (
          <Card className="mt-2 py-0 opacity-80">
            <ul>
              {notNeeded.map((i) => (
                <ItemRow
                  key={i.id}
                  item={i}
                  checked={false}
                  strike
                  toggleLabel="Needed after all"
                  onToggle={() => update.mutate({ id: i.id, patch: { included: true } })}
                  onOpen={() => setEditing(i)}
                />
              ))}
            </ul>
          </Card>
        )}
        <BillsList bills={bills.data ?? []} groupId={group.id} currency={currency} />
        <BottomBar>
          <BillUploadButton period={period} groupId={group.id} label="Upload bill" />
          <Button variant="secondary" busy={updatePeriod.isPending} onClick={() => updatePeriod.mutate({ status: "shopped" })}>
            Done, no bill
          </Button>
        </BottomBar>
        {editor}
      </>
    );
  }

  // ───── Shopped: summary and spending ─────
  return (
    <ShoppedSummary
      list={list}
      group={group}
      period={period}
      items={all}
      bills={bills.data ?? []}
      onEdit={setEditing}
      onToggle={(i) => update.mutate({ id: i.id, patch: { bought: !i.bought, included: true } })}
      onReopen={() => updatePeriod.mutate({ status: "final" })}
      addBar={addBar}
      editor={editor}
    />
  );
}

function ShoppedSummary({
  list,
  group,
  period,
  items,
  bills,
  onEdit,
  onToggle,
  onReopen,
  addBar,
  editor,
}: {
  list: List;
  group: Group;
  period: Period;
  items: Item[];
  bills: Bill[];
  onEdit: (i: Item) => void;
  onToggle: (i: Item) => void;
  onReopen: () => void;
  addBar: ReactNode;
  editor: ReactNode;
}) {
  const [manualOpen, setManualOpen] = useState(false);
  const currency = group.currency;
  const bought = items.filter((i) => i.bought);
  const missed = items.filter((i) => i.included && !i.bought);
  const spend = useMemo(
    () => monthSpend({ bills, manualTotal: period.manual_total, itemPrices: items.map((i) => i.price) }),
    [bills, period.manual_total, items],
  );
  const hasConfirmedBill = bills.some((b) => b.status === "confirmed");
  const sourceText = {
    bills: (() => {
      const n = bills.filter((b) => b.status === "confirmed").length;
      return `from ${n} bill${n === 1 ? "" : "s"}`;
    })(),
    manual: "entered by hand",
    items: "sum of item prices",
    none: "",
  }[spend.source];

  return (
    <>
      <Card className="mb-4">
        <div className="flex items-start justify-between gap-3">
          <div>
            <div className="text-sm text-stone-500">Spent in {monthName(period.period)}</div>
            <div className="text-3xl font-bold">{spend.total === null ? "—" : formatMoney(spend.total, currency)}</div>
            {sourceText && <div className="text-xs text-stone-500">{sourceText}</div>}
          </div>
          <Link to={`/g/${group.id}/l/${list.id}/spending?m=${period.period}`} className="text-sm font-medium text-brand-700">
            Breakdown ›
          </Link>
        </div>
        {!hasConfirmedBill && (
          <div className="mt-3 flex flex-wrap gap-2">
            <BillUploadButton period={period} groupId={group.id} label="Upload the bill" />
            <Button variant="secondary" onClick={() => setManualOpen(true)}>
              {period.manual_total === null ? "Enter total by hand" : "Change total"}
            </Button>
          </div>
        )}
      </Card>

      <BillsList bills={bills} groupId={group.id} currency={currency} />

      <p className="mt-4 mb-2 text-sm text-stone-600">
        Anything you add here is carried to next month's list too.
      </p>
      {addBar}

      <h2 className="mb-2 text-sm font-semibold tracking-wide text-stone-500 uppercase">Bought ({bought.length})</h2>
      {bought.length ? (
        <Card className="py-0">
          <ul>
            {bought.map((i) => (
              <ItemRow
                key={i.id}
                item={i}
                checked
                toggleLabel="Didn't get it"
                onToggle={() => onToggle(i)}
                onOpen={() => onEdit(i)}
                currency={currency}
                showPrice
                extra={i.source === "bill" ? <SourceBadge item={i} /> : undefined}
              />
            ))}
          </ul>
        </Card>
      ) : (
        <Empty title="Nothing marked as bought" />
      )}

      {missed.length > 0 && (
        <>
          <h2 className="mt-6 mb-2 text-sm font-semibold tracking-wide text-stone-500 uppercase">Didn't get ({missed.length})</h2>
          <Card className="py-0 opacity-80">
            <ul>
              {missed.map((i) => (
                <ItemRow key={i.id} item={i} checked={false} toggleLabel="Got it" onToggle={() => onToggle(i)} onOpen={() => onEdit(i)} />
              ))}
            </ul>
          </Card>
        </>
      )}

      <div className="mt-6 flex flex-wrap gap-2">
        {hasConfirmedBill && <BillUploadButton period={period} groupId={group.id} label="Add another bill" secondary />}
        <Button variant="ghost" onClick={onReopen}>
          Back to shopping mode
        </Button>
      </div>

      <ManualTotalSheet open={manualOpen} onClose={() => setManualOpen(false)} period={period} currency={currency} />
      {editor}
    </>
  );
}

function ManualTotalSheet({ open, onClose, period, currency }: { open: boolean; onClose: () => void; period: Period; currency: string }) {
  const [value, setValue] = useState(period.manual_total === null ? "" : String(period.manual_total));
  const update = useUpdatePeriod(period);
  return (
    <Sheet open={open} onClose={onClose} title="How much did you spend?">
      <form
        className="space-y-4"
        onSubmit={(e) => {
          e.preventDefault();
          update.mutate({ manual_total: toNumber(value) }, { onSuccess: onClose });
        }}
      >
        <Field label={`Total (${currency})`} hint="Leave empty to clear it.">
          <Input autoFocus inputMode="decimal" value={value} onChange={(e) => setValue(e.target.value)} placeholder="0.00" />
        </Field>
        <Button type="submit" className="w-full" busy={update.isPending}>
          Save
        </Button>
        <ErrorBox error={update.error} />
      </form>
    </Sheet>
  );
}

function BillsList({ bills, groupId, currency }: { bills: Bill[]; groupId: string; currency: string }) {
  if (!bills.length) return null;
  const tone = { uploaded: "stone", parsing: "blue", parsed: "amber", confirmed: "green", failed: "red" } as const;
  const label = { uploaded: "Uploading", parsing: "Reading…", parsed: "Needs review", confirmed: "Added", failed: "Couldn't read" };
  return (
    <div className="mt-4 space-y-2">
      {bills.map((b) => (
        <Link key={b.id} to={`/g/${groupId}/bills/${b.id}`} className="block">
          <Card className="flex items-center justify-between gap-2 py-3 hover:border-brand-500">
            <div className="min-w-0">
              <div className="truncate font-medium">{b.store_name ?? "Bill"}</div>
              <div className="text-xs text-stone-500">
                {/* bill_date is a plain YYYY-MM-DD; noon avoids timezone shifts to the previous day */}
                {new Date(b.bill_date ? `${b.bill_date}T12:00:00` : b.created_at).toLocaleDateString(undefined, {
                  day: "numeric",
                  month: "short",
                  year: "numeric",
                })}
                {b.total !== null && ` · ${formatMoney(Number(b.total), currency)}`}
              </div>
            </div>
            <Badge tone={tone[b.status]}>{label[b.status]}</Badge>
          </Card>
        </Link>
      ))}
    </div>
  );
}

export function BillUploadButton({ period, groupId, label, secondary }: { period: Period; groupId: string; label: string; secondary?: boolean }) {
  const input = useRef<HTMLInputElement>(null);
  const upload = useUploadBill(period, groupId);
  const navigate = useNavigate();
  return (
    <>
      <input
        ref={input}
        type="file"
        accept="image/*,application/pdf"
        className="hidden"
        onChange={(e) => {
          const file = e.target.files?.[0];
          e.target.value = "";
          if (file) upload.mutate(file, { onSuccess: (billId) => navigate(`/g/${groupId}/bills/${billId}`) });
        }}
      />
      <Button variant={secondary ? "secondary" : "primary"} className="flex-1" busy={upload.isPending} onClick={() => input.current?.click()}>
        {upload.isPending ? "Uploading…" : label}
      </Button>
      {upload.error && (
        <div className="basis-full">
          <ErrorBox error={upload.error} />
        </div>
      )}
    </>
  );
}
