-- Family Lists: schema, row-level security, RPCs, storage policies.
-- Every row is reachable only by members of the group that owns it.

-- ───────────────────────────── Profiles ─────────────────────────────
create table public.profiles (
  id uuid primary key references auth.users (id) on delete cascade,
  email text,
  display_name text,
  avatar_url text,
  created_at timestamptz not null default now()
);

create or replace function public.handle_new_user()
returns trigger
language plpgsql
security definer
set search_path = public
as $$
begin
  insert into public.profiles (id, email, display_name, avatar_url)
  values (
    new.id,
    new.email,
    coalesce(
      new.raw_user_meta_data ->> 'full_name',
      new.raw_user_meta_data ->> 'name',
      split_part(coalesce(new.email, 'friend'), '@', 1)
    ),
    new.raw_user_meta_data ->> 'avatar_url'
  )
  on conflict (id) do nothing;
  return new;
end;
$$;

create trigger on_auth_user_created
  after insert on auth.users
  for each row execute function public.handle_new_user();

-- ───────────────────────────── Groups ─────────────────────────────
create or replace function public.gen_invite_code()
returns text
language plpgsql
volatile
as $$
declare
  alphabet constant text := 'ABCDEFGHJKMNPQRSTUVWXYZ23456789'; -- no 0/O/1/I/L
  code text := '';
begin
  for i in 1..6 loop
    code := code || substr(alphabet, 1 + floor(random() * length(alphabet))::int, 1);
  end loop;
  return code;
end;
$$;

create table public.groups (
  id uuid primary key default gen_random_uuid(),
  name text not null check (char_length(btrim(name)) between 1 and 60),
  invite_code text not null unique default public.gen_invite_code(),
  currency text not null default 'USD' check (currency ~ '^[A-Z]{3}$'),
  created_by uuid not null references public.profiles (id),
  created_at timestamptz not null default now()
);

create table public.group_members (
  group_id uuid not null references public.groups (id) on delete cascade,
  user_id uuid not null references public.profiles (id) on delete cascade,
  role text not null default 'member' check (role in ('owner', 'member')),
  joined_at timestamptz not null default now(),
  primary key (group_id, user_id)
);
create index group_members_user_idx on public.group_members (user_id);

create table public.join_requests (
  id uuid primary key default gen_random_uuid(),
  group_id uuid not null references public.groups (id) on delete cascade,
  user_id uuid not null references public.profiles (id) on delete cascade,
  status text not null default 'pending' check (status in ('pending', 'accepted', 'rejected')),
  created_at timestamptz not null default now(),
  decided_by uuid references public.profiles (id),
  decided_at timestamptz
);
create unique index join_requests_one_pending
  on public.join_requests (group_id, user_id) where status = 'pending';

-- ───────────────────────────── Lists ─────────────────────────────
create table public.lists (
  id uuid primary key default gen_random_uuid(),
  group_id uuid not null references public.groups (id) on delete cascade,
  name text not null check (char_length(btrim(name)) between 1 and 80),
  kind text not null check (kind in ('monthly', 'todo')),
  archived boolean not null default false,
  created_by uuid references public.profiles (id) default auth.uid(),
  created_at timestamptz not null default now()
);
create index lists_group_idx on public.lists (group_id);

-- One row per list per month ("2026-11").
create table public.list_periods (
  id uuid primary key default gen_random_uuid(),
  list_id uuid not null references public.lists (id) on delete cascade,
  period text not null check (period ~ '^\d{4}-(0[1-9]|1[0-2])$'),
  status text not null default 'draft' check (status in ('draft', 'final', 'shopped')),
  manual_total numeric(12, 2) check (manual_total is null or manual_total >= 0),
  created_at timestamptz not null default now(),
  unique (list_id, period)
);

-- Items of a monthly list belong to a period; to-do items have period_id = null.
create table public.items (
  id uuid primary key default gen_random_uuid(),
  list_id uuid not null references public.lists (id) on delete cascade,
  period_id uuid references public.list_periods (id) on delete cascade,
  name text not null check (char_length(btrim(name)) between 1 and 120),
  qty numeric(10, 3) not null default 1 check (qty > 0),
  unit text,
  included boolean not null default true,      -- false = "not needed this month"
  bought boolean not null default false,       -- to-do lists use this as "done"
  source text not null default 'added' check (source in ('carried', 'added', 'bill')),
  skipped_last boolean not null default false, -- carried over but was not needed last month
  price numeric(12, 2) check (price is null or price >= 0), -- amount spent on this item
  notes text,
  position integer not null default 0,
  created_by uuid references public.profiles (id) default auth.uid(),
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now()
);
create index items_period_idx on public.items (period_id);
create index items_list_idx on public.items (list_id) where period_id is null;

create or replace function public.touch_updated_at()
returns trigger language plpgsql as $$
begin
  new.updated_at := now();
  return new;
end;
$$;
create trigger items_touch before update on public.items
  for each row execute function public.touch_updated_at();

-- ───────────────────────────── Bills ─────────────────────────────
create table public.bills (
  id uuid primary key default gen_random_uuid(),
  period_id uuid not null references public.list_periods (id) on delete cascade,
  storage_path text not null,
  store_name text,
  bill_date date,
  subtotal numeric(12, 2),
  tax numeric(12, 2),
  discount numeric(12, 2),
  total numeric(12, 2),
  status text not null default 'uploaded'
    check (status in ('uploaded', 'parsing', 'parsed', 'confirmed', 'failed')),
  error text,
  raw_json jsonb,
  created_by uuid references public.profiles (id) default auth.uid(),
  created_at timestamptz not null default now()
);
create index bills_period_idx on public.bills (period_id);

create table public.bill_lines (
  id uuid primary key default gen_random_uuid(),
  bill_id uuid not null references public.bills (id) on delete cascade,
  position integer not null default 0,
  raw_text text,
  name text not null,
  qty numeric(10, 3),
  unit_price numeric(12, 2),
  line_total numeric(12, 2),
  matched_item_id uuid references public.items (id) on delete set null,
  action text not null default 'add' check (action in ('match', 'add', 'ignore'))
);
create index bill_lines_bill_idx on public.bill_lines (bill_id);

-- ───────────────────────────── Access helpers ─────────────────────────────
-- security definer so policies can look at membership without recursive RLS.
create or replace function public.is_group_member(gid uuid)
returns boolean language sql stable security definer set search_path = public as $$
  select exists (
    select 1 from public.group_members where group_id = gid and user_id = auth.uid()
  );
$$;

create or replace function public.is_group_owner(gid uuid)
returns boolean language sql stable security definer set search_path = public as $$
  select exists (
    select 1 from public.group_members
    where group_id = gid and user_id = auth.uid() and role = 'owner'
  );
$$;

create or replace function public.can_access_list(lid uuid)
returns boolean language sql stable security definer set search_path = public as $$
  select exists (
    select 1 from public.lists l
    join public.group_members m on m.group_id = l.group_id
    where l.id = lid and m.user_id = auth.uid()
  );
$$;

create or replace function public.can_access_period(pid uuid)
returns boolean language sql stable security definer set search_path = public as $$
  select exists (
    select 1 from public.list_periods p
    join public.lists l on l.id = p.list_id
    join public.group_members m on m.group_id = l.group_id
    where p.id = pid and m.user_id = auth.uid()
  );
$$;

-- An item may only point at a period that belongs to its own list.
create or replace function public.can_write_item(lid uuid, pid uuid)
returns boolean language sql stable security definer set search_path = public as $$
  select public.can_access_list(lid) and (
    pid is null
    or exists (select 1 from public.list_periods p where p.id = pid and p.list_id = lid)
  );
$$;

create or replace function public.can_access_bill(bid uuid)
returns boolean language sql stable security definer set search_path = public as $$
  select exists (
    select 1 from public.bills b
    where b.id = bid and public.can_access_period(b.period_id)
  );
$$;

-- People can see profiles of people they share a group with, and of people
-- asking to join one of their groups.
create or replace function public.can_see_profile(pid uuid)
returns boolean language sql stable security definer set search_path = public as $$
  select pid = auth.uid()
    or exists (
      select 1 from public.group_members a
      join public.group_members b on a.group_id = b.group_id
      where a.user_id = auth.uid() and b.user_id = pid
    )
    or exists (
      select 1 from public.join_requests r
      join public.group_members m on m.group_id = r.group_id
      where r.user_id = pid and r.status = 'pending' and m.user_id = auth.uid()
    );
$$;

-- Storage paths look like "<group_id>/<bill_id>.jpg".
create or replace function public.is_group_folder(object_name text)
returns boolean language plpgsql stable security definer set search_path = public as $$
declare
  first_segment text := split_part(object_name, '/', 1);
begin
  if first_segment !~ '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$' then
    return false;
  end if;
  return public.is_group_member(first_segment::uuid);
end;
$$;

-- ───────────────────────────── Row-level security ─────────────────────────────
alter table public.profiles enable row level security;
alter table public.groups enable row level security;
alter table public.group_members enable row level security;
alter table public.join_requests enable row level security;
alter table public.lists enable row level security;
alter table public.list_periods enable row level security;
alter table public.items enable row level security;
alter table public.bills enable row level security;
alter table public.bill_lines enable row level security;

create policy profiles_select on public.profiles for select to authenticated
  using (public.can_see_profile(id));
create policy profiles_update on public.profiles for update to authenticated
  using (id = auth.uid()) with check (id = auth.uid());

-- Groups are created through create_group(); members read, owners edit/delete.
create policy groups_select on public.groups for select to authenticated
  using (public.is_group_member(id));
create policy groups_update on public.groups for update to authenticated
  using (public.is_group_owner(id)) with check (public.is_group_owner(id));
create policy groups_delete on public.groups for delete to authenticated
  using (public.is_group_owner(id));

-- Membership rows are only created by create_group()/decide_join_request().
create policy members_select on public.group_members for select to authenticated
  using (public.is_group_member(group_id));
create policy members_delete_by_owner on public.group_members for delete to authenticated
  using (public.is_group_owner(group_id) and user_id <> auth.uid());

create policy requests_select on public.join_requests for select to authenticated
  using (user_id = auth.uid() or public.is_group_member(group_id));

create policy lists_all on public.lists for all to authenticated
  using (public.is_group_member(group_id))
  with check (public.is_group_member(group_id));

create policy periods_all on public.list_periods for all to authenticated
  using (public.can_access_list(list_id))
  with check (public.can_access_list(list_id));

create policy items_all on public.items for all to authenticated
  using (public.can_write_item(list_id, period_id))
  with check (public.can_write_item(list_id, period_id));

create policy bills_all on public.bills for all to authenticated
  using (public.can_access_period(period_id))
  with check (public.can_access_period(period_id));

create policy bill_lines_all on public.bill_lines for all to authenticated
  using (public.can_access_bill(bill_id))
  with check (public.can_access_bill(bill_id));

-- ───────────────────────────── RPCs ─────────────────────────────
create or replace function public.create_group(p_name text, p_currency text default 'USD')
returns public.groups
language plpgsql security definer set search_path = public as $$
declare
  g public.groups;
begin
  if auth.uid() is null then
    raise exception 'Not signed in';
  end if;
  loop
    begin
      insert into public.groups (name, currency, created_by)
      values (btrim(p_name), upper(coalesce(p_currency, 'USD')), auth.uid())
      returning * into g;
      exit;
    exception when unique_violation then
      -- invite code collision: try again with a fresh code
    end;
  end loop;
  insert into public.group_members (group_id, user_id, role) values (g.id, auth.uid(), 'owner');
  return g;
end;
$$;

-- Ask to join a group by its invite code. Never reveals anything but the name.
create or replace function public.request_join(p_code text)
returns jsonb
language plpgsql security definer set search_path = public as $$
declare
  g public.groups;
begin
  if auth.uid() is null then
    raise exception 'Not signed in';
  end if;
  select * into g from public.groups where invite_code = upper(btrim(p_code));
  if not found then
    return jsonb_build_object('status', 'not_found');
  end if;
  if exists (select 1 from public.group_members where group_id = g.id and user_id = auth.uid()) then
    return jsonb_build_object('status', 'member', 'group_id', g.id, 'group_name', g.name);
  end if;
  insert into public.join_requests (group_id, user_id)
  values (g.id, auth.uid())
  on conflict (group_id, user_id) where status = 'pending' do nothing;
  return jsonb_build_object('status', 'pending', 'group_name', g.name);
end;
$$;

-- Requests I have sent that are still waiting (with the group's name).
create or replace function public.my_pending_requests()
returns table (id uuid, group_name text, created_at timestamptz)
language sql stable security definer set search_path = public as $$
  select r.id, g.name, r.created_at
  from public.join_requests r join public.groups g on g.id = r.group_id
  where r.user_id = auth.uid() and r.status = 'pending'
  order by r.created_at desc;
$$;

-- Any existing member can accept or reject a pending request.
create or replace function public.decide_join_request(p_request_id uuid, p_accept boolean)
returns void
language plpgsql security definer set search_path = public as $$
declare
  r public.join_requests;
begin
  select * into r from public.join_requests where id = p_request_id for update;
  if not found or r.status <> 'pending' then
    raise exception 'Request not found or already decided';
  end if;
  if not public.is_group_member(r.group_id) then
    raise exception 'Only group members can decide on join requests';
  end if;
  update public.join_requests
     set status = case when p_accept then 'accepted' else 'rejected' end,
         decided_by = auth.uid(),
         decided_at = now()
   where id = r.id;
  if p_accept then
    insert into public.group_members (group_id, user_id, role)
    values (r.group_id, r.user_id, 'member')
    on conflict do nothing;
  end if;
end;
$$;

-- Leave a group. If the last owner leaves, the longest-standing member becomes
-- owner; if nobody is left, the group and all its lists are deleted.
create or replace function public.leave_group(p_group_id uuid)
returns void
language plpgsql security definer set search_path = public as $$
declare
  next_owner uuid;
begin
  delete from public.group_members where group_id = p_group_id and user_id = auth.uid();
  if not found then
    raise exception 'You are not a member of this group';
  end if;
  if not exists (select 1 from public.group_members where group_id = p_group_id) then
    delete from public.groups where id = p_group_id;
    return;
  end if;
  if not exists (select 1 from public.group_members where group_id = p_group_id and role = 'owner') then
    select user_id into next_owner from public.group_members
      where group_id = p_group_id order by joined_at limit 1;
    update public.group_members set role = 'owner'
      where group_id = p_group_id and user_id = next_owner;
  end if;
end;
$$;

-- Apply a reviewed bill: save edited lines, add unlisted purchases as new items
-- (so next month's list carries them), write the amount spent onto each item
-- and mark the month as shopped. Runs with the caller's rights, so RLS applies.
create or replace function public.confirm_bill(p_bill_id uuid, p_bill jsonb, p_lines jsonb)
returns void
language plpgsql security invoker set search_path = public as $$
declare
  b public.bills;
  per public.list_periods;
  line jsonb;
  next_pos integer;
  new_item_id uuid;
begin
  select * into b from public.bills where id = p_bill_id;
  if not found then
    raise exception 'Bill not found';
  end if;
  if b.status = 'confirmed' then
    raise exception 'This bill has already been confirmed';
  end if;
  select * into per from public.list_periods where id = b.period_id;

  update public.bills set
    store_name = nullif(p_bill ->> 'store_name', ''),
    bill_date  = nullif(p_bill ->> 'bill_date', '')::date,
    subtotal   = nullif(p_bill ->> 'subtotal', '')::numeric,
    tax        = nullif(p_bill ->> 'tax', '')::numeric,
    discount   = nullif(p_bill ->> 'discount', '')::numeric,
    total      = nullif(p_bill ->> 'total', '')::numeric,
    status     = 'confirmed',
    error      = null
  where id = b.id;

  select coalesce(max(position), 0) into next_pos from public.items where period_id = per.id;

  for line in select * from jsonb_array_elements(coalesce(p_lines, '[]'::jsonb)) loop
    new_item_id := nullif(line ->> 'matched_item_id', '')::uuid;

    if line ->> 'action' = 'add' then
      next_pos := next_pos + 1;
      insert into public.items (list_id, period_id, name, qty, included, bought, source, position)
      values (
        per.list_id, per.id, btrim(line ->> 'name'),
        greatest(coalesce(nullif(line ->> 'qty', '')::numeric, 1), 0.001),
        true, true, 'bill', next_pos
      )
      returning id into new_item_id;
    elsif line ->> 'action' = 'match' then
      if new_item_id is null
         or not exists (select 1 from public.items where id = new_item_id and period_id = per.id) then
        raise exception 'Line "%" must be matched to an item on this month''s list', line ->> 'name';
      end if;
    else
      new_item_id := null; -- ignored line (bags, deposits, fees…)
    end if;

    update public.bill_lines set
      name            = btrim(line ->> 'name'),
      qty             = nullif(line ->> 'qty', '')::numeric,
      unit_price      = nullif(line ->> 'unit_price', '')::numeric,
      line_total      = nullif(line ->> 'line_total', '')::numeric,
      action          = line ->> 'action',
      matched_item_id = new_item_id
    where id = (line ->> 'id')::uuid and bill_id = b.id;
  end loop;

  -- Amount spent per item = sum of every confirmed bill line pointing at it.
  update public.items i set price = s.spent, bought = true, included = true
  from (
    select bl.matched_item_id, sum(coalesce(bl.line_total, 0)) as spent
    from public.bill_lines bl
    join public.bills b2 on b2.id = bl.bill_id
    where b2.period_id = per.id and b2.status = 'confirmed'
      and bl.action in ('match', 'add') and bl.matched_item_id is not null
    group by bl.matched_item_id
  ) s
  where i.id = s.matched_item_id;

  update public.list_periods set status = 'shopped' where id = per.id;
end;
$$;

revoke execute on function public.create_group(text, text) from anon;
revoke execute on function public.request_join(text) from anon;
revoke execute on function public.my_pending_requests() from anon;
revoke execute on function public.decide_join_request(uuid, boolean) from anon;
revoke execute on function public.leave_group(uuid) from anon;
revoke execute on function public.confirm_bill(uuid, jsonb, jsonb) from anon;

-- ───────────────────────────── Storage (bill photos) ─────────────────────────────
insert into storage.buckets (id, name, public, file_size_limit, allowed_mime_types)
values ('bills', 'bills', false, 10485760,
        array['image/jpeg', 'image/png', 'image/webp', 'application/pdf'])
on conflict (id) do nothing;

create policy bills_objects_select on storage.objects for select to authenticated
  using (bucket_id = 'bills' and public.is_group_folder(name));
create policy bills_objects_insert on storage.objects for insert to authenticated
  with check (bucket_id = 'bills' and public.is_group_folder(name));
create policy bills_objects_update on storage.objects for update to authenticated
  using (bucket_id = 'bills' and public.is_group_folder(name));
create policy bills_objects_delete on storage.objects for delete to authenticated
  using (bucket_id = 'bills' and public.is_group_folder(name));

-- ───────────────────────────── Realtime ─────────────────────────────
-- Live sync between phones (realtime respects the RLS policies above).
alter publication supabase_realtime add table public.items, public.list_periods, public.bills;
