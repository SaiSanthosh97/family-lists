import { useState } from "react";
import { Navigate, useParams } from "react-router-dom";
import { useGroup } from "../api/groups";
import { useItemMutations, useItems, useList } from "../api/lists";
import type { Item, List } from "../lib/types";
import { AddItemBar, ItemEditor, ItemRow } from "../components/items";
import { Button, Card, Empty, ErrorBox, Header, IconButton, Page, PageSpinner } from "../components/ui";
import MonthView from "./MonthView";
import { DotsIcon, ListMenu } from "../components/ListMenu";

export default function ListPage() {
  const { groupId = "", listId = "" } = useParams();
  const list = useList(listId);
  const group = useGroup(groupId);

  if (list.isLoading || group.isLoading) return <PageSpinner />;
  if (!list.data || !group.data) return <Navigate to={`/g/${groupId}`} replace />;

  return list.data.kind === "monthly" ? (
    <MonthView list={list.data} group={group.data} />
  ) : (
    <TodoView list={list.data} groupId={groupId} />
  );
}

function TodoView({ list, groupId }: { list: List; groupId: string }) {
  const items = useItems(list.id, null);
  const { add, update, remove, removeMany, swapPositions } = useItemMutations(list.id, null);
  const [editing, setEditing] = useState<Item | null>(null);
  const [menu, setMenu] = useState(false);

  const all = items.data ?? [];
  const open = all.filter((i) => !i.bought);
  const done = all.filter((i) => i.bought);

  function move(item: Item, dir: -1 | 1) {
    const idx = open.findIndex((i) => i.id === item.id);
    const other = open[idx + dir];
    if (idx >= 0 && other) swapPositions.mutate({ a: open[idx], b: other });
  }

  return (
    <>
      <Header
        title={list.name}
        subtitle={`${open.length} to do`}
        back={`/g/${groupId}`}
        right={
          <IconButton label="List options" onClick={() => setMenu(true)}>
            <DotsIcon />
          </IconButton>
        }
      />
      <Page>
        <div className="mb-4">
          <AddItemBar onAdd={(name, qty) => add.mutate({ name, qty })} placeholder="Add a task…" />
        </div>
        <ErrorBox error={items.error ?? add.error ?? update.error} />
        {items.isLoading ? (
          <PageSpinner />
        ) : all.length === 0 ? (
          <Empty title="Nothing to do">Add the first task above.</Empty>
        ) : (
          <>
            {open.length > 0 && (
              <Card className="py-0">
                <ul>
                  {open.map((i) => (
                    <ItemRow
                      key={i.id}
                      item={i}
                      checked={false}
                      toggleLabel="Mark done"
                      onToggle={() => update.mutate({ id: i.id, patch: { bought: true } })}
                      onOpen={() => setEditing(i)}
                    />
                  ))}
                </ul>
              </Card>
            )}
            {done.length > 0 && (
              <div className="mt-6">
                <div className="mb-2 flex items-center justify-between">
                  <h2 className="text-sm font-semibold tracking-wide text-stone-500 uppercase">Done ({done.length})</h2>
                  <Button
                    variant="ghost"
                    className="px-2 py-1 text-sm"
                    onClick={() => confirm("Clear all completed tasks?") && removeMany.mutate(done.map((d) => d.id))}
                  >
                    Clear done
                  </Button>
                </div>
                <Card className="py-0">
                  <ul>
                    {done.map((i) => (
                      <ItemRow
                        key={i.id}
                        item={i}
                        checked
                        strike
                        toggleLabel="Mark not done"
                        onToggle={() => update.mutate({ id: i.id, patch: { bought: false } })}
                        onOpen={() => setEditing(i)}
                      />
                    ))}
                  </ul>
                </Card>
              </div>
            )}
          </>
        )}
      </Page>

      <ItemEditor
        item={editing}
        monthly={false}
        onClose={() => setEditing(null)}
        saving={update.isPending}
        error={update.error}
        onSave={(patch) => editing && update.mutate({ id: editing.id, patch }, { onSuccess: () => setEditing(null) })}
        onDelete={() => editing && remove.mutate(editing.id, { onSuccess: () => setEditing(null) })}
        onMove={editing && !editing.bought ? (dir) => move(editing, dir) : undefined}
      />
      <ListMenu list={list} open={menu} onClose={() => setMenu(false)} />
    </>
  );
}
