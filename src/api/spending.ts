import { useQuery } from "@tanstack/react-query";
import { supabase } from "../lib/supabase";
import type { Bill, BillLine, Item, Period } from "../lib/types";
import { monthSpend, type SpendSource } from "../lib/money";
import { must } from "./util";

export interface MonthSpending {
  period: Period;
  total: number | null;
  source: SpendSource;
  items: Item[]; // bought or priced items of the month
  otherLines: Array<Pick<BillLine, "id" | "name" | "line_total">>; // bill lines not tied to a list item
  bills: Bill[];
}

/** Everything needed for the spending page of one monthly list. */
export function useSpending(listId: string | undefined) {
  return useQuery({
    queryKey: ["spending", listId],
    enabled: !!listId,
    queryFn: async (): Promise<MonthSpending[]> => {
      const periods = must(
        await supabase.from("list_periods").select("*").eq("list_id", listId!).order("period"),
      ) as Period[];
      if (!periods.length) return [];
      const ids = periods.map((p) => p.id);

      const [items, bills] = await Promise.all([
        supabase.from("items").select("*").in("period_id", ids).then(must) as Promise<Item[]>,
        supabase.from("bills").select("*").in("period_id", ids).then(must) as Promise<Bill[]>,
      ]);
      const confirmedIds = bills.filter((b) => b.status === "confirmed").map((b) => b.id);
      const ignored = confirmedIds.length
        ? ((await supabase
            .from("bill_lines")
            .select("id, name, line_total, bill_id")
            .in("bill_id", confirmedIds)
            .eq("action", "ignore")
            .then(must)) as Array<Pick<BillLine, "id" | "name" | "line_total" | "bill_id">>)
        : [];

      return periods.map((period) => {
        const monthItems = items.filter((i) => i.period_id === period.id);
        const monthBills = bills.filter((b) => b.period_id === period.id);
        const billIds = new Set(monthBills.map((b) => b.id));
        const { total, source } = monthSpend({
          bills: monthBills,
          manualTotal: period.manual_total,
          itemPrices: monthItems.map((i) => i.price),
        });
        return {
          period,
          total,
          source,
          items: monthItems.filter((i) => i.bought || i.price !== null),
          otherLines: ignored.filter((l) => billIds.has(l.bill_id)),
          bills: monthBills,
        };
      });
    },
  });
}
