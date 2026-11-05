import { useRef, useState } from "react";
import { type PointerPressWiring, usePointerGesture } from "@client/canvas/gesture/usePointerGesture";
import type { HierarchySurfaceNode } from "../api/hierarchySurfaceModel";
import { ancestorsOf, type HierarchyIndex } from "../layout";

/** Where a dropped node goes: into a level (empty for the top), after a sibling of its role there (empty for none). */
export interface EntryDrop {
  into: string;
  after: string;
}

/**
 * What the page shows while a node is dragged: the box under the pointer it would go into, after or before, and the
 * node and the place it would be dropped at, where the page draws it as it would stand.
 */
export interface DropHint {
  nodeId: string;
  kind: "into" | "after" | "before";
  dragged: HierarchySurfaceNode;
  drop: EntryDrop;
}

/** The class of the place the page draws a dragged node at: the pointer over it keeps the drop it shows. */
export const DROP_SLOT = "hs-drop-slot";

/**
 * Where the place a drop shows stands among `shown`, the children of its level as the page draws them, entries before
 * levels; -1 where it stands nowhere among them. A move after a sibling goes right after it; without one, as the move
 * of the definition says, to the front of its kind within its own level, and after the last of its kind into another.
 */
export function slotIndex(shown: readonly HierarchySurfaceNode[], hint: DropHint): number {
  const { dragged, drop } = hint;
  if (drop.after !== "") {
    const at = shown.findIndex((node) => node.id === drop.after);
    return at < 0 ? -1 : at + 1;
  }
  const entries = shown.filter((node) => node.role !== "level").length;
  if (dragged.parentId === drop.into) {
    return dragged.role === "level" ? entries : 0;
  }
  return dragged.role === "level" ? shown.length : entries;
}

/**
 * Where `dragged` goes when dropped on `over`'s box, in its lower half or its upper one; null where it cannot go - on
 * itself, into what it holds, or an entry onto the top. A level dropped on a level goes after it from the lower half and
 * into it from the upper one; on an entry, into that entry's level. An entry dropped on an entry goes after it from the
 * lower half and before it from the upper one; on a level, into it, where a new entry would go.
 */
export function dropOf(index: HierarchyIndex, dragged: HierarchySurfaceNode, over: HierarchySurfaceNode, lowerHalf: boolean): EntryDrop | null {
  if (over.id === dragged.id || ancestorsOf(index, over.id).some((node) => node.id === dragged.id)) {
    return null;
  }
  if (dragged.role === "level") {
    if (over.role === "entry") {
      return { into: over.parentId, after: "" };
    }
    return lowerHalf ? { into: over.parentId, after: over.id } : { into: over.id, after: "" };
  }
  if (over.role === "level") {
    return { into: over.id, after: "" };
  }
  if (over.parentId === "") {
    return null;
  }
  if (lowerHalf) {
    return { into: over.parentId, after: over.id };
  }
  const siblings = (index.children.get(over.parentId) ?? []).filter((node) => node.role === "entry" && node.id !== dragged.id);
  const before = siblings[siblings.findIndex((node) => node.id === over.id) - 1];
  return { into: over.parentId, after: before?.id ?? "" };
}

export interface DragEntry {
  /** The wiring of a node's grip: pressing it and moving starts the drag. */
  grip: (node: HierarchySurfaceNode) => PointerPressWiring;
  /** Where the node being dragged would go now, or null. */
  hint: DropHint | null;
}

/**
 * Dragging a node, with everything in it, to another place (FR-013): from its grip, over the boxes of the page, and
 * dropped as one move. The box under the pointer is found by the `data-node-id` every box carries.
 */
export function useDragEntry(index: HierarchyIndex, onDrop: (dragged: HierarchySurfaceNode, drop: EntryDrop) => void): DragEntry {
  const [hint, setHint] = useState<DropHint | null>(null);
  const hintRef = useRef(hint);
  hintRef.current = hint;
  // Where the pointer is: the gesture reports how far it moved, the page needs what it is over.
  const pointer = useRef({ x: 0, y: 0 });

  const target = (dragged: HierarchySurfaceNode): { over: HierarchySurfaceNode; drop: EntryDrop | null } | "slot" | null => {
    const under = document.elementFromPoint?.(pointer.current.x, pointer.current.y);
    // Over the place the drop is shown at, the drop stays: that place moved the boxes away from under the pointer.
    if (under?.closest(`.${DROP_SLOT}`) !== null && under?.closest(`.${DROP_SLOT}`) !== undefined) {
      return "slot";
    }
    const element = under?.closest<HTMLElement>("[data-node-id]");
    const over = element === null || element === undefined ? undefined : index.byId.get(element.dataset.nodeId ?? "");
    if (element === null || element === undefined || over === undefined) {
      return null;
    }
    const box = element.getBoundingClientRect();
    const lowerHalf = pointer.current.y >= box.top + box.height / 2;
    return { over, drop: dropOf(index, dragged, over, lowerHalf) };
  };

  const gesture = usePointerGesture<HierarchySurfaceNode>({
    onPress: () => undefined,
    onDragMove: (dragged) => {
      const found = target(dragged);
      if (found === "slot") {
        return;
      }
      setHint(
        found === null || found.drop === null
          ? null
          : {
              nodeId: found.over.id,
              kind: found.drop.into === found.over.id ? "into" : found.drop.after === found.over.id ? "after" : "before",
              dragged,
              drop: found.drop,
            },
      );
    },
    onDragEnd: (dragged) => {
      const shown = hintRef.current;
      setHint(null);
      const found = target(dragged);
      const drop = found === "slot" ? shown?.drop : found?.drop;
      if (drop !== null && drop !== undefined) {
        onDrop(dragged, drop);
      }
    },
    onDragAbandon: () => setHint(null),
  });

  const grip = (node: HierarchySurfaceNode): PointerPressWiring => {
    const wiring = gesture.press(node);
    const track = (event: React.PointerEvent) => {
      pointer.current = { x: event.clientX, y: event.clientY };
    };
    return {
      ...wiring,
      onPointerDown: (event) => {
        track(event);
        wiring.onPointerDown(event);
      },
      onPointerMove: (event) => {
        track(event);
        wiring.onPointerMove(event);
      },
      onPointerUp: (event) => {
        track(event);
        wiring.onPointerUp(event);
      },
    };
  };

  return { grip, hint };
}
