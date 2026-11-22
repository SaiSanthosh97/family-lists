import { useEffect, useRef } from "react";
import { useQueryClient, type QueryKey } from "@tanstack/react-query";
import type { PostgrestError } from "@supabase/supabase-js";
import { supabase } from "../lib/supabase";

/** Unwrap a Supabase response or throw its error. */
export function must<T>(res: { data: T | null; error: PostgrestError | null }): T {
  if (res.error) throw new Error(res.error.message);
  return res.data as T;
}

/** Re-fetch the given queries whenever rows in `table` matching `filter` change. */
export function useRealtime(table: string, filter: string | null, keys: QueryKey[]) {
  const qc = useQueryClient();
  const keysRef = useRef(keys);
  keysRef.current = keys;

  useEffect(() => {
    if (!filter) return;
    const channel = supabase
      .channel(`rt:${table}:${filter}:${Math.random().toString(36).slice(2)}`)
      .on("postgres_changes", { event: "*", schema: "public", table, filter }, () => {
        for (const key of keysRef.current) qc.invalidateQueries({ queryKey: key });
      })
      .subscribe();
    return () => {
      supabase.removeChannel(channel);
    };
  }, [table, filter, qc]);
}

export function errorMessage(err: unknown): string {
  if (err instanceof Error) return err.message;
  return String(err);
}
