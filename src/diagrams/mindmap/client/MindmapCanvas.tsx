import { useMemo, useState } from "react";

import { elementSelectionOf, elementSourceOf, selectedElementIdOf } from "@client/canvas/selection";
import { isTextTarget, structuralShortcutFor } from "@client/canvas/interaction";
import { DiagramCanvas } from "@client/canvas/library/DiagramCanvas";
import { assertValidDiagramDefinition } from "@client/canvas/library/definition/validateDiagramDefinition";
import type { DiagramDefinition, ShapeBounds, ShapePoint } from "@client/canvas/library/definition/diagramDefinition";
import type { DiagramEventHandlers, DiagramSelection } from "@client/canvas/library/api/diagramEvents";
import type { DiagramModel, DiagramModelElement } from "@client/canvas/library/api/diagramModel";
import { innermostKey, useContextConnection, useContextPrompt, useContextSelection } from "@client/shell/context/ContextConnectionProvider";
import { inlineLabelElementIdOf } from "@client/shell/context/inlineLabelPrompt";
import { type ContextShortcut } from "@client/generated/context-contract_pb";
import { ContextSelectionAction } from "@client/generated/context_pb";
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
          { className: "mindmap-node-focused", on: "element", when: { path: "state.selected", is: "true" } },
          { className: "mindmap-node-dragging", on: "element", when: { path: "state.dragging", is: "true" } },
        ],
        labels: [
          {
            text: { path: "payload.text" },
            editable: true,
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
            className: "mindmap-node-indicators",
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
        endpoints: {
          source: { elementTypes: ["node"] },
          target: { elementTypes: ["node"], anchors: "edge" },
          allowSelf: false,
        },
      },
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
  const { select, executeAction, executeShortcut } = useContextConnection();
  const { selection, actions } = useContextSelection();
  const toolboxItems = useToolboxItems(projectId, path);
  const [viewport, setViewport] = useState<ShapeBounds | null>(null);

  const selectionKey = innermostKey(selection);
  const selectedNodeId = useMemo(() => selectedElementIdOf(selection), [selection]);

  const { prompt, onPropose: onProposeLabel, onSubmit: onSubmitLabel, onCancel: onCancelLabel } = useContextPrompt();
  const editingId = inlineLabelElementIdOf(prompt);

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

  /** The backend's push is the selection; the canvas renders it and never decides. */
  const librarySelection = useMemo<DiagramSelection>(
    () => (selectedNodeId && model.elements.has(selectedNodeId) ? [{ kind: "element", id: selectedNodeId }] : []),
    [selectedNodeId, model.elements],
  );

  const runShortcut = (shortcut: ContextShortcut, sourceId: string) => {
    void executeShortcut(shortcut, elementSourceOf(sourceId));
  };

  const events: DiagramEventHandlers = {
    onSelectionChanged: ({ selection: next }) => {
      if (next.length === 0 || next[0].kind === "connection") {
        // A press on empty canvas deselects; a press on a branch line means the same - the
        // old canvas gave its edges no hit surface, so a click there fell through to the
        // background (recorded unification: the library's fat hit twin catches it first).
        select(null);
        return;
      }
      select(elementSelectionOf(entryId, path, next[0].id));
    },
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
        void executeAction(elementType, elementSourceOf(target.id));
      }
    },
    // Delete travels as the backend shortcut it always was, raised by the library's key path.
    onElementDeleted: ({ elementId }) =>
      runShortcut({ key: "Delete", ctrl: false, shift: false, alt: false, meta: false } as ContextShortcut, elementId),
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

  /**
   * The structural keys, forwarded as data against the selected node - the backend holds the
   * key-to-action table. Tab is the XMind convention for "add child"; the backend's child
   * action carries Insert, so the alias resolves here - a key-to-key mapping, never a
   * key-to-action one. Delete is absent: the library raises it as its deletion event above.
   */
  const onKeyDown = (event: React.KeyboardEvent) => {
    if (!selectedNodeId || isTextTarget(event.target)) {
      return;
    }
    const shortcut = structuralShortcutFor(event, ["Insert", "Enter", "F2", " ", "Tab"], { Tab: "Insert" });
    if (!shortcut) {
      return;
    }
    event.preventDefault();
    runShortcut(shortcut, selectedNodeId);
  };

  if (failed) {
    return (
      <div className="mindmap-canvas" data-testid="mindmap-canvas">
        <div className="mindmap-canvas-unavailable" role="alert">
          This diagram is no longer available at {path.join("/")}.
        </div>
      </div>
    );
  }

  return (
    <div className="mindmap-canvas" data-testid="mindmap-canvas" onKeyDown={onKeyDown}>
      {loading ? (
        <div className="mindmap-canvas-loading" role="status">
          Loading…
        </div>
      ) : (
        <DiagramCanvas
          definition={definition}
          model={diagramModel}
          events={events}
          selection={librarySelection}
          toolboxItems={toolboxItems}
          context={{
            selectionKey: selectionKey ?? undefined,
            actions,
            selectForMenu: (id) => select(elementSelectionOf(entryId, path, id, ContextSelectionAction.CONTEXT_MENU)),
            executeAction: (actionId) => void executeAction(actionId, selectedNodeId ? elementSourceOf(selectedNodeId) : undefined),
          }}
          editing={{ editingId, onPropose: onProposeLabel, onSubmit: onSubmitLabel, onCancel: onCancelLabel }}
          className="mindmap-canvas-host"
          ariaLabel="Mind map"
        />
      )}
    </div>
  );
}

// Kept exported for the panel and tests; the model type is re-exported so consumers need one import.
export type { MindmapModel };
