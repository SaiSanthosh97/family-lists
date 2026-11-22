import { Link, Navigate, useParams } from "react-router-dom";
import { useRequestJoin } from "../api/groups";
import { Button, Card, ErrorBox, Header, Page } from "../components/ui";

export default function JoinGroup() {
  const { code = "" } = useParams();
  const join = useRequestJoin();

  if (join.data?.status === "member") return <Navigate to={`/g/${join.data.group_id}`} replace />;

  return (
    <>
      <Header title="Join a group" back="/" />
      <Page>
        <Card className="space-y-4 text-center">
          {join.data?.status === "pending" ? (
            <>
              <p className="text-lg font-semibold">Request sent</p>
              <p className="text-stone-600">
                Someone in <strong>{join.data.group_name}</strong> needs to accept you. Once they do, the group shows up
                on your home screen.
              </p>
              <Link to="/" state={{ stay: true }} className="inline-block font-medium text-brand-700">
                Go to home
              </Link>
            </>
          ) : join.data?.status === "not_found" ? (
            <>
              <p className="text-lg font-semibold">That invite code doesn't exist</p>
              <p className="text-stone-600">Ask for a fresh link or code.</p>
            </>
          ) : (
            <>
              <p className="text-stone-600">You were invited with the code</p>
              <p className="text-3xl font-bold tracking-[0.3em]">{code.toUpperCase()}</p>
              <p className="text-sm text-stone-500">
                Members of the group will see your name and email and can accept you.
              </p>
              <Button className="w-full" onClick={() => join.mutate(code)} busy={join.isPending}>
                Ask to join
              </Button>
            </>
          )}
          <ErrorBox error={join.error} />
        </Card>
      </Page>
    </>
  );
}
