import { useMutation, useQuery, useQueryClient, type QueryKey } from "@tanstack/react-query";
import { supabase } from "../lib/supabase";
import type { Item, List, ListKind, Period, PeriodStatus } from "../lib/types";
import { buildCarryOver } from "../lib/rollover";
import { must, useRealtime } from "./util";

// ───────────── Lists ─────────────
export function useLists(groupId: string | undefined) {
  return useQuery({
    queryKey: ["lists", groupId],
    enabled: !!groupId,
    queryFn: async () =>
      must(
        await supabase.from("lists").select("*").eq("group_id", groupId!).eq("archived", false).order("created_at"),
      ) as List[],
  });
}

export function useList(listId: string | undefined) {
  return useQuery({
    queryKey: ["list", listId],
    enabled: !!listId,
    queryFn: async () => must(await supabase.from("lists").select("*").eq("id", listId!).maybeSingle()) as List | null,
  });
}

export function useCreateList(groupId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (input: { name: string; kind: ListKind }) =>
      must(await supabase.from("lists").insert({ group_id: groupId, ...input }).select().single()) as List,
    onSuccess: () => qc.invalidateQueries({ queryKey: ["lists", groupId] }),
  });
}

export function useUpdateList(list: List) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (patch: Partial<Pick<List, "name" | "archived">>) =>
      must(await supabase.from("lists").update(patch).eq("id", list.id).select().single()) as List,
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["lists", list.group_id] });
      qc.invalidateQueries({ queryKey: ["list", list.id] });
    },
  });
}

export function useDeleteList(list: List) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async () => must(await supabase.from("lists").delete().eq("id", list.id)),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["lists", list.group_id] }),
  });
}

// ───────────── Periods (one per month) ─────────────
export function usePeriods(listId: string | undefined) {
  const key = ["periods", listId];
  useRealtime("list_periods", listId ? `list_id=eq.${listId}` : null, [key]);
  return useQuery({
    queryKey: key,
    enabled: !!listId,
    queryFn: async () =>
      must(await supabase.from("list_periods").select("*").eq("list_id", listId!).order("period", { ascending: false })) as Period[],
  });
}

/** Latest period of every monthly list in a group (for the group home cards). */
export function useLatestPeriods(listIds: string[]) {
  return useQuery({
    queryKey: ["latestPeriods", listIds],
    enabled: listIds.length > 0,
    queryFn: async () => {
      const rows = must(
        await supabase.from("list_periods").select("*").in("list_id", listIds).order("period", { ascending: false }),
      ) as Period[];
      const latest = new Map<string, Period>();
      for (const p of rows) if (!latest.has(p.list_id)) latest.set(p.list_id, p);
      return latest;
    },
  });
}

/**
 * Start a month: create the period and copy the most recent month's items.
 * If someone else started it a moment ago, just use theirs.
 */
export function useStartPeriod(listId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (period: string): Promise<Period> => {
      const previous = (
        must(
          await supabase
            .from("list_periods")
            .select("*")
            .eq("list_id", listId)
            .lt("period", period)
            .order("period", { ascending: false })
            .limit(1),
        ) as Period[]
      )[0];

      const created = await supabase.from("list_periods").insert({ list_id: listId, period }).select().single();
      if (created.error) {
        if (created.error.code === "23505") {
          return must(
            await supabase.from("list_periods").select("*").eq("list_id", listId).eq("period", period).single(),
          ) as Period;
        }
        throw new Error(created.error.message);
      }
      const newPeriod = created.data as Period;

      if (previous) {
        const prevItems = must(
          await supabase.from("items").select("name, qty, unit, notes, included, position").eq("period_id", previous.id),
        ) as Array<Pick<Item, "name" | "qty" | "unit" | "notes" | "included" | "position">>;
        const carried = buildCarryOver(prevItems);
        if (carried.length) {
          must(
            await supabase
              .from("items")
              .insert(carried.map((c) => ({ ...c, list_id: listId, period_id: newPeriod.id }))),
          );
        }
      }
      return newPeriod;
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: ["periods", listId] }),
  });
}

export function useUpdatePeriod(period: Period) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (patch: { status?: PeriodStatus; manual_total?: number | null }) =>
      must(await supabase.from("list_periods").update(patch).eq("id", period.id).select().single()) as Period,
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["periods", period.list_id] });
      qc.invalidateQueries({ queryKey: ["spending", period.list_id] });
    },
  });
}

// ───────────── Items ─────────────
/** Items of one month (monthly lists) or of a to-do list (periodId = null). */
export function itemsKey(listId: string, periodId: string | null): QueryKey {
  return periodId ? ["items", "period", periodId] : ["items", "todo", listId];
}

export function useItems(listId: string, periodId: string | null) {
  const key = itemsKey(listId, periodId);
  useRealtime("items", periodId ? `period_id=eq.${periodId}` : `list_id=eq.${listId}`, [key]);
  return useQuery({
    queryKey: key,
    queryFn: async () => {
      let q = supabase.from("items").select("*").eq("list_id", listId);
      q = periodId ? q.eq("period_id", periodId) : q.is("period_id", null);
      return must(await q.order("position").order("created_at")) as Item[];
    },
  });
}

export function useItemMutations(listId: string, periodId: string | null) {
  const qc = useQueryClient();
  const key = itemsKey(listId, periodId);
  const invalidate = () => {
    qc.invalidateQueries({ queryKey: key });
    qc.invalidateQueries({ queryKey: ["spending", listId] });
  };

  const add = useMutation({
    mutationFn: async (input: { name: string; qty?: number; unit?: string | null }) => {
      const items = qc.getQueryData<Item[]>(key) ?? [];
      const position = items.reduce((m, i) => Math.max(m, i.position), 0) + 1;
      return must(
        await supabase
          .from("items")
          .insert({
            list_id: listId,
            period_id: periodId,
            name: input.name.trim(),
            qty: input.qty && input.qty > 0 ? input.qty : 1,
            unit: input.unit || null,
            source: "added",
            position,
          })
          .select()
          .single(),
      ) as Item;
    },
    onSuccess: (item) => qc.setQueryData<Item[]>(key, (old) => (old ? [...old.filter((i) => i.id !== item.id), item] : [item])),
    onSettled: invalidate,
  });

  const update = useMutation({
    mutationFn: async ({ id, patch }: { id: string; patch: Partial<Item> }) =>
      must(await supabase.from("items").update(patch).eq("id", id).select().single()) as Item,
    // Optimistic: ticks feel instant in the shop even on slow mobile data.
    onMutate: async ({ id, patch }) => {
      await qc.cancelQueries({ queryKey: key });
      const prev = qc.getQueryData<Item[]>(key);
      qc.setQueryData<Item[]>(key, (old) => old?.map((i) => (i.id === id ? { ...i, ...patch } : i)));
      return { prev };
    },
    onError: (_e, _v, ctx) => ctx?.prev && qc.setQueryData(key, ctx.prev),
    onSettled: invalidate,
  });

  const remove = useMutation({
    mutationFn: async (id: string) => must(await supabase.from("items").delete().eq("id", id)),
    onMutate: async (id) => {
      await qc.cancelQueries({ queryKey: key });
      const prev = qc.getQueryData<Item[]>(key);
      qc.setQueryData<Item[]>(key, (old) => old?.filter((i) => i.id !== id));
      return { prev };
    },
    onError: (_e, _v, ctx) => ctx?.prev && qc.setQueryData(key, ctx.prev),
    onSettled: invalidate,
  });

  const removeMany = useMutation({
    mutationFn: async (ids: string[]) => must(await supabase.from("items").delete().in("id", ids)),
    onSettled: invalidate,
  });

  const swapPositions = useMutation({
    mutationFn: async ({ a, b }: { a: Item; b: Item }) => {
      must(await supabase.from("items").update({ position: b.position }).eq("id", a.id));
      must(await supabase.from("items").update({ position: a.position }).eq("id", b.id));
    },
    onMutate: async ({ a, b }) => {
      await qc.cancelQueries({ queryKey: key });
      qc.setQueryData<Item[]>(key, (old) =>
        old
          ?.map((i) => (i.id === a.id ? { ...i, position: b.position } : i.id === b.id ? { ...i, position: a.position } : i))
          .sort((x, y) => x.position - y.position),
      );
    },
    onSettled: invalidate,
  });

  return { add, update, remove, removeMany, swapPositions };
}
