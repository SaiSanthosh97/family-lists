import { useState, type FormEvent } from "react";
import { Link, Navigate, useLocation, useNavigate } from "react-router-dom";
import { useCreateGroup, useGroups, useMyPendingRequests, useRequestJoin } from "../api/groups";
import { CURRENCIES, guessCurrency, lastGroup } from "../lib/currency";
import { signOut, useAuth } from "../lib/auth";
import { Badge, Button, Card, ErrorBox, Field, Header, Input, Page, PageSpinner } from "../components/ui";

export default function Home() {
  const groups = useGroups();
  const pending = useMyPendingRequests();
  const location = useLocation();
  const { session } = useAuth();

  if (groups.isLoading) return <PageSpinner />;

  // Jump straight into the group used last time, unless the user came here on purpose.
  const stay = (location.state as { stay?: boolean } | null)?.stay;
  const remembered = lastGroup();
  if (!stay && remembered && groups.data?.some((g) => g.id === remembered)) {
    return <Navigate to={`/g/${remembered}`} replace />;
  }

  const hasGroups = (groups.data?.length ?? 0) > 0;

  return (
    <>
      <Header
        title="Your groups"
        subtitle={session?.user.email}
        right={
          <Button variant="ghost" onClick={() => signOut()}>
            Sign out
          </Button>
        }
      />
      <Page>
        <ErrorBox error={groups.error} />

        {hasGroups && (
          <div className="mb-6 space-y-2">
            {groups.data!.map((g) => (
              <Link key={g.id} to={`/g/${g.id}`} className="block">
                <Card className="flex items-center justify-between hover:border-brand-500">
                  <span className="font-medium">{g.name}</span>
                  <span className="text-stone-400">›</span>
                </Card>
              </Link>
            ))}
          </div>
        )}

        {!!pending.data?.length && (
          <div className="mb-6 space-y-2">
            {pending.data.map((r) => (
              <Card key={r.id} className="flex items-center justify-between">
                <span>
                  Asked to join <strong>{r.group_name}</strong>
                </span>
                <Badge tone="amber">Waiting for approval</Badge>
              </Card>
            ))}
          </div>
        )}

        {!hasGroups && (
          <div className="mb-6">
            <h2 className="text-xl font-semibold">Welcome!</h2>
            <p className="mt-1 text-stone-600">
              Start a group for your household, or join one with the code someone shared with you.
            </p>
          </div>
        )}

        <div className="grid gap-4 sm:grid-cols-2">
          <CreateGroup />
          <JoinWithCode />
        </div>
      </Page>
    </>
  );
}

function CreateGroup() {
  const [name, setName] = useState("");
  const [currency, setCurrency] = useState(guessCurrency);
  const create = useCreateGroup();
  const navigate = useNavigate();

  function submit(e: FormEvent) {
    e.preventDefault();
    if (!name.trim()) return;
    create.mutate({ name: name.trim(), currency }, { onSuccess: (g) => navigate(`/g/${g.id}`) });
  }

  return (
    <Card>
      <form onSubmit={submit} className="space-y-3">
        <h3 className="font-semibold">Start a new group</h3>
        <Field label="Group name">
          <Input value={name} onChange={(e) => setName(e.target.value)} placeholder="Home" maxLength={60} required />
        </Field>
        <Field label="Currency">
          <select
            value={currency}
            onChange={(e) => setCurrency(e.target.value)}
            className="w-full rounded-xl border border-stone-300 bg-white px-3 py-2.5"
          >
            {CURRENCIES.map((c) => (
              <option key={c}>{c}</option>
            ))}
          </select>
        </Field>
        <Button type="submit" className="w-full" busy={create.isPending}>
          Create group
        </Button>
        <ErrorBox error={create.error} />
      </form>
    </Card>
  );
}

function JoinWithCode() {
  const [code, setCode] = useState("");
  const join = useRequestJoin();
  const navigate = useNavigate();

  function submit(e: FormEvent) {
    e.preventDefault();
    join.mutate(code, {
      onSuccess: (r) => r.status === "member" && navigate(`/g/${r.group_id}`),
    });
  }

  return (
    <Card>
      <form onSubmit={submit} className="space-y-3">
        <h3 className="font-semibold">Join a group</h3>
        <Field label="Invite code" hint="Ask someone in the group for the 6-letter code in its settings.">
          <Input
            value={code}
            onChange={(e) => setCode(e.target.value.toUpperCase())}
            placeholder="ABC234"
            maxLength={6}
            autoCapitalize="characters"
            className="tracking-widest uppercase"
            required
          />
        </Field>
        <Button type="submit" variant="secondary" className="w-full" busy={join.isPending}>
          Ask to join
        </Button>
        {join.data?.status === "pending" && (
          <p className="text-sm text-brand-800">
            Request sent to <strong>{join.data.group_name}</strong>. Someone in the group needs to accept it.
          </p>
        )}
        {join.data?.status === "not_found" && <p className="text-sm text-red-700">No group has that code.</p>}
        <ErrorBox error={join.error} />
      </form>
    </Card>
  );
}
