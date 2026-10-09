import { useRef, useState } from "react";
import { ContextMenu } from "../../../shell/context/ContextMenu";
import type { TableGesture } from "../api/tableEvents";
import type { TableDefinition } from "../definition/tableDefinition";
import { addableKinds } from "./columnActions";

export interface AddColumnProps {
  definition: TableDefinition;
  raise: (gesture: TableGesture) => void;
}

/**
 * The `+` after the last column: it asks which kind the new column holds, and raises one
 * gesture for it. It offers the kinds the module's definition lets be added and no others, and
 * is not shown at all when there is none.
 */
export function AddColumn({ definition, raise }: AddColumnProps) {
  const buttonRef = useRef<HTMLButtonElement>(null);
  const [menuAt, setMenuAt] = useState<{ x: number; y: number } | null>(null);
  const kinds = addableKinds(definition);

  if (kinds.length === 0) {
    return null;
  }

  return (
    <div className="table-add-column" role="presentation">
      <button
        ref={buttonRef}
        type="button"
        className="table-add-column-button"
        aria-label="Add a property"
        aria-haspopup="menu"
        aria-expanded={menuAt !== null}
        onClick={() => {
          const rect = buttonRef.current?.getBoundingClientRect();
          setMenuAt({ x: rect?.left ?? 0, y: rect?.bottom ?? 0 });
        }}
      >
        <span className="mdi mdi-plus" aria-hidden="true" />
      </button>
      <ContextMenu
        open={menuAt !== null}
        groups={[kinds.map((kind) => ({ id: `add:${kind.kind}`, label: kind.label, icon: kind.icon, onSelect: () => raise({ kind: "addColumn", settings: { type: kind.kind } }) }))]}
        position={menuAt ?? { x: 0, y: 0 }}
        onClose={() => setMenuAt(null)}
      />
    </div>
  );
}
