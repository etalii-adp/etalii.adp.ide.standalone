import { useMemo, useState } from "react";

import { elementSourceOf } from "@client/canvas/selection";
import { DiagramCanvas } from "@client/canvas/library/DiagramCanvas";
import { useCanvasRefusal } from "@client/canvas/library/surface/useCanvasRefusal";
import { assertValidDiagramDefinition } from "@client/canvas/library/definition/validateDiagramDefinition";
import type { DiagramDefinition, ShapeBounds } from "@client/canvas/library/definition/diagramDefinition";
import type { DiagramEventHandlers } from "@client/canvas/library/api/diagramEvents";
import type { DiagramModel, DiagramModelConnection, DiagramModelElement } from "@client/canvas/library/api/diagramModel";
import { compileNotation } from "@client/canvas/library/disl/compileNotation";
import { parseDisl } from "@client/canvas/library/disl/disTypes";
import { useContextConnection } from "@client/shell/context/ContextConnectionProvider";
import { useToolboxItems } from "@client/shell/panels/useToolboxItems";
import { useViewReport } from "@client/diagrams/useViewReport";
import { placementId, relationId, withoutPrefix } from "@client/canvas/gestureIds";
import disText from "../definition/causal-loop-diagram.dis?raw";
import { CLD_BINDINGS } from "./cldBindings";
import { LoopPolarityProto, loopCaption, polarityMark, type CausalLoopLink, type CausalLoopModel } from "./causalLoopModel";
import { useCausalLoopStream } from "./useCausalLoopStream";

export interface CausalLoopCanvasProps {
  projectId: Uint8Array;
  entryId: Uint8Array;
  path: readonly string[];
}

/**
 * The backend actions this canvas raises from a gesture of its own: a toolbox drop and a link drawn
 * by right-dragging. Each is the id the provider answers to; the client decides only when to raise it
 * and what source to carry.
 */
const AddVariableActionId = "causal-loop.add-variable";
const ConnectActionId = "causal-loop.connect";

/**
 * What a causal loop diagram allows, stated once - compiled from the bundled DISL specification
 * (`definition/causal-loop-diagram.dis`), with the few things the library cannot read from it in
 * `cldBindings.ts`: pill variables that drag, select and rename in place; links as bowed arcs with
 * their polarity mark, delay strokes and weight; and loop badges that select but never move, drawn
 * at the centre of the variables they run through. `causalLoopCompiledDefinition.test.tsx` holds it
 * to the definition once written here by hand.
 */
export const CLD_DEFINITION: DiagramDefinition = assertValidDiagramDefinition(compileNotation(parseDisl(disText), CLD_BINDINGS));

/** The actions the compiled definition declares, which this module runs against the backend. */
const DECLARED_ACTIONS: ReadonlySet<string> = new Set((CLD_DEFINITION.actions ?? []).map((action) => action.id));

/** The word a loop's computed polarity reads as: its badge's class and its accessible name. */
export function polarityWordOf(computed: LoopPolarityProto): string {
  switch (computed) {
    case LoopPolarityProto.REINFORCING:
      return "reinforcing";
    case LoopPolarityProto.BALANCING:
      return "balancing";
    default:
      return "undecidable";
  }
}

/**
 * The library's elements and connections for a model: what each type's declarations read, and
 * nothing a drawing computes. A loop's position is the library's to compute from its members; the
 * model's own is kept for when a member is not held.
 */
export function diagramModelOf(model: CausalLoopModel): DiagramModel {
  const variables = [...model.variables.values()].map((variable): DiagramModelElement => ({
    id: variable.id,
    type: "variable",
    x: variable.x,
    y: variable.y,
    width: variable.payload.width,
    height: variable.payload.height,
    label: variable.payload.display,
    payload: { display: variable.payload.display },
  }));
  const loops = [...model.loops.values()].map((loop): DiagramModelElement => ({
    id: loop.id,
    type: "loop",
    x: loop.x,
    y: loop.y,
    // The badge and its marker together, roughly: enough box for a press to land on.
    width: 60,
    height: 48,
    payload: {
      caption: loopCaption(loop),
      computed: polarityWordOf(loop.payload.computed),
      disagrees: loop.payload.disagrees,
      members: loop.payload.memberElementIds,
    },
  }));
  const links = [...model.links.values()].flatMap((link): DiagramModelConnection[] =>
    model.variables.has(link.payload.fromElementId) && model.variables.has(link.payload.toElementId)
      ? [{
          id: link.id,
          type: "causallink",
          sourceId: link.payload.fromElementId,
          targetId: link.payload.toElementId,
          payload: {
            flipped: link.payload.flipped,
            delayed: link.payload.delayed,
            polarityMark: polarityMark(link.payload.polarity),
            // Absent rather than zero where none was written: the ladder draws an unweighted link
            // at its normal width, and zero is a weight an author may write.
            ...(link.payload.hasWeight ? { weight: link.payload.weight } : {}),
          },
        }]
      : [], // the far end is not held - off screen, or never declared; a line to nothing is worse than none
  );
  return { elements: [...variables, ...loops], connections: links };
}

/**
 * Draws a causal loop diagram, through the diagram library, in the notation the world uses:
 * variables joined by polarised arrows, delays marked with strokes across the link, and each
 * loop carrying its R or B identifier among the variables it runs through. Every gesture
 * answers with a backend call.
 */
export function CausalLoopCanvas({ projectId, entryId, path }: CausalLoopCanvasProps) {
  const { model, loading, failed, moveElementTo, reportView } = useCausalLoopStream(projectId, path);
  const { executeAction } = useContextConnection();
  const toolboxItems = useToolboxItems(projectId, path);
  // The refusals this canvas decides on the client, before anything is sent; every other refusal
  // is the backend's, and the call that got it reports it to the library's line itself.
  const { refuse } = useCanvasRefusal();
  const [viewport, setViewport] = useState<ShapeBounds | null>(null);

  /** Runs a backend action, threading its source; a refusal reaches the library's line by itself. */
  const runAction = (actionId: string, sourceId?: string) => {
    void executeAction(actionId, sourceId !== undefined ? elementSourceOf(sourceId, entryId) : undefined);
  };

  const diagramModel = useMemo(() => diagramModelOf(model), [model]);

  // Selection is the library's (centralized-selection), and so is the refusal line: every call
  // below, and every menu action the library runs, reports its own refusal there.
  const events: DiagramEventHandlers = {
    // The specification's double-click rename and its Delete gesture, each the backend action of
    // the same id on the element or link it reached.
    onActionInvoked: ({ actionId, targetId }) => {
      if (targetId !== undefined && DECLARED_ACTIONS.has(actionId)) {
        runAction(actionId, targetId);
      }
    },
    onElementMoved: ({ elementId, position }) => {
      if (!model.variables.has(elementId)) {
        return; // a loop badge has no position of its own; the definition already refuses the drag
      }
      // Returned, so a refusal lets the library put the element back where it was (ruled 2026-10-19).
      return moveElementTo(elementId, position.x, position.y);
    },
    onViewChanged: ({ viewport: next }) => setViewport(next),
    // A toolbox drop carries the backend action its item names; the placement carries where it
    // landed. "Add variable" then adds one there with a calculated name and no dialog; a link
    // or loop item drops onto whatever variable it was released on, the same as its menu entry.
    onElementDropped: ({ elementType, position, elementIds }) => {
      if (elementType === AddVariableActionId) {
        runAction(elementType, placementId(position.x, position.y));
        return;
      }
      const variableId = (elementIds ?? []).find((id) => model.variables.has(id));
      if (variableId !== undefined) {
        runAction(elementType, variableId);
      } else {
        refuse("Drop a link or a loop onto a variable.");
      }
    },
    // A link drawn by right-dragging between two variables: the shared gesture layer raises this,
    // and the module states the link (and claims the loops it closes) with no dialog.
    onConnectionDrawn: ({ sourceElementId, targetElementId }) => {
      // Both ends must be variables: an id that is not one, or is the bare prefix, is refused
      // here rather than sent as a truncated or empty end.
      const from = withoutPrefix(sourceElementId, "variable:");
      const to = withoutPrefix(targetElementId, "variable:");
      if (from === null || to === null) {
        refuse("Draw a link between two variables.");
        return;
      }
      runAction(ConnectActionId, relationId(from, to));
    },
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
    <div className="causal-loop-frame canvas-host">
      {!loading && !failed && model.variables.size === 0 ? (
        <div className="causal-loop-message">This causal loop diagram states no variables yet.</div>
      ) : (
        <DiagramCanvas
          definition={CLD_DEFINITION}
          model={diagramModel}
          events={events}
          source={{ entryId, path }}
          toolboxItems={toolboxItems}
          className="causal-loop-canvas-host"
          ariaLabel="Causal loop diagram"
        />
      )}
    </div>
  );
}

export type { CausalLoopLink };
