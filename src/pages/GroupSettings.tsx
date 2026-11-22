import { useState } from "react";
import { Navigate, useNavigate, useParams } from "react-router-dom";
import {
  useDecideRequest,
  useGroup,
  useJoinRequests,
  useLeaveGroup,
  useMembers,
  useRemoveMember,
  useUpdateGroup,
} from "../api/groups";
import { useUserId } from "../lib/auth";
import { CURRENCIES } from "../lib/currency";
import { Badge, Button, Card, ErrorBox, Field, Header, Input, Page, PageSpinner } from "../components/ui";

export default function GroupSettings() {
  const { groupId = "" } = useParams();
  const me = useUserId();
  const group = useGroup(groupId);
  const members = useMembers(groupId);
  const requests = useJoinRequests(groupId);
  const decide = useDecideRequest(groupId);
  const remove = useRemoveMember(groupId);
  const leave = useLeaveGroup();
  const update = useUpdateGroup(groupId);
  const navigate = useNavigate();
  const [copied, setCopied] = useState(false);
  const [name, setName] = useState<string | null>(null);

  if (group.isLoading || members.isLoading) return <PageSpinner />;
  if (!group.data) return <Navigate to="/" state={{ stay: true }} replace />;

  const g = group.data;
  const isOwner = members.data?.some((m) => m.user_id === me && m.role === "owner") ?? false;
  const link = `${window.location.origin}/join/${g.invite_code}`;

  async function share() {
    const text = `Join "${g.name}" on Family Lists: ${link}`;
    try {
      if (navigator.share) {
        await navigator.share({ title: "Join my group", text, url: link });
        return;
      }
      await navigator.clipboard.writeText(link);
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    } catch {
      /* user cancelled share sheet */
    }
  }

  function leaveGroup() {
    const others = (members.data?.length ?? 1) - 1;
    const warning =
      others === 0
        ? `You're the only member. Leaving deletes "${g.name}" and all of its lists. Continue?`
        : `Leave "${g.name}"? You'll lose access to its lists until someone accepts you again.`;
    if (!confirm(warning)) return;
    leave.mutate(g.id, { onSuccess: () => navigate("/", { replace: true, state: { stay: true } }) });
  }

  return (
    <>
      <Header title="Group settings" subtitle={g.name} back={`/g/${groupId}`} />
      <Page>
        <div className="space-y-6">
          <Card className="space-y-3">
            <h2 className="font-semibold">Invite people</h2>
            <p className="text-sm text-stone-600">
              Share this link or code. They'll need someone in the group to accept them, so a forwarded link can't let
              strangers in.
            </p>
            <div className="flex items-center justify-between rounded-xl bg-stone-100 px-4 py-3">
              <span className="text-2xl font-bold tracking-[0.3em]">{g.invite_code}</span>
              <Button variant="secondary" onClick={share}>
                {copied ? "Link copied" : "Share link"}
              </Button>
            </div>
          </Card>

          {!!requests.data?.length && (
            <Card className="space-y-3 border-amber-300">
              <h2 className="font-semibold">Waiting to join</h2>
              {requests.data.map((r) => (
                <div key={r.id} className="flex flex-wrap items-center justify-between gap-2">
                  <div className="min-w-0">
                    <div className="font-medium">{r.profile?.display_name ?? "Someone"}</div>
                    <div className="truncate text-sm text-stone-500">{r.profile?.email}</div>
                  </div>
                  <div className="flex gap-2">
                    <Button
                      variant="secondary"
                      onClick={() => decide.mutate({ requestId: r.id, accept: false })}
                      disabled={decide.isPending}
                    >
                      Decline
                    </Button>
                    <Button onClick={() => decide.mutate({ requestId: r.id, accept: true })} disabled={decide.isPending}>
                      Accept
                    </Button>
                  </div>
                </div>
              ))}
              <ErrorBox error={decide.error} />
            </Card>
          )}

          <Card className="space-y-3">
            <h2 className="font-semibold">Members</h2>
            {members.data?.map((m) => (
              <div key={m.user_id} className="flex items-center justify-between gap-2">
                <div className="min-w-0">
                  <div className="font-medium">
                    {m.profile?.display_name ?? m.profile?.email ?? "Member"}
                    {m.user_id === me && <span className="text-stone-500"> (you)</span>}
                  </div>
                  <div className="truncate text-sm text-stone-500">{m.profile?.email}</div>
                </div>
                <div className="flex items-center gap-2">
                  {m.role === "owner" && <Badge tone="green">Owner</Badge>}
                  {isOwner && m.user_id !== me && (
                    <Button
                      variant="ghost"
                      className="px-2 py-1 text-sm text-red-700"
                      onClick={() =>
                        confirm(`Remove ${m.profile?.display_name ?? "this member"} from the group?`) && remove.mutate(m.user_id)
                      }
                    >
                      Remove
                    </Button>
                  )}
                </div>
              </div>
            ))}
            <ErrorBox error={remove.error} />
          </Card>

          {isOwner && (
            <Card className="space-y-3">
              <h2 className="font-semibold">Group details</h2>
              <Field label="Name">
                <div className="flex gap-2">
                  <Input value={name ?? g.name} onChange={(e) => setName(e.target.value)} maxLength={60} />
                  <Button
                    variant="secondary"
                    disabled={!name || name.trim() === g.name}
                    busy={update.isPending}
                    onClick={() => name && update.mutate({ name: name.trim() }, { onSuccess: () => setName(null) })}
                  >
                    Save
                  </Button>
                </div>
              </Field>
              <Field label="Currency for spending">
                <select
                  value={g.currency}
                  onChange={(e) => update.mutate({ currency: e.target.value })}
                  className="w-full rounded-xl border border-stone-300 bg-white px-3 py-2.5"
                >
                  {CURRENCIES.map((c) => (
                    <option key={c}>{c}</option>
                  ))}
                </select>
              </Field>
              <ErrorBox error={update.error} />
            </Card>
          )}

          <Button variant="danger" className="w-full" onClick={leaveGroup} busy={leave.isPending}>
            Leave group
          </Button>
          <ErrorBox error={leave.error} />
        </div>
      </Page>
    </>
  );
}
