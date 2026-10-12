import { useEffect, useRef, useState } from "react";
import { createPortal } from "react-dom";
import { choicesOf, type TableGesture } from "../api/tableEvents";
import { kindOf, type TableDefinition } from "../definition/tableDefinition";
import { addableKinds } from "./columnActions";

/** How near the window's left edge the panel may stand, in pixels. */
const PANEL_MARGIN = 8;

/** The room the panel is kept within the window's right edge, in pixels: about its own width. */
const PANEL_WIDTH = 260;

export interface NewColumnPanelProps {
  definition: TableDefinition;
  /** Where the panel stands, in the window. */
  position: { x: number; y: number };
  /** The column the new one goes beside, and on which side of it; left out, it goes after the last. */
  beside?: { columnId: string; side: "left" | "right" };
  raise: (gesture: TableGesture) => void;
  /** Makes a gesture and answers with what it was answered with: for a relation, whose table is asked for before it is made. Left out, a relation cannot be asked for here. */
  ask?: (gesture: TableGesture) => Promise<string>;
  onClose: () => void;
}

/** A relation that is being asked for: the tables there are, the one chosen, and what was said of the relation so far. */
interface RelationAsked {
  targets: { id: string; label: string }[];
  target: { id: string; label: string } | null;
  limit: string;
  twoWay: boolean;
  otherName: string;
}

/**
 * A new property, asked for as Notion asks: its name first, then its type. Choosing a type makes
 * the property under the name that was typed; with no name typed it takes its type's, and the
 * table asks for one in its header once it is there. Leaving the panel makes nothing.
 *
 * <b>A relation is a type among the others</b>, and asks what Notion asks, in its order: the table
 * it relates to, chosen from the tables the backend says there are; how many rows a cell holds;
 * and whether the other table shows the relation too, and under which name. Nothing is made
 * before *Add relation* is pressed, and a relation that is refused is refused here.
 */
export function NewColumnPanel({ definition, position, beside, raise, ask, onClose }: NewColumnPanelProps) {
  const panelRef = useRef<HTMLDivElement>(null);
  const nameRef = useRef<HTMLInputElement>(null);
  const [name, setName] = useState("");
  const [relation, setRelation] = useState<RelationAsked | null>(null);
  const [refusal, setRefusal] = useState("");

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

  /** The relation as a gesture: with no table chosen yet it is a question, answered with the tables there are. */
  const relationGesture = (asked: RelationAsked | null): TableGesture => {
    const typed = name.trim();
    return {
      kind: "addRelation",
      ...(beside === undefined ? {} : { targetId: beside.columnId }),
      ...(typed === "" ? {} : { values: [typed] }),
      settings: {
        ...(beside === undefined ? {} : { side: beside.side }),
        ...(asked?.target == null ? {} : { target: asked.target.id, limit: asked.limit, twoWay: asked.twoWay ? "true" : "false", ...(asked.twoWay ? { counterpart: asked.otherName.trim() } : {}) }),
      },
    };
  };

  const add = (kind: string) => {
    if (ask !== undefined && kindOf(definition, kind).needsTarget === true) {
      // Which tables are there to relate to? The answer is the list; anything else is why there is none.
      void ask(relationGesture(null)).then((answer) => {
        const targets = choicesOf(answer);
        if (targets.length === 0) {
          setRefusal(answer === "" ? "There is no table to relate to." : answer);
        } else {
          setRefusal("");
          setRelation({ targets, target: null, limit: "none", twoWay: false, otherName: "" });
        }
      });
      return;
    }
    const typed = name.trim();
    raise({
      kind: "addColumn",
      ...(beside === undefined ? {} : { targetId: beside.columnId }),
      ...(typed === "" ? {} : { values: [typed] }),
      settings: beside === undefined ? { type: kind } : { side: beside.side, type: kind },
    });
    onClose();
  };

  // Drawn at the document's top, as a menu is: inside its header it is painted in the header's
  // layer, under the rows that scroll past and the frozen cells beside it.
  return createPortal(
    <div ref={panelRef} className="table-options-editor table-new-column" role="dialog" aria-label="New property" style={{ left: Math.max(PANEL_MARGIN, Math.min(position.x, window.innerWidth - PANEL_WIDTH - PANEL_MARGIN)), top: position.y }}>
      <input ref={nameRef} type="text" aria-label="Property name" placeholder="Property name" value={name} onChange={(event) => setName(event.target.value)} onKeyDown={(event) => event.stopPropagation()} />
      {relation === null ? (
        <ul className="table-new-column-kinds" role="menu" aria-label="Type">
          {addableKinds(definition)
            .filter((kind) => ask !== undefined || kindOf(definition, kind.kind).needsTarget !== true)
            .map((kind) => (
              <li key={kind.kind} role="none">
                <button type="button" role="menuitem" className="table-new-column-kind" onClick={() => add(kind.kind)}>
                  <span className={`mdi ${kind.icon}`} aria-hidden="true" />
                  <span>{kind.label}</span>
                </button>
              </li>
            ))}
        </ul>
      ) : relation.target === null ? (
        <ul className="table-new-column-kinds" role="menu" aria-label="Relate to">
          {relation.targets.map((target) => (
            <li key={target.id} role="none">
              <button type="button" role="menuitem" className="table-new-column-kind" onClick={() => setRelation({ ...relation, target })}>
                <span className="mdi mdi-table" aria-hidden="true" />
                <span>{target.label}</span>
              </button>
            </li>
          ))}
        </ul>
      ) : (
        <div className="table-new-relation" role="group" aria-label="Relation">
          <p className="table-relation-target">
            <span>Related to</span>
            <strong>{relation.target.label}</strong>
          </p>
          <label>
            <span>Limit</span>
            <select aria-label="Limit" value={relation.limit} onChange={(event) => setRelation({ ...relation, limit: event.target.value })}>
              <option value="none">No limit</option>
              <option value="one">1 row</option>
            </select>
          </label>
          <label>
            <input type="checkbox" aria-label="Two-way relation" checked={relation.twoWay} onChange={(event) => setRelation({ ...relation, twoWay: event.target.checked })} />
            <span>Two-way relation</span>
          </label>
          {relation.twoWay && (
            <input
              type="text"
              aria-label="Name in the other table"
              placeholder="Name in the other table"
              value={relation.otherName}
              onChange={(event) => setRelation({ ...relation, otherName: event.target.value })}
              onKeyDown={(event) => event.stopPropagation()}
            />
          )}
          <button
            type="button"
            className="table-new-relation-add"
            onClick={() =>
              void ask?.(relationGesture(relation)).then((answer) => {
                if (answer === "") {
                  onClose();
                } else {
                  setRefusal(answer);
                }
              })
            }
          >
            Add relation
          </button>
        </div>
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
