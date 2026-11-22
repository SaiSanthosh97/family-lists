import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { FunctionsHttpError } from "@supabase/supabase-js";
import { supabase } from "../lib/supabase";
import type { Bill, BillLine, LineAction, Period } from "../lib/types";
import { prepareBillFile } from "../lib/image";
import { must, useRealtime } from "./util";

export function useBills(periodId: string | undefined) {
  const key = ["bills", periodId];
  useRealtime("bills", periodId ? `period_id=eq.${periodId}` : null, [key]);
  return useQuery({
    queryKey: key,
    enabled: !!periodId,
    queryFn: async () =>
      must(await supabase.from("bills").select("*").eq("period_id", periodId!).order("created_at")) as Bill[],
  });
}

export function useBill(billId: string | undefined) {
  return useQuery({
    queryKey: ["bill", billId],
    enabled: !!billId,
    queryFn: async () => {
      const bill = must(await supabase.from("bills").select("*").eq("id", billId!).maybeSingle()) as Bill | null;
      if (!bill) return null;
      const lines = must(
        await supabase.from("bill_lines").select("*").eq("bill_id", bill.id).order("position"),
      ) as BillLine[];
      return { bill, lines };
    },
    refetchInterval: (q) => {
      const status = q.state.data?.bill.status;
      return status === "uploaded" || status === "parsing" ? 2500 : false;
    },
  });
}

export function useBillPhotoUrl(path: string | undefined) {
  return useQuery({
    queryKey: ["billPhoto", path],
    enabled: !!path,
    staleTime: 50 * 60 * 1000,
    queryFn: async () => {
      const { data, error } = await supabase.storage.from("bills").createSignedUrl(path!, 60 * 60);
      if (error) throw new Error(error.message);
      return data.signedUrl;
    },
  });
}

async function functionError(error: unknown): Promise<string> {
  if (error instanceof FunctionsHttpError) {
    try {
      const body = await error.context.json();
      if (body?.error) return String(body.error);
    } catch {
      /* not JSON */
    }
  }
  return error instanceof Error ? error.message : String(error);
}

export async function parseBill(billId: string) {
  const { error } = await supabase.functions.invoke("parse-bill", { body: { bill_id: billId } });
  if (error) throw new Error(await functionError(error));
}

/** Upload a bill photo/PDF for a month, then ask the server to read it. */
export function useUploadBill(period: Period, groupId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (file: File): Promise<string> => {
      const prepared = await prepareBillFile(file);
      const billId = crypto.randomUUID();
      const path = `${groupId}/${billId}.${prepared.ext}`;

      const up = await supabase.storage.from("bills").upload(path, prepared.blob, { contentType: prepared.type });
      if (up.error) throw new Error(`Upload failed: ${up.error.message}`);

      const ins = await supabase.from("bills").insert({ id: billId, period_id: period.id, storage_path: path });
      if (ins.error) {
        await supabase.storage.from("bills").remove([path]);
        throw new Error(ins.error.message);
      }
      // Reading happens in the background; the review page shows progress.
      parseBill(billId).catch(() => qc.invalidateQueries({ queryKey: ["bill", billId] }));
      return billId;
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: ["bills", period.id] }),
  });
}

export function useRetryParse(billId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async () => {
      await supabase.from("bills").update({ status: "parsing", error: null }).eq("id", billId);
      qc.invalidateQueries({ queryKey: ["bill", billId] });
      await parseBill(billId);
    },
    onSettled: () => qc.invalidateQueries({ queryKey: ["bill", billId] }),
  });
}

export interface ReviewedLine {
  id: string;
  name: string;
  qty: number | null;
  unit_price: number | null;
  line_total: number | null;
  action: LineAction;
  matched_item_id: string | null;
}

export interface ReviewedBill {
  store_name: string | null;
  bill_date: string | null;
  subtotal: number | null;
  tax: number | null;
  discount: number | null;
  total: number | null;
}

export function useConfirmBill(bill: Bill, listId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (input: { bill: ReviewedBill; lines: ReviewedLine[] }) =>
      must(
        await supabase.rpc("confirm_bill", { p_bill_id: bill.id, p_bill: input.bill, p_lines: input.lines }),
      ),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["bill", bill.id] });
      qc.invalidateQueries({ queryKey: ["bills", bill.period_id] });
      qc.invalidateQueries({ queryKey: ["items", "period", bill.period_id] });
      qc.invalidateQueries({ queryKey: ["periods", listId] });
      qc.invalidateQueries({ queryKey: ["spending", listId] });
    },
  });
}

export function useDeleteBill(bill: Bill) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async () => {
      must(await supabase.from("bills").delete().eq("id", bill.id));
      await supabase.storage.from("bills").remove([bill.storage_path]);
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: ["bills", bill.period_id] }),
  });
}
