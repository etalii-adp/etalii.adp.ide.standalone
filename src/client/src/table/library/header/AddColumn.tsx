import { useRef, useState } from "react";
import type { TableGesture } from "../api/tableEvents";
import type { TableDefinition } from "../definition/tableDefinition";
import { addableKinds } from "./columnActions";
import { NewColumnPanel } from "./NewColumnPanel";

export interface AddColumnProps {
  definition: TableDefinition;
  raise: (gesture: TableGesture) => void;
  /** Makes a gesture and answers with what it was answered with: for a property that asks something before it is made. */
  ask?: (gesture: TableGesture) => Promise<string>;
}

/**
 * The `+` after the last column: it opens the panel that asks for the new property's name and
 * then its type, and raises one gesture for it. It offers the kinds the module's definition lets
 * be added and no others, and is not shown at all when there is none.
 */
export function AddColumn({ definition, raise, ask }: AddColumnProps) {
  const buttonRef = useRef<HTMLButtonElement>(null);
  const [panelAt, setPanelAt] = useState<{ x: number; y: number } | null>(null);

  if (addableKinds(definition).length === 0) {
    return null;
  }

  return (
    <div className="table-add-column" role="presentation">
      <button
        ref={buttonRef}
        type="button"
        className="table-add-column-button"
        aria-label="Add a property"
        aria-haspopup="dialog"
        aria-expanded={panelAt !== null}
        onClick={() => {
          const rect = buttonRef.current?.getBoundingClientRect();
          setPanelAt({ x: rect?.left ?? 0, y: rect?.bottom ?? 0 });
        }}
      >
        <span className="mdi mdi-plus" aria-hidden="true" />
      </button>
      {panelAt !== null && <NewColumnPanel definition={definition} position={panelAt} raise={raise} ask={ask} onClose={() => setPanelAt(null)} />}
    </div>
  );
}
