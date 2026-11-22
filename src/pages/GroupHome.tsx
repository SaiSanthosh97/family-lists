import { useEffect, useState, type FormEvent } from "react";
import { Link, Navigate, useNavigate, useParams } from "react-router-dom";
import { useGroup, useJoinRequests } from "../api/groups";
import { useCreateList, useLatestPeriods, useLists } from "../api/lists";
import { rememberGroup } from "../lib/currency";
import { currentPeriod, monthName, periodLabel } from "../lib/period";
import type { ListKind, Period } from "../lib/types";
import {
  Badge,
  Button,
  Card,
  Dot,
  Empty,
  ErrorBox,
  Field,
  GearIcon,
  Header,
  IconButton,
  Input,
  Page,
  PageSpinner,
  Sheet,
} from "../components/ui";

export default function GroupHome() {
  const { groupId = "" } = useParams();
  const group = useGroup(groupId);
  const lists = useLists(groupId);
  const requests = useJoinRequests(groupId);
  const [creating, setCreating] = useState<ListKind | null>(null);

  const monthly = lists.data?.filter((l) => l.kind === "monthly") ?? [];
  const todos = lists.data?.filter((l) => l.kind === "todo") ?? [];
  const latest = useLatestPeriods(monthly.map((l) => l.id));

  useEffect(() => {
    if (group.data) rememberGroup(group.data.id);
  }, [group.data]);

  if (group.isLoading || lists.isLoading) return <PageSpinner />;
  if (!group.data) return <Navigate to="/" state={{ stay: true }} replace />;

  const pendingCount = requests.data?.length ?? 0;

  return (
    <>
      <Header
        title={group.data.name}
        subtitle="Switch group"
        back="/"
        backState={{ stay: true }}
        right={
          <IconButton label="Group settings and members" to={`/g/${groupId}/settings`}>
            <GearIcon />
            {pendingCount > 0 && <Dot />}
          </IconButton>
        }
      />
      <Page>
        {pendingCount > 0 && (
          <Link to={`/g/${groupId}/settings`} className="mb-4 block">
            <Card className="border-amber-300 bg-amber-50">
              <strong>
                {pendingCount} {pendingCount === 1 ? "person wants" : "people want"} to join
              </strong>{" "}
              — tap to review.
            </Card>
          </Link>
        )}
        <ErrorBox error={lists.error} />

        <section className="mb-8">
          <div className="mb-2 flex items-center justify-between">
            <h2 className="text-sm font-semibold tracking-wide text-stone-500 uppercase">Monthly lists</h2>
            <Button variant="ghost" className="px-2 py-1 text-sm" onClick={() => setCreating("monthly")}>
              + New
            </Button>
          </div>
          {monthly.length === 0 ? (
            <Empty title="No monthly lists yet">
              Make one for groceries. Each month starts as a copy of last month's list.
              <div className="mt-3">
                <Button onClick={() => setCreating("monthly")}>Create a groceries list</Button>
              </div>
            </Empty>
          ) : (
            <div className="space-y-2">
              {monthly.map((l) => (
                <Link key={l.id} to={`/g/${groupId}/l/${l.id}`} className="block">
                  <Card className="hover:border-brand-500">
                    <div className="flex items-center justify-between gap-2">
                      <span className="font-medium">{l.name}</span>
                      <MonthStatus period={latest.data?.get(l.id)} />
                    </div>
                  </Card>
                </Link>
              ))}
            </div>
          )}
        </section>

        <section>
          <div className="mb-2 flex items-center justify-between">
            <h2 className="text-sm font-semibold tracking-wide text-stone-500 uppercase">To-do lists</h2>
            <Button variant="ghost" className="px-2 py-1 text-sm" onClick={() => setCreating("todo")}>
              + New
            </Button>
          </div>
          {todos.length === 0 ? (
            <Empty title="No to-do lists yet">For chores, errands, trip packing, anything.</Empty>
          ) : (
            <div className="space-y-2">
              {todos.map((l) => (
                <Link key={l.id} to={`/g/${groupId}/l/${l.id}`} className="block">
                  <Card className="hover:border-brand-500">
                    <span className="font-medium">{l.name}</span>
                  </Card>
                </Link>
              ))}
            </div>
          )}
        </section>
      </Page>

      <NewListSheet groupId={groupId} kind={creating} onClose={() => setCreating(null)} />
    </>
  );
}

function MonthStatus({ period }: { period: Period | undefined }) {
  const now = currentPeriod();
  // Periods are "YYYY-MM", so string comparison orders them.
  if (!period || period.period < now) return <Badge tone="amber">Start {periodLabel(now)}</Badge>;
  // Someone on another device may already be in the next month (time zones around the 1st).
  const prefix = period.period > now ? `${monthName(period.period)}: ` : "";
  if (period.status === "draft") return <Badge tone="blue">{prefix}Reviewing</Badge>;
  if (period.status === "final") return <Badge tone="green">{prefix}Ready to shop</Badge>;
  return <Badge>{prefix}Shopped</Badge>;
}

function NewListSheet({ groupId, kind, onClose }: { groupId: string; kind: ListKind | null; onClose: () => void }) {
  const [name, setName] = useState("");
  const create = useCreateList(groupId);
  const navigate = useNavigate();

  function submit(e: FormEvent) {
    e.preventDefault();
    if (!kind || !name.trim()) return;
    create.mutate(
      { name: name.trim(), kind },
      {
        onSuccess: (l) => {
          setName("");
          onClose();
          navigate(`/g/${groupId}/l/${l.id}`);
        },
      },
    );
  }

  return (
    <Sheet open={kind !== null} onClose={onClose} title={kind === "monthly" ? "New monthly list" : "New to-do list"}>
      <form onSubmit={submit} className="space-y-4">
        <Field
          label="Name"
          hint={
            kind === "monthly"
              ? "Every month this list is copied forward for you to review."
              : "A simple checklist shared with your group."
          }
        >
          <Input
            autoFocus
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder={kind === "monthly" ? "Groceries" : "Weekend chores"}
            maxLength={80}
            required
          />
        </Field>
        <Button type="submit" className="w-full" busy={create.isPending}>
          Create list
        </Button>
        <ErrorBox error={create.error} />
      </form>
    </Sheet>
  );
}
