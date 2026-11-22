// Runs the real migration inside an in-process Postgres (PGlite) with small
// stand-ins for Supabase's auth/storage schemas, then checks that groups are
// private from each other. Run with: npm run test:db
import { PGlite } from "@electric-sql/pglite";
import { readFileSync, readdirSync } from "node:fs";
import { join } from "node:path";
import { beforeAll, describe, expect, it } from "vitest";

const MIGRATIONS = join(process.cwd(), "supabase", "migrations");

const SUPABASE_STUBS = `
  create role anon nologin;
  create role authenticated nologin;
  create schema auth;
  create table auth.users (
    id uuid primary key,
    email text,
    raw_user_meta_data jsonb default '{}'::jsonb
  );
  create function auth.uid() returns uuid language sql stable as $$
    select nullif(current_setting('request.jwt.claim.sub', true), '')::uuid
  $$;
  create schema storage;
  create table storage.buckets (
    id text primary key, name text, public boolean,
    file_size_limit bigint, allowed_mime_types text[]
  );
  create table storage.objects (
    id uuid primary key default gen_random_uuid(),
    bucket_id text references storage.buckets (id),
    name text
  );
  alter table storage.objects enable row level security;
  create publication supabase_realtime;
  grant usage on schema auth, storage, public to anon, authenticated;
  grant execute on function auth.uid() to anon, authenticated;
`;

const GRANTS = `
  grant select, insert, update, delete on all tables in schema public to authenticated;
  grant select, insert, update, delete on storage.objects to authenticated;
  grant select on storage.buckets to authenticated;
`;

const A = "aaaaaaaa-0000-4000-8000-000000000001"; // Sai
const B = "aaaaaaaa-0000-4000-8000-000000000002"; // spouse
const C = "cccccccc-0000-4000-8000-000000000003"; // someone else's household

let db: PGlite;

async function as<T = Record<string, unknown>>(user: string, sql: string, params: unknown[] = []) {
  await db.exec(`reset role; select set_config('request.jwt.claim.sub', '${user}', false); set role authenticated;`);
  try {
    return (await db.query<T>(sql, params)).rows;
  } finally {
    await db.exec("reset role;");
  }
}

async function asFails(user: string, sql: string, params: unknown[] = []) {
  let failed = false;
  try {
    await as(user, sql, params);
  } catch {
    failed = true;
  }
  return failed;
}

let groupA = "";
let groupC = "";
let listA = "";
let periodA = "";
let itemA = "";
let billA = "";

beforeAll(async () => {
  db = new PGlite();
  await db.exec(SUPABASE_STUBS);
  for (const file of readdirSync(MIGRATIONS).filter((f) => f.endsWith(".sql")).sort()) {
    await db.exec(readFileSync(join(MIGRATIONS, file), "utf8"));
  }
  await db.exec(GRANTS);
  await db.exec(`
    insert into auth.users (id, email, raw_user_meta_data) values
      ('${A}', 'a@example.com', '{"full_name":"User A"}'),
      ('${B}', 'b@example.com', '{}'),
      ('${C}', 'c@example.com', '{}');
  `);

  groupA = (await as<{ id: string }>(A, "select id from public.create_group('Home', 'INR')"))[0].id;
  groupC = (await as<{ id: string }>(C, "select id from public.create_group('Other house')"))[0].id;
  listA = (
    await as<{ id: string }>(A, "insert into lists (group_id, name, kind) values ($1, 'Groceries', 'monthly') returning id", [groupA])
  )[0].id;
  periodA = (
    await as<{ id: string }>(A, "insert into list_periods (list_id, period) values ($1, '2026-11') returning id", [listA])
  )[0].id;
  itemA = (
    await as<{ id: string }>(A, "insert into items (list_id, period_id, name) values ($1, $2, 'Milk') returning id", [listA, periodA])
  )[0].id;
  billA = (
    await as<{ id: string }>(A, "insert into bills (period_id, storage_path) values ($1, $2) returning id", [periodA, `${groupA}/bill.jpg`])
  )[0].id;
  await as(A, "insert into storage.objects (bucket_id, name) values ('bills', $1)", [`${groupA}/bill.jpg`]);
});

describe("profiles", () => {
  it("are created on signup with a display name", async () => {
    const rows = await as<{ display_name: string }>(A, "select display_name from profiles where id = $1", [A]);
    expect(rows[0].display_name).toBe("User A");
  });

  it("strangers cannot see each other's profiles", async () => {
    expect(await as(C, "select * from profiles where id = $1", [A])).toHaveLength(0);
  });
});

describe("group privacy", () => {
  it("the creator becomes owner and sees their group", async () => {
    const rows = await as<{ role: string }>(A, "select role from group_members where group_id = $1", [groupA]);
    expect(rows).toEqual([{ role: "owner" }]);
  });

  it("another household sees none of group A's data", async () => {
    for (const table of ["list_periods", "items", "bills", "bill_lines", "lists"]) {
      expect(await as(C, `select * from ${table}`), table).toEqual([]);
    }
    expect(await as(C, "select * from groups where id = $1", [groupA])).toHaveLength(0);
    expect(await as(C, "select * from group_members where group_id = $1", [groupA])).toHaveLength(0);
    expect(await as(C, "select * from join_requests where group_id = $1", [groupA])).toHaveLength(0);
    expect(await as(C, "select * from storage.objects where name like $1", [`${groupA}/%`])).toHaveLength(0);
  });

  it("another household cannot write into group A", async () => {
    expect(await asFails(C, "insert into lists (group_id, name, kind) values ($1, 'x', 'todo')", [groupA])).toBe(true);
    expect(await asFails(C, "insert into items (list_id, period_id, name) values ($1, $2, 'x')", [listA, periodA])).toBe(true);
    expect(await asFails(C, "insert into storage.objects (bucket_id, name) values ('bills', $1)", [`${groupA}/evil.jpg`])).toBe(true);
    const updated = await as(C, "update items set name = 'hacked' where id = $1 returning id", [itemA]);
    expect(updated).toHaveLength(0);
    const deleted = await as(C, "delete from lists where id = $1 returning id", [listA]);
    expect(deleted).toHaveLength(0);
  });

  it("an item cannot point at another group's period", async () => {
    const listC = (
      await as<{ id: string }>(C, "insert into lists (group_id, name, kind) values ($1, 'Mine', 'monthly') returning id", [groupC])
    )[0].id;
    expect(await asFails(C, "insert into items (list_id, period_id, name) values ($1, $2, 'x')", [listC, periodA])).toBe(true);
  });

  it("nobody can add themselves to a group directly", async () => {
    expect(await asFails(C, "insert into group_members (group_id, user_id) values ($1, $2)", [groupA, C])).toBe(true);
  });

  it("another household cannot confirm group A's bill", async () => {
    expect(await asFails(C, "select public.confirm_bill($1, '{}'::jsonb, '[]'::jsonb)", [billA])).toBe(true);
  });
});

describe("joining a group", () => {
  it("a pending requester sees nothing until accepted, then sees everything", async () => {
    const code = (await as<{ invite_code: string }>(A, "select invite_code from groups where id = $1", [groupA]))[0].invite_code;
    const res = await as<{ r: { status: string; group_name: string } }>(B, "select public.request_join($1) as r", [code.toLowerCase()]);
    expect(res[0].r).toMatchObject({ status: "pending", group_name: "Home" });

    expect(await as(B, "select * from lists where group_id = $1", [groupA])).toHaveLength(0);
    expect(await as(B, "select * from public.my_pending_requests()")).toHaveLength(1);

    // the requester cannot accept their own request
    const reqId = (await as<{ id: string }>(A, "select id from join_requests where user_id = $1", [B]))[0].id;
    expect(await asFails(B, "select public.decide_join_request($1, true)", [reqId])).toBe(true);
    // strangers cannot either
    expect(await asFails(C, "select public.decide_join_request($1, true)", [reqId])).toBe(true);

    // group A can see who is asking
    expect(await as(A, "select id from profiles where id = $1", [B])).toHaveLength(1);

    await as(A, "select public.decide_join_request($1, true)", [reqId]);
    expect(await as(B, "select * from lists where group_id = $1", [groupA])).toHaveLength(1);
    expect(await as(B, "select * from storage.objects where name like $1", [`${groupA}/%`])).toHaveLength(1);
    expect(await as(B, "select * from groups where id = $1", [groupC])).toHaveLength(0);
  });

  it("an unknown code reveals nothing", async () => {
    const res = await as<{ r: { status: string } }>(C, "select public.request_join('ZZZZZZ') as r");
    expect(res[0].r).toEqual({ status: "not_found" });
  });

  it("a person can belong to several groups", async () => {
    const code = (await as<{ invite_code: string }>(C, "select invite_code from groups where id = $1", [groupC]))[0].invite_code;
    await as(A, "select public.request_join($1)", [code]);
    const reqId = (await as<{ id: string }>(C, "select id from join_requests where user_id = $1 and status = 'pending'", [A]))[0].id;
    await as(C, "select public.decide_join_request($1, true)", [reqId]);
    const groups = await as(A, "select id from groups order by name");
    expect(groups).toHaveLength(2);
    // ...but B (only in Home) still cannot see C's group
    expect(await as(B, "select * from groups where id = $1", [groupC])).toHaveLength(0);
  });

  it("when the last owner leaves, another member becomes owner", async () => {
    await as(C, "select public.leave_group($1)", [groupC]);
    const rows = await as<{ user_id: string; role: string }>(A, "select user_id, role from group_members where group_id = $1", [groupC]);
    expect(rows).toEqual([{ user_id: A, role: "owner" }]);
    expect(await as(C, "select * from lists where group_id = $1", [groupC])).toHaveLength(0);
  });
});

describe("confirm_bill", () => {
  it("prices matched items, adds unlisted purchases and marks the month shopped", async () => {
    const lines = await as<{ id: string }>(
      A,
      `insert into bill_lines (bill_id, position, name, line_total) values
        ($1, 1, 'Milk', 3.50), ($1, 2, 'Bananas', 1.20), ($1, 3, 'Carrier bag', 0.10)
       returning id`,
      [billA],
    );
    const payload = [
      { id: lines[0].id, name: "Milk", qty: 2, unit_price: 1.75, line_total: 3.5, action: "match", matched_item_id: itemA },
      { id: lines[1].id, name: "Bananas", qty: 1, unit_price: 1.2, line_total: 1.2, action: "add", matched_item_id: null },
      { id: lines[2].id, name: "Carrier bag", qty: 1, unit_price: 0.1, line_total: 0.1, action: "ignore", matched_item_id: null },
    ];
    await as(A, "select public.confirm_bill($1, $2::jsonb, $3::jsonb)", [
      billA,
      JSON.stringify({ store_name: "Corner Shop", bill_date: "2026-11-20", total: 4.8 }),
      JSON.stringify(payload),
    ]);

    const items = await as<{ name: string; price: string; bought: boolean; source: string }>(
      B,
      "select name, price::text, bought, source from items where period_id = $1 order by position, name",
      [periodA],
    );
    expect(items).toEqual([
      { name: "Milk", price: "3.50", bought: true, source: "added" },
      { name: "Bananas", price: "1.20", bought: true, source: "bill" },
    ]);
    const period = await as<{ status: string }>(A, "select status from list_periods where id = $1", [periodA]);
    expect(period[0].status).toBe("shopped");
    const bill = await as<{ status: string; total: string }>(A, "select status, total::text from bills where id = $1", [billA]);
    expect(bill[0]).toEqual({ status: "confirmed", total: "4.80" });
  });

  it("refuses to confirm the same bill twice", async () => {
    expect(await asFails(A, "select public.confirm_bill($1, '{}'::jsonb, '[]'::jsonb)", [billA])).toBe(true);
  });
});
