import { useMemo, useState } from "react";

import { elementSourceOf } from "@client/canvas/selection";
import { DiagramCanvas } from "@client/canvas/library/DiagramCanvas";
import type { DiagramDefinition, ShapeBounds } from "@client/canvas/library/definition/diagramDefinition";
import type { DiagramEventHandlers } from "@client/canvas/library/api/diagramEvents";
import type { DiagramModel, DiagramModelElement } from "@client/canvas/library/api/diagramModel";
import { useContextConnection } from "@client/shell/context/ContextConnectionProvider";
import { useToolboxItems } from "@client/shell/panels/useToolboxItems";
import type { ToolContentProps } from "@client/shell/panels/toolPanelRegistration";
import { useViewReport } from "@client/diagrams/useViewReport";
import { useDatabricksStream } from "./useDatabricksStream";
import { SIMULATED_MARKER, useSimulatedRun } from "./useSimulatedRun";
import type { DatabricksEdge, DatabricksFrame, DatabricksNode } from "./databricksModel";
import { placementId, relationId } from "@client/canvas/gestureIds";

/** A node's drawn size, in the module's own canvas units: the definitions' fixed size, which the backend layouts space by. */
export const NODE_WIDTH = 200;
export const NODE_HEIGHT = 56;

/** A target frame's drawn size, the bundle definition's fixed size for a target. */
const FRAME_WIDTH = 220;
const FRAME_HEIGHT = 120;

/** The definition's type a target frame draws as. */
const TARGET_TYPE = "target";

/** The definition's relation each kind of edge the stream sends draws as. */
const RELATION_OF: Readonly<Record<DatabricksEdge["kind"], string>> = { depends: "dependency", override: "override", flow: "flow" };

export interface DatabricksCanvasConfig {
  /** The aria label naming which of the family's readings this canvas draws. */
  ariaLabel: string;
  /** The reading's canvas definition, compiled from its bundled definition (`databricksBindings.ts`). */
  definition: DiagramDefinition;
  /** The definition's node type a box of this reading draws as. */
  typeOf: (node: DatabricksNode) => string;
  /**
   * Intercepts an action id before it reaches the backend; answering true means the canvas
   * handled it locally. The simulation engine's seam (Requirement 8.6): simulated ids play
   * client-side, everything else still travels.
   */
  interceptAction?: (actionId: string) => boolean;
}

/**
 * What a box's labels read, under the names its definition gives them: an attribute's own name, or the
 * path `databricksBindings.ts` names for a computed label. The backend sends the name and the badge strip
 * already worked out, so each type's attributes are read from those.
 */
function payloadOf(type: string, node: DatabricksNode): Record<string, unknown> {
  const badgeLine = node.badges.join(" · ");
  switch (type) {
    case "resource":
      return { key: node.label, resourceKind: node.kind };
    case "unknown":
      return { key: node.label, path: node.kind };
    case "library":
      return { path: node.label, libraryKind: node.badges[0] ?? "" };
    case "notifications":
      return { recipients: node.label };
    default:
      return { key: node.label, name: node.label, badgeLine };
  }
}

function overrides(count: number): string {
  return count === 0 ? "" : count === 1 ? "1 override" : `${count} overrides`;
}

/** A target's `mode · default · n overrides`, with the parts that do not apply left out. */
function summaryOf(frame: DatabricksFrame): string {
  return [frame.mode, frame.isDefault ? "default" : "", overrides(frame.overrideCount)].filter((part) => part !== "").join(" · ");
}

/**
 * The family's shared canvas: boxes, frames and directed edges at the positions the backend
 * computed and the `.adp` authored, drawn through the central canvas library from the reading's
 * compiled definition. The three diagram types differ in what arrives on the stream and in their
 * definitions - everything else is one implementation (Requirement 1.2).
 *
 * A drag never writes the body file: `moveElementTo` lands in the registration's `layout:`
 * block as one undoable command (Requirement 7).
 */
export function DatabricksCanvas({
  projectId,
  entryId,
  path,
  ariaLabel,
  definition,
  typeOf,
  interceptAction,
}: ToolContentProps & DatabricksCanvasConfig) {
  const { model, loading, failed, moveElementTo, reportView } = useDatabricksStream(projectId, path);
  const simulation = useSimulatedRun(model);
  const { executeAction } = useContextConnection();
  const toolboxItems = useToolboxItems(projectId, path);
  const [viewport, setViewport] = useState<ShapeBounds | null>(null);

  const diagramModel = useMemo<DiagramModel>(() => {
    // Frames first, as they always were; the definition puts them beneath the connections too.
    const frames = [...model.frames.values()].map((frame): DiagramModelElement => ({
      id: frame.id,
      type: TARGET_TYPE,
      x: frame.x + FRAME_WIDTH / 2,
      y: frame.y + FRAME_HEIGHT / 2,
      width: FRAME_WIDTH,
      height: FRAME_HEIGHT,
      label: frame.label,
      payload: { name: frame.label, summary: summaryOf(frame) },
    }));
    const nodes = [...model.nodes.values()].map((node): DiagramModelElement => {
      const type = typeOf(node);
      const simulated = simulation.states.get(node.id);
      return {
        id: node.id,
        type,
        x: node.x + NODE_WIDTH / 2,
        y: node.y + NODE_HEIGHT / 2,
        width: NODE_WIDTH,
        height: NODE_HEIGHT,
        label: node.label,
        payload: {
          ...payloadOf(type, node),
          kind: node.kind,
          unresolved: node.unresolved,
          ...(simulated !== undefined ? { simulated } : {}),
        },
      };
    });
    const connections = [...model.edges.values()].map((edge) => ({
      id: edge.id,
      type: RELATION_OF[edge.kind],
      sourceId: edge.fromElementId,
      targetId: edge.toElementId,
      label: edge.outcome || undefined,
      // Outcome edges wear their answer: the definition's conditions, which the stylesheet paints.
      className: edge.outcome === "true"
        ? "databricks-edge-outcome-true"
        : edge.outcome === "false"
          ? "databricks-edge-outcome-false"
          : undefined,
    }));
    return { elements: [...frames, ...nodes], connections };
  }, [model, simulation.states, typeOf]);

  /**
   * Runs an action, letting the interception seam play it locally first (Requirement 8.6): a
   * simulated id starts the client-side show and never reaches executeAction, the history or
   * a file (Requirement 11.6); everything else travels as ever.
   */
  const runAction = (actionId: string, sourceId?: string) => {
    if ((interceptAction ?? simulation.intercept)(actionId)) {
      return;
    }

    // A refusal needs nothing here: the call reports it to the library's refusal line.
    void executeAction(actionId, sourceId ? elementSourceOf(sourceId, entryId) : undefined);
  };

  const events: DiagramEventHandlers = {
    // The declared actions, answered as the shortcuts the backend has always known them by.
    onActionInvoked: ({ actionId }) => {
      // A simulated entry chosen from the shared menu: the show plays here, and nothing travels.
      // Every other declared action is sent by the library, from its declared backendKey.
      if (actionId.includes(SIMULATED_MARKER)) {
        (interceptAction ?? simulation.intercept)(actionId);
      }
    },
    // Selection is the library's (centralized-selection), edges included: this family's edges
    // were pending, not exempt, and the backend has always resolved them. So is the refusal line:
    // every call here, and every menu action the library runs, reports its own refusal to it.
    onElementMoved: ({ elementId, position }) => {
      const isFrame = model.frames.has(elementId);
      const width = isFrame ? FRAME_WIDTH : NODE_WIDTH;
      const height = isFrame ? FRAME_HEIGHT : NODE_HEIGHT;
      // The authored position, raw: the layout block stores what the author placed, and
      // rounding it here would quietly turn the canvas into a grid.
      // Returned, so a refusal lets the library put the element back where it was (ruled 2026-10-19).
      return moveElementTo(elementId, position.x - width / 2, position.y - height / 2);
    },
    // The whole gesture in one stateless rel: call - the dragged task becomes the dependency
    // the landing task waits for. Released on nothing, the library raises nothing: this
    // family creates tasks by drop, not by relation-to-empty-space.
    onConnectionDrawn: ({ sourceElementId, targetElementId }) =>
      runAction("databricks.connect", relationId(sourceElementId, targetElementId)),
    // A toolbox drop names a placement - `new:{x},{y}` under the pointer (Requirement 9).
    onElementDropped: ({ elementType, position }) => runAction(elementType, placementId(position.x, position.y)),
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
    <div className="databricks-canvas canvas-host" role="application" aria-label={ariaLabel}>
      <DiagramCanvas
        definition={definition}
        model={diagramModel}
        events={events}
        source={{ entryId, path }}
        toolboxItems={toolboxItems}
        ariaLabel={ariaLabel}
        className="databricks-surface"
        scrollbarsClassName="databricks-scrollbars"
      />
      {simulation.marker ? (
        <button type="button" className="databricks-simulation-banner" onClick={simulation.dismiss}>
          {simulation.marker} · dismiss
        </button>
      ) : null}
    </div>
  );
}
