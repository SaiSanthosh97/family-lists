export type Role = "owner" | "member";
export type ListKind = "monthly" | "todo";
export type PeriodStatus = "draft" | "final" | "shopped";
export type ItemSource = "carried" | "added" | "bill";
export type BillStatus = "uploaded" | "parsing" | "parsed" | "confirmed" | "failed";
export type LineAction = "match" | "add" | "ignore";

export interface Profile {
  id: string;
  email: string | null;
  display_name: string | null;
  avatar_url: string | null;
}

export interface Group {
  id: string;
  name: string;
  invite_code: string;
  currency: string;
  created_by: string;
  created_at: string;
}

export interface Member {
  group_id: string;
  user_id: string;
  role: Role;
  joined_at: string;
  profile: Pick<Profile, "display_name" | "email" | "avatar_url"> | null;
}

export interface JoinRequest {
  id: string;
  group_id: string;
  user_id: string;
  status: "pending" | "accepted" | "rejected";
  created_at: string;
  profile: Pick<Profile, "display_name" | "email"> | null;
}

export interface List {
  id: string;
  group_id: string;
  name: string;
  kind: ListKind;
  archived: boolean;
  created_at: string;
}

export interface Period {
  id: string;
  list_id: string;
  period: string;
  status: PeriodStatus;
  manual_total: number | null;
  created_at: string;
}

export interface Item {
  id: string;
  list_id: string;
  period_id: string | null;
  name: string;
  qty: number;
  unit: string | null;
  included: boolean;
  bought: boolean;
  source: ItemSource;
  skipped_last: boolean;
  price: number | null;
  notes: string | null;
  position: number;
  created_at: string;
  updated_at: string;
}

export interface Bill {
  id: string;
  period_id: string;
  storage_path: string;
  store_name: string | null;
  bill_date: string | null;
  subtotal: number | null;
  tax: number | null;
  discount: number | null;
  total: number | null;
  status: BillStatus;
  error: string | null;
  created_at: string;
}

export interface BillLine {
  id: string;
  bill_id: string;
  position: number;
  raw_text: string | null;
  name: string;
  qty: number | null;
  unit_price: number | null;
  line_total: number | null;
  matched_item_id: string | null;
  action: LineAction;
}
