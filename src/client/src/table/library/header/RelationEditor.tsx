import { useEffect, useRef, useState } from "react";
import { createPortal } from "react-dom";
import type { TableGesture } from "../api/tableEvents";
import type { TableColumn } from "../api/tableModel";
import { BACKLINK_NAME, OTHER_SIDE } from "./columnActions";

/** How near the window's left edge the panel may stand, in pixels. */
const PANEL_MARGIN = 8;

/** The room the panel is kept within the window's right edge, in pixels: about its own width. */
const PANEL_WIDTH = 260;

/** The settings a relation's column says under which table it relates to and how many rows a cell of it holds. */
const TARGET = "targetFile";
const LIMIT = "limit";

/**
 * Why a relation to a diagram offers no two-way box (spec 092, FR-025): a diagram has no column to
 * show the rows that link to it in, and lists them on each element instead.
 */
export const ONE_WAY_ONLY = "Shown in this table only: a diagram has no column to show it in. Its elements list the rows that link to them under Linked from.";

/** The setting a relation's column has only when its target is a diagram: the kinds of element Create can add there. */
const CREATE_KINDS = "createKinds";

/** What a relation's table is called where only its file is known: this table, or the file as the relation names it, by its name where that is a link (`$<id>@<file>`). */
export function targetLabel(target: string): string {
  if (target === "" || target === ".") {
    return "This table";
  }
  const at = target.startsWith("$") ? target.indexOf("@") : -1;
  return at >= 0 ? target.slice(at + 1) : target;
}

export interface RelationEditorProps {
  /** The relation's column. */
  column: TableColumn;
  /** Where the panel stands, in the window: under the column's header. */
  position: { x: number; y: number };
  /** Makes a gesture and answers with its refusal, or with nothing when it was accepted. */
  ask: (gesture: TableGesture) => Promise<string>;
  onClose: () => void;
}

/**
 * A relation's own settings, edited where a property is edited, as in Notion: which table it
 * relates to, which is said and never changed; how many rows a cell holds; and whether the other
 * table shows the relation too. Each change is one gesture, made as it is chosen; the panel shows
 * what the model has, so it shows what was accepted. A change that is refused is said here.
 */
export function RelationEditor({ column, position, ask, onClose }: RelationEditorProps) {
  const panelRef = useRef<HTMLDivElement>(null);
  const [refusal, setRefusal] = useState("");
  const [otherName, setOtherName] = useState("");
  // The other table shows the relation as its backlink column, or, in a file made before links, as a counterpart property.
  const otherSide = column.settings[BACKLINK_NAME] ?? column.settings[OTHER_SIDE] ?? "";
  const twoWay = otherSide !== "";
  // A relation to a diagram is not made two-way; one that is already, from a file written by hand, can still be made one-way.
  const oneWayOnly = column.settings[CREATE_KINDS] !== undefined && !twoWay;

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

  const make = (gesture: TableGesture) => void ask(gesture).then(setRefusal);

  // Drawn at the document's top, as a menu is: inside its header it is painted in the header's
  // layer, under the rows that scroll past and the frozen cells beside it.
  return createPortal(
    <div ref={panelRef} className="table-options-editor table-relation-editor" role="dialog" aria-label={`Relation ${column.name}`} style={{ left: Math.max(PANEL_MARGIN, Math.min(position.x, window.innerWidth - PANEL_WIDTH - PANEL_MARGIN)), top: position.y }}>
      <p className="table-relation-target">
        <span>Related to</span>
        <strong>{targetLabel(column.settings[TARGET] ?? "")}</strong>
      </p>
      <label>
        <span>Limit</span>
        <select aria-label="Limit" value={column.settings[LIMIT] === "one" ? "one" : "none"} onChange={(event) => make({ kind: "setRelationLimit", columnId: column.id, settings: { limit: event.target.value } })}>
          <option value="none">No limit</option>
          <option value="one">1 row</option>
        </select>
      </label>
      {oneWayOnly ? (
        <p className="table-relation-one-way">{ONE_WAY_ONLY}</p>
      ) : (
        <>
          <label>
            <input
              type="checkbox"
              aria-label="Two-way relation"
              checked={twoWay}
              onChange={(event) => make({ kind: "setRelationOtherSide", columnId: column.id, settings: event.target.checked ? { twoWay: "true", counterpart: otherName.trim() } : { twoWay: "false" } })}
            />
            <span>Two-way relation</span>
          </label>
          {twoWay ? (
            <p className="table-relation-other">
              Shown in the other table as <strong>{otherSide}</strong>.
            </p>
          ) : (
            <input type="text" aria-label="Name in the other table" placeholder="Name in the other table" value={otherName} onChange={(event) => setOtherName(event.target.value)} onKeyDown={(event) => event.stopPropagation()} />
          )}
        </>
      )}
      {refusal !== "" && (
        <p className="table-relation-refusal" role="alert">
          {refusal}
        </p>
      )}
    </div>,
    document.body,
  );
}
