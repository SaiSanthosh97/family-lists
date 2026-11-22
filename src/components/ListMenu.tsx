import { useState } from "react";
import { useNavigate } from "react-router-dom";
import { useDeleteList, useUpdateList } from "../api/lists";
import type { List } from "../lib/types";
import { Button, ErrorBox, Field, Input, Sheet } from "./ui";

export function ListMenu({ list, open, onClose }: { list: List; open: boolean; onClose: () => void }) {
  const [name, setName] = useState(list.name);
  const update = useUpdateList(list);
  const del = useDeleteList(list);
  const navigate = useNavigate();
  return (
    <Sheet open={open} onClose={onClose} title="List options">
      <div className="space-y-4">
        <Field label="List name">
          <div className="flex gap-2">
            <Input value={name} onChange={(e) => setName(e.target.value)} maxLength={80} />
            <Button
              variant="secondary"
              disabled={!name.trim() || name.trim() === list.name}
              busy={update.isPending}
              onClick={() => update.mutate({ name: name.trim() }, { onSuccess: onClose })}
            >
              Save
            </Button>
          </div>
        </Field>
        <Button
          variant="danger"
          className="w-full"
          busy={del.isPending}
          onClick={() => {
            if (confirm(`Delete "${list.name}" and all its history${list.kind === "monthly" ? ", bills and spending" : ""}? This can't be undone.`))
              del.mutate(undefined, { onSuccess: () => navigate(`/g/${list.group_id}`, { replace: true }) });
          }}
        >
          Delete list
        </Button>
        <ErrorBox error={update.error ?? del.error} />
      </div>
    </Sheet>
  );
}

export const DotsIcon = () => (
  <svg width="22" height="22" viewBox="0 0 24 24" aria-hidden>
    <circle cx="5" cy="12" r="2" fill="currentColor" />
    <circle cx="12" cy="12" r="2" fill="currentColor" />
    <circle cx="19" cy="12" r="2" fill="currentColor" />
  </svg>
);
