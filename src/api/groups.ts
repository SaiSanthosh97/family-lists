import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { supabase } from "../lib/supabase";
import type { Group, JoinRequest, Member } from "../lib/types";
import { must } from "./util";

export function useGroups() {
  return useQuery({
    queryKey: ["groups"],
    queryFn: async () =>
      must(await supabase.from("groups").select("*").order("created_at")) as Group[],
  });
}

export function useGroup(groupId: string | undefined) {
  return useQuery({
    queryKey: ["group", groupId],
    enabled: !!groupId,
    queryFn: async () =>
      must(await supabase.from("groups").select("*").eq("id", groupId!).maybeSingle()) as Group | null,
  });
}

export function useMembers(groupId: string | undefined) {
  return useQuery({
    queryKey: ["members", groupId],
    enabled: !!groupId,
    queryFn: async () =>
      must(
        await supabase
          .from("group_members")
          .select("group_id, user_id, role, joined_at, profile:profiles(display_name, email, avatar_url)")
          .eq("group_id", groupId!)
          .order("joined_at"),
      ) as unknown as Member[],
  });
}

export function useJoinRequests(groupId: string | undefined) {
  return useQuery({
    queryKey: ["requests", groupId],
    enabled: !!groupId,
    refetchInterval: 30_000,
    queryFn: async () =>
      must(
        await supabase
          .from("join_requests")
          .select("id, group_id, user_id, status, created_at, profile:profiles!join_requests_user_id_fkey(display_name, email)")
          .eq("group_id", groupId!)
          .eq("status", "pending")
          .order("created_at"),
      ) as unknown as JoinRequest[],
  });
}

type PendingRequest = { id: string; group_name: string; created_at: string };

export function useMyPendingRequests() {
  const qc = useQueryClient();
  return useQuery({
    queryKey: ["myPending"],
    queryFn: async () => {
      const before = qc.getQueryData<PendingRequest[]>(["myPending"]) ?? [];
      const now = must(await supabase.rpc("my_pending_requests")) as PendingRequest[];
      // A request disappeared: it was accepted (or declined), so the group list may have changed.
      if (before.some((b) => !now.some((n) => n.id === b.id))) qc.invalidateQueries({ queryKey: ["groups"] });
      return now;
    },
    // Check every 10 s while waiting so an acceptance shows up without reopening the app.
    refetchInterval: (q) => (q.state.data?.length ? 10_000 : false),
  });
}

export function useCreateGroup() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (input: { name: string; currency: string }) =>
      must(await supabase.rpc("create_group", { p_name: input.name, p_currency: input.currency })) as Group,
    onSuccess: () => qc.invalidateQueries({ queryKey: ["groups"] }),
  });
}

export type JoinResult =
  | { status: "not_found" }
  | { status: "pending"; group_name: string }
  | { status: "member"; group_id: string; group_name: string };

export function useRequestJoin() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (code: string) => must(await supabase.rpc("request_join", { p_code: code })) as JoinResult,
    onSuccess: () => qc.invalidateQueries({ queryKey: ["myPending"] }),
  });
}

export function useDecideRequest(groupId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (input: { requestId: string; accept: boolean }) =>
      must(await supabase.rpc("decide_join_request", { p_request_id: input.requestId, p_accept: input.accept })),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["requests", groupId] });
      qc.invalidateQueries({ queryKey: ["members", groupId] });
    },
  });
}

export function useUpdateGroup(groupId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (patch: Partial<Pick<Group, "name" | "currency">>) =>
      must(await supabase.from("groups").update(patch).eq("id", groupId).select().single()) as Group,
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["group", groupId] });
      qc.invalidateQueries({ queryKey: ["groups"] });
    },
  });
}

export function useRemoveMember(groupId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (userId: string) =>
      must(await supabase.from("group_members").delete().eq("group_id", groupId).eq("user_id", userId)),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["members", groupId] }),
  });
}

export function useLeaveGroup() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (groupId: string) => must(await supabase.rpc("leave_group", { p_group_id: groupId })),
    onSuccess: () => qc.invalidateQueries(),
  });
}
