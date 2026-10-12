import { useEffect, useRef, useState } from "react";
import type { TableGesture } from "../api/tableEvents";
import type { TableDefinition } from "../definition/tableDefinition";
import { addableKinds } from "./columnActions";

/** How near the window's left edge the panel may stand, in pixels. */
const PANEL_MARGIN = 8;

export interface NewColumnPanelProps {
  definition: TableDefinition;
  /** Where the panel stands, in the window. */
  position: { x: number; y: number };
  /** The column the new one goes beside, and on which side of it; left out, it goes after the last. */
  beside?: { columnId: string; side: "left" | "right" };
  raise: (gesture: TableGesture) => void;
  onClose: () => void;
}

/**
 * A new property, asked for as Notion asks: its name first, then its type. Choosing a type makes
 * the property under the name that was typed; with no name typed it takes its type's, and the
 * table asks for one in its header once it is there. Leaving the panel makes nothing.
 */
export function NewColumnPanel({ definition, position, beside, raise, onClose }: NewColumnPanelProps) {
  const panelRef = useRef<HTMLDivElement>(null);
  const nameRef = useRef<HTMLInputElement>(null);
  const [name, setName] = useState("");

  useEffect(() => nameRef.current?.focus(), []);

  // Leaving the panel - a press anywhere else, or Escape - closes it.
  useEffect(() => {
    const onPress = (event: PointerEvent) => {
      if (panelRef.current !== null && event.target instanceof Node && !panelRef.current.contains(event.target)) {
        onClose();
      }
    };
    const onKey = (event: KeyboardEvent) => {
      if (event.key === "Escape") {
        onClose();
      }
    };
    document.addEventListener("pointerdown", onPress);
    document.addEventListener("keydown", onKey);
    return () => {
      document.removeEventListener("pointerdown", onPress);
      document.removeEventListener("keydown", onKey);
    };
  }, [onClose]);

  const add = (kind: string) => {
    const typed = name.trim();
    raise({
      kind: "addColumn",
      ...(beside === undefined ? {} : { targetId: beside.columnId }),
      ...(typed === "" ? {} : { values: [typed] }),
      settings: beside === undefined ? { type: kind } : { side: beside.side, type: kind },
    });
    onClose();
  };

  return (
    <div ref={panelRef} className="table-options-editor table-new-column" role="dialog" aria-label="New property" style={{ left: Math.max(PANEL_MARGIN, position.x), top: position.y }}>
      <input ref={nameRef} type="text" aria-label="Property name" placeholder="Property name" value={name} onChange={(event) => setName(event.target.value)} onKeyDown={(event) => event.stopPropagation()} />
      <ul className="table-new-column-kinds" role="menu" aria-label="Type">
        {addableKinds(definition).map((kind) => (
          <li key={kind.kind} role="none">
            <button type="button" role="menuitem" className="table-new-column-kind" onClick={() => add(kind.kind)}>
              <span className={`mdi ${kind.icon}`} aria-hidden="true" />
              <span>{kind.label}</span>
            </button>
          </li>
        ))}
      </ul>
    </div>
  );
}
