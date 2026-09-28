import { useMemo, useState } from "react";

import { elementSourceOf } from "@client/canvas/selection";
import { DiagramCanvas } from "@client/canvas/library/DiagramCanvas";
import { assertValidDiagramDefinition } from "@client/canvas/library/definition/validateDiagramDefinition";
import type { DiagramDefinition, ShapeBounds, ShapePoint } from "@client/canvas/library/definition/diagramDefinition";
import type { DiagramEventHandlers } from "@client/canvas/library/api/diagramEvents";
import type { DiagramModel, DiagramModelElement } from "@client/canvas/library/api/diagramModel";
import { useContextConnection } from "@client/shell/context/ContextConnectionProvider";
import { useToolboxItems } from "@client/shell/panels/useToolboxItems";
import { useViewReport } from "@client/diagrams/useViewReport";
import { isFolded, type MindmapElement, type MindmapModel } from "./mindmapModel";
import { useMindmapStream } from "./useMindmapStream";

/** The diagram file this canvas shows: its project id, and the project-relative path of its `.adp`. */
export interface MindmapCanvasProps {
  projectId: Uint8Array;
  entryId: Uint8Array;
  path: readonly string[];
}

const NODE_HALF_WIDTH = 60;
const NODE_HALF_HEIGHT = 16;

/** An element as the library carries it here: the model element plus the node it draws. */
type NodeElement = DiagramModelElement & { node: MindmapElement; folded: boolean };

/** A node's drawn size: the box the backend measured, or the nominal guess before it has. */
function sizeOf(element: MindmapElement): { width: number; height: number } {
  return {
    width: element.payload.width > 0 ? element.payload.width : NODE_HALF_WIDTH * 2,
    height: element.payload.height > 0 ? element.payload.height : NODE_HALF_HEIGHT * 2,
  };
}


/**
 * Where a drop at `point` would land: the topmost node under it that the backend could accept.
 * The dragged node itself, or a node inside its own branch, is no outcome at all - the backend
 * would refuse the move - so it is not offered as one either.
 */
function dropTargetAt(model: MindmapModel, draggedId: string, point: ShapePoint): MindmapElement | undefined {
  const candidates = [...model.elements.values()];
  for (let i = candidates.length - 1; i >= 0; i--) {
    const candidate = candidates[i];
    const { width, height } = sizeOf(candidate);
    if (Math.abs(point.x - candidate.x) > width / 2 || Math.abs(point.y - candidate.y) > height / 2) {
      continue;
    }

    const valid = candidate.id !== draggedId && !isInSubtree(model, candidate.id, draggedId);
    return valid ? candidate : undefined;
  }
  return undefined;
}


/** Whether `candidateId` sits inside the branch rooted at `rootId` - itself included. */
function isInSubtree(model: MindmapModel, candidateId: string, rootId: string): boolean {
  let cursor: string | undefined = candidateId;
  const seen = new Set<string>(); // a defensive stop; the model never actually cycles
  while (cursor !== undefined && !seen.has(cursor)) {
    if (cursor === rootId) {
      return true;
    }
    seen.add(cursor);
    cursor = model.elements.get(cursor)?.payload.parentId || undefined;
  }
  return false;
}

/**
 * What a mindmap allows, stated once: nodes that drag onto one another to re-parent, edit
 * their text in place and speak the structural keys; branches drawn between facing sides; the
 * backend's own layout passed through untouched.
 *
 * **Manual layout, deliberately, and the tree mode stays unexercised here** - a recorded
 * deviation from this task's original text. The mindmap backend computes the arrangement and
 * streams positions, and the library's own design (diagram-library, Requirement 8.3) says
 * exactly what to do with that: where the backend places elements today, the library uses
 * manual/external and leaves it untouched. Wiring the client-side tree algorithm against a
 * backend-arranged diagram would run a second layout to fight the wire's.
 */
function definitionOf(): DiagramDefinition {
  return assertValidDiagramDefinition({
    elementTypes: [
      {
        id: "node",
        // The shared centered box the renderer wrapped. What it added - three classes, a line
        // of indicator glyphs, and the drag preview - is a declaration now.
        shape: "centered-box",
        classNames: [
          { className: "mindmap-node", on: "element" },
          { className: "mindmap-node-dragging", on: "element", when: { path: "state.dragging", is: "true" } },
          // The box's own class, so the stylesheet reaches the box and nothing else the library
          // draws in the node's group. `.mindmap-node rect` reached the library's selection ring
          // too and painted an opaque box over the label (59f1ed3a; client-centralization
          // Requirement 3.2).
          { className: "mindmap-node-box" },
        ],
        labels: [
          {
            text: { path: "payload.text" },
            editable: true,
            className: "mindmap-node-label",
          },
          {
            // The corner glyphs: notes, a link, a folded branch. Joined by the fold rather than
            // drawn one at a time, because the notation shows them as one line.
            text: { path: "payload.indicators" },
            when: { path: "payload.indicators", is: "non-empty" },
            anchorTo: "top",
            offset: { x: 0, y: 12 },
            align: "end",
            insetX: 4,
            className: "mindmap-node-label mindmap-node-indicators",
          },
        ],
        // A branch leaves a node SIDEWAYS at mid-height, whatever the angle - the difference
        // between a tree and a graph, and what `facingSidePoint` did in the renderer.
        anchors: { kind: "edge", edgeSides: "horizontal" },
        sizing: "model",
      },
    ],
    relationTypes: [
      {
        id: "branch",
        route: "cubic-bezier",
        style: { endMarker: "none" },
        lineClassName: "mindmap-edge",
        // A branch is a node's link to its parent, not a thing a reader picks: a press on it is
        // a press on the background, as it has always been here. A decision about the notation,
        // recorded in this module's readme - making branches selectable is a separate question
        // for the user, not a side effect (centralized-selection Requirement 2.4).
        selectable: false,
        endpoints: {
          source: { elementTypes: ["node"] },
          target: { elementTypes: ["node"], anchors: "edge" },
          allowSelf: false,
        },
      },
    ],
    // WHAT A MIND MAP OFFERS, AND WHAT INVOKES IT. Five keys were a hand-written list in this
    // canvas - the longest of the four spellings the tree carried - and the delete was a
    // keystroke it built to describe a gesture the library had already handed it.
    //
    // TAB AND INSERT ARE ONE ACTION WITH TWO KEYS, which is what an alias always meant: Tab is
    // the XMind convention for "add child" and the backend knows that action as Insert. Two
    // invocations of one declared id says it directly, where the alias said it sideways.
    actions: [
      {
        id: "add-child",
        // Insert, not Tab, even when Tab fired it: the backend knows this action as Insert, and
        // sending the key that was pressed would put an unknown keystroke on the wire (5.3).
        backendKey: "Insert",
        invokedBy: [
          { kind: "shortcut", key: "Insert" },
          { kind: "shortcut", key: "Tab" },
        ],
        appliesTo: [{ kind: "element" }],
      },
      { id: "add-sibling", backendKey: "Enter", invokedBy: [{ kind: "shortcut", key: "Enter" }], appliesTo: [{ kind: "element" }] },
      { id: "rename", backendKey: "F2", invokedBy: [{ kind: "shortcut", key: "F2" }], appliesTo: [{ kind: "element" }] },
      { id: "fold", backendKey: " ", invokedBy: [{ kind: "shortcut", key: " " }], appliesTo: [{ kind: "element" }] },
      { id: "delete", backendKey: "Delete", invokedBy: [{ kind: "gesture", gesture: "delete" }], appliesTo: [{ kind: "element" }] },
    ],
    layout: { modes: ["manual"] },
    dragging: "enabled",
    // THE DRAG PREVIEW, declared. The library computes the candidate parent per frame - it is
    // the only thing that knows where the drag is - from the parent link this names; what a
    // drop MEANS is still answered in `onElementMoved` below.
    dropTarget: {
      parentPath: "payload.parentId",
      group: { className: "mindmap-drag-preview", data: { testid: "mindmap-drag-preview" } },
      ring: { className: "mindmap-node-drop-target-ring", data: { testid: "mindmap-drop-ring" } },
      preview: { className: "mindmap-edge mindmap-edge-preview" },
    },
  });
}

/**
 * Renders a mindmap through the diagram library. It holds no document state of its own beyond
 * the stream's model; every edit is an `ExecuteAction`, selection follows what the backend
 * pushes, and a node drag is what it always was - a re-parenting proposal the backend is free
 * to refuse, never a position write.
 */
export function MindmapCanvas({ projectId, entryId, path }: MindmapCanvasProps) {
  const { model, loading, failed, moveElement, reportView } = useMindmapStream(projectId, path);
  const { executeAction } = useContextConnection();
  const toolboxItems = useToolboxItems(projectId, path);
  const [viewport, setViewport] = useState<ShapeBounds | null>(null);

  const definition = useMemo(() => definitionOf(), [model]);

  const diagramModel = useMemo<DiagramModel>(() => {
    const elements = [...model.elements.values()].map((element): NodeElement => {
      const folded = isFolded(model, element.id);
      return {
        id: element.id,
        type: "node",
        x: element.x,
        y: element.y,
        ...sizeOf(element),
        label: element.payload.text,
        // What the declaration reads: the text, the corner glyphs, and the parent link the
        // library follows to know which nodes a drag may not be dropped on.
        payload: {
          text: element.payload.text,
          parentId: element.payload.parentId,
          indicators: [element.payload.notes ? "•" : "", element.payload.link ? "↗" : "", folded && element.payload.hasChildren ? "⊕" : ""]
            .join(" ")
            .trim(),
        },
        node: element,
        folded,
      };
    });
    // The branches, from the payload's parent id - each node knows whose child it is, and
    // nothing more is needed to see the tree.
    const connections = [...model.elements.values()]
      .filter((element) => element.payload.parentId && model.elements.has(element.payload.parentId))
      .map((element) => ({
        id: `edge-${element.id}`,
        type: "branch",
        sourceId: element.payload.parentId!,
        targetId: element.id,
      }));
    return { elements, connections };
  }, [model]);

  // Every refusal - a drop, a re-parenting, a declared keystroke the library sends - is shown on the
  // one line the library draws around every canvas, by the call that got it (client-centralization
  // Requirement 2), and the next gesture clears it.
  const events: DiagramEventHandlers = {
    // Selection is the library's (centralized-selection); a press on a branch is a background
    // press because the branch type declares `selectable: false`.
    onElementMoved: ({ elementId, position }) => {
      // A drag is a re-parenting proposal, not a position write: the drop lands on whichever
      // node sits under the released point, appended as its last child; over empty canvas the
      // drag simply ends. The backend refuses the moves that make no sense.
      const parent = dropTargetAt(model, elementId, position);
      if (parent !== undefined) {
        void moveElement(elementId, parent.id);
      }
    },
    onElementDropped: ({ elementType, position }) => {
      // A toolbox entry carries the backend's own action id; dropping it on a node executes
      // that action against the node - the same add-child flow, prompt and undo the menu and
      // the Insert key already share. Over empty canvas: no node, no action, exactly as before.
      const target = [...model.elements.values()].reverse().find((element) => {
        const { width, height } = sizeOf(element);
        return Math.abs(position.x - element.x) <= width / 2 && Math.abs(position.y - element.y) <= height / 2;
      });
      if (target !== undefined) {
        void executeAction(elementType, elementSourceOf(target.id, entryId));
      }
    },
    onViewChanged: ({ viewport: next }) => setViewport(next),
  };

  useViewReport({
    view: { x: viewport?.x ?? 0, y: viewport?.y ?? 0, w: viewport?.width ?? 0, h: viewport?.height ?? 0 },
    report: reportView,
    convert: () => ({
      minX: viewport?.x ?? 0,
      minY: viewport?.y ?? 0,
      maxX: (viewport?.x ?? 0) + (viewport?.width ?? 0),
      maxY: (viewport?.y ?? 0) + (viewport?.height ?? 0),
    }),
    ready: !loading && !failed && viewport !== null,
  });

  // Opening, reconnecting and unavailable are the library's to say, in the frame around this canvas.
  return (
    <div className="mindmap-canvas" data-testid="mindmap-canvas">
      <DiagramCanvas
        definition={definition}
        model={diagramModel}
        events={events}
        source={{ entryId, path }}
        toolboxItems={toolboxItems}
        className="mindmap-canvas-host"
        ariaLabel="Mind map"
      />
    </div>
  );
}

// Kept exported for the panel and tests; the model type is re-exported so consumers need one import.
export type { MindmapModel };
