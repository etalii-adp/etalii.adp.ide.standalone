import { useRef, useState } from "react";
import { type PointerPressWiring, usePointerGesture } from "@client/canvas/gesture/usePointerGesture";
import type { HierarchySurfaceNode } from "../api/hierarchySurfaceModel";
import { ancestorsOf, type HierarchyIndex } from "../layout";

/** Where a dropped node goes: into a level (empty for the top), after a sibling of its role there (empty for none). */
export interface EntryDrop {
  into: string;
  after: string;
}

/** What the page shows under the pointer while a node is dragged: the box it would go into, after or before. */
export interface DropHint {
  nodeId: string;
  kind: "into" | "after" | "before";
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
  // Where the pointer is: the gesture reports how far it moved, the page needs what it is over.
  const pointer = useRef({ x: 0, y: 0 });

  const target = (dragged: HierarchySurfaceNode): { over: HierarchySurfaceNode; drop: EntryDrop | null } | null => {
    const element = document.elementFromPoint?.(pointer.current.x, pointer.current.y)?.closest<HTMLElement>("[data-node-id]");
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
      setHint(
        found === null || found.drop === null
          ? null
          : { nodeId: found.over.id, kind: found.drop.into === found.over.id ? "into" : found.drop.after === found.over.id ? "after" : "before" },
      );
    },
    onDragEnd: (dragged) => {
      setHint(null);
      const found = target(dragged);
      if (found !== null && found.drop !== null) {
        onDrop(dragged, found.drop);
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
