import { useMemo, useState } from "react";

import { elementSourceOf } from "@client/canvas/selection";
import { DiagramCanvas } from "@client/canvas/library/DiagramCanvas";
import { assertValidDiagramDefinition } from "@client/canvas/library/definition/validateDiagramDefinition";
import { compileNotation } from "@client/canvas/library/disl/compileNotation";
import { parseDisl } from "@client/canvas/library/disl/disTypes";
import type { DiagramDefinition, ShapeBounds, ShapePoint } from "@client/canvas/library/definition/diagramDefinition";
import type { DiagramEventHandlers } from "@client/canvas/library/api/diagramEvents";
import type { DiagramModel, DiagramModelElement } from "@client/canvas/library/api/diagramModel";
import { useContextConnection } from "@client/shell/context/ContextConnectionProvider";
import { useToolboxItems } from "@client/shell/panels/useToolboxItems";
import { useViewReport } from "@client/diagrams/useViewReport";
import { isFolded, type MindmapElement, type MindmapModel } from "./mindmapModel";
import { MINDMAP_BINDINGS } from "./mindmapBindings";
import { useMindmapStream } from "./useMindmapStream";
import disText from "../definition/mindmap.dis?raw";

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
 * What a mind map allows, compiled from its bundled DISL specification (`definition/mindmap.dis`) with
 * the module's {@link MINDMAP_BINDINGS}: nodes that drag onto one another to re-parent, edit their text
 * in place and speak the structural keys; branches drawn between facing sides; the backend's own
 * layout passed through untouched. `mindmapCompiledDefinition.test.ts` holds it to the definition this
 * canvas stated by hand before.
 *
 * **Manual layout, deliberately.** The mind map backend computes the arrangement and streams
 * positions, and the library's own design (diagram-library, Requirement 8.3) says where the backend
 * places elements the library uses manual/external and leaves it untouched; the specification's
 * `mindmapLayout` is a host layout, which compiles to exactly that.
 */
export const MINDMAP_DEFINITION: DiagramDefinition = assertValidDiagramDefinition(compileNotation(parseDisl(disText), MINDMAP_BINDINGS));

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

  const definition = MINDMAP_DEFINITION;

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
