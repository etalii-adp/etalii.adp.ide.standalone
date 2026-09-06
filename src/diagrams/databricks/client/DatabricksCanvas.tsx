import { useMemo, useState } from "react";

import { BoxElement } from "@client/canvas/elements/box/BoxElement";
import { FrameElement } from "@client/canvas/elements/frame/FrameElement";
import { edgePointOf } from "@client/canvas/connectors";
import { elementIdOfKey, elementSelectionOf, elementSourceOf } from "@client/canvas/selection";
import { isTextTarget, structuralShortcutFor } from "@client/canvas/interaction";
import { DiagramCanvas } from "@client/canvas/library/DiagramCanvas";
import { assertValidDiagramDefinition } from "@client/canvas/library/definition/validateDiagramDefinition";
import type {
  CustomRouteRef,
  CustomShapeRef,
  CustomShapeState,
  DiagramDefinition,
  ShapeBounds,
  ShapePoint,
} from "@client/canvas/library/definition/diagramDefinition";
import type { DiagramEventHandlers, DiagramSelection } from "@client/canvas/library/api/diagramEvents";
import type { DiagramModel, DiagramModelElement } from "@client/canvas/library/api/diagramModel";
import { inlineLabelElementIdOf } from "@client/shell/context/inlineLabelPrompt";
import { innermostKey, useContextConnection, useContextPrompt, useContextSelection } from "@client/shell/context/ContextConnectionProvider";
import { useToolboxItems } from "@client/shell/panels/useToolboxItems";
import type { DiagramCanvasProps } from "@client/shell/panels/diagramCanvas";
import { type ContextShortcut } from "@client/generated/context-contract_pb";
import { ContextSelectionAction } from "@client/generated/context_pb";
import { useViewReport } from "@client/diagrams/useViewReport";
import { useDatabricksStream } from "./useDatabricksStream";
import { useSimulatedRun } from "./useSimulatedRun";
import type { DatabricksFrame, DatabricksNode } from "./databricksModel";

/** A node's drawn size, in the module's own canvas units - matching the backend layouts' spacing. */
export const NODE_WIDTH = 200;
export const NODE_HEIGHT = 56;

/** A target frame's drawn size; the frame is an enclosure, so it is larger than a node. */
const FRAME_WIDTH = 220;
const FRAME_HEIGHT = 120;

/** FixedBezierConnection's control reach, kept for the family's layered edges. */
const EDGE_REACH = 30;

export interface DatabricksCanvasConfig {
  /** The aria label naming which of the family's readings this canvas draws. */
  ariaLabel: string;
  /** Whether side anchors offer the dependency gesture - the job canvas's interaction. */
  connectable: boolean;
  /**
   * Intercepts an action id before it reaches the backend; answering true means the canvas
   * handled it locally. The simulation engine's seam (Requirement 8.6): simulated ids play
   * client-side, everything else still travels.
   */
  interceptAction?: (actionId: string) => boolean;
}

/** An element as the library carries it here: the model element plus what it draws. */
type NodeElement = DiagramModelElement & { node: DatabricksNode; simulated: string | undefined };
type FrameBoxElement = DiagramModelElement & { frame: DatabricksFrame };

function boxEdgePoint(bounds: ShapeBounds, towards: ShapePoint): ShapePoint {
  const centre = { x: bounds.x + bounds.width / 2, y: bounds.y + bounds.height / 2 };
  return edgePointOf(
    { x: centre.x, y: centre.y, width: bounds.width, height: bounds.height },
    towards.x - centre.x,
    towards.y - centre.y,
  );
}

/** One node: kind, unresolved and simulation stylings ride its classes; badges its footer. */
const nodeShape: CustomShapeRef = {
  customShape: "databricks-node",
  render: (raw, state?: CustomShapeState) => {
    const element = raw as NodeElement;
    const node = element.node;
    const classes = ["databricks-node canvas-element", `databricks-node-${node.kind}`];
    if (node.unresolved) {
      classes.push("databricks-node-missing");
    }

    if (element.simulated) {
      classes.push(`databricks-sim-${element.simulated}`);
    }

    if (state?.selected) {
      classes.push("databricks-selected");
    }

    if (state?.connectTarget) {
      classes.push("databricks-connect-target canvas-connect-target");
    }

    return (
      <BoxElement
        className={classes.join(" ")}
        x={element.x - NODE_WIDTH / 2}
        y={element.y - NODE_HEIGHT / 2}
        width={NODE_WIDTH}
        height={NODE_HEIGHT}
        label={node.label}
        boxClassName="databricks-node-box canvas-node"
        labelClassName="databricks-label canvas-node-label"
        labelY={NODE_HEIGHT / 2 - 4}
      >
        {node.badges.length > 0 ? (
          <text className="databricks-badges" x={8} y={NODE_HEIGHT - 8}>
            {node.badges.join(" · ")}
          </text>
        ) : null}
      </BoxElement>
    );
  },
  edgePoint: boxEdgePoint,
};

/** A target frame: the dashed enclosure with its mode/default/override badges. */
const frameShape: CustomShapeRef = {
  customShape: "databricks-frame",
  render: (raw, state?: CustomShapeState) => {
    const element = raw as FrameBoxElement;
    const frame = element.frame;
    const badges = [frame.mode, frame.isDefault ? "default" : "", overrides(frame.overrideCount)]
      .filter((badge) => badge.length > 0)
      .join(" · ");

    return (
      <FrameElement
        className={`databricks-frame${state?.selected ? " databricks-selected" : ""}`}
        x={element.x}
        y={element.y}
        width={FRAME_WIDTH}
        height={FRAME_HEIGHT}
        label={frame.label}
        labelClassName="databricks-frame-label"
      >
        {badges ? (
          <text className="databricks-badges" x={-FRAME_WIDTH / 2 + 12} y={-FRAME_HEIGHT / 2 + 18}>
            {badges}
          </text>
        ) : null}
      </FrameElement>
    );
  },
  edgePoint: boxEdgePoint,
};

/**
 * A dependency or flow edge, exactly as FixedBezierConnection drew it: out of the source's
 * right edge, into the target's left, with the fixed control reach the layered layout wants.
 */
const layeredRoute: CustomRouteRef = {
  customRoute: "databricks-layered",
  path: (from, to, _waypoints, ends) => {
    const a = ends ? { x: ends.source.x + ends.source.width, y: ends.source.y + ends.source.height / 2 } : from;
    const b = ends ? { x: ends.target.x, y: ends.target.y + ends.target.height / 2 } : to;
    return `M ${a.x} ${a.y} C ${a.x + EDGE_REACH} ${a.y}, ${b.x - EDGE_REACH} ${b.y}, ${b.x} ${b.y}`;
  },
};

/**
 * What the family's diagrams allow, stated once per reading: task and resource nodes that
 * drag and select, frames that drag and select, three edge kinds - depends and flow on the
 * layered bezier, overrides straight - and the dependency gesture only where the reading
 * offers it (the job canvas). Edges are not selectable in this family and the migration is
 * not the moment that changes: a press on one is ignored, exactly as it fell on nothing
 * before.
 */
function definitionFor(connectable: boolean): DiagramDefinition {
  const targets = ["task", "node", "frame"];
  return assertValidDiagramDefinition({
    elementTypes: [
      {
        id: "task",
        shape: nodeShape,
        label: { placement: "inside", editable: true },
        anchors: connectable
          ? {
            kind: "sides",
            fractions: [
              { side: "left", at: 0.5, name: "left" },
              { side: "right", at: 0.5, name: "right" },
            ],
          }
          : { kind: "edge" },
        sizing: "model",
      },
      { id: "node", shape: nodeShape, label: { placement: "inside", editable: true }, anchors: { kind: "edge" }, sizing: "model" },
      { id: "frame", shape: frameShape, anchors: { kind: "edge" }, sizing: "model" },
    ],
    relationTypes: [
      {
        id: "depends",
        route: layeredRoute,
        style: { endMarker: "arrow" },
        label: { placement: "midpoint", offset: -6 },
        className: "databricks-edge databricks-edge-depends",
        endpoints: {
          source: { elementTypes: ["task"], anchors: connectable ? ["left", "right"] : [] },
          target: { elementTypes: targets, anchors: "edge" },
          allowSelf: false,
        },
      },
      {
        id: "flow",
        route: layeredRoute,
        style: { endMarker: "arrow" },
        label: { placement: "midpoint", offset: -6 },
        className: "databricks-edge databricks-edge-flow",
        endpoints: {
          source: { elementTypes: targets, anchors: [] },
          target: { elementTypes: targets, anchors: "edge" },
          allowSelf: false,
        },
      },
      {
        id: "override",
        route: "straight",
        style: { endMarker: "arrow" },
        className: "databricks-override",
        lineClassName: "databricks-override-line",
        endpoints: {
          source: { elementTypes: targets, anchors: [] },
          target: { elementTypes: targets, anchors: "edge" },
          allowSelf: false,
        },
      },
    ],
    layout: { modes: ["manual"] },
    dragging: "enabled",
  });
}

const CONNECTABLE_DEFINITION = definitionFor(true);
const RENDER_ONLY_DEFINITION = definitionFor(false);

/**
 * The family's shared canvas: boxes, frames and directed edges at the positions the backend
 * computed and the `.adp` authored, drawn through the central canvas library. The three
 * diagram types differ in what arrives on the stream and whether the dependency gesture is
 * offered - everything else is one implementation (Requirement 1.2).
 *
 * A drag never writes the body file: `moveElementTo` lands in the registration's `layout:`
 * block as one undoable command (Requirement 7).
 */
export function DatabricksCanvas({
  projectId,
  entryId,
  path,
  ariaLabel,
  connectable,
  interceptAction,
}: DiagramCanvasProps & DatabricksCanvasConfig) {
  const { model, loading, failed, moveElementTo, reportView } = useDatabricksStream(projectId, path);
  const simulation = useSimulatedRun(model);
  const { select, executeAction, executeShortcut } = useContextConnection();
  const { selection, actions } = useContextSelection();
  const toolboxItems = useToolboxItems(projectId, path);
  const [rejection, setRejection] = useState("");
  const [viewport, setViewport] = useState<ShapeBounds | null>(null);

  const selectionKey = innermostKey(selection);
  const selectedId = elementIdOfKey(selectionKey ?? null);

  const { prompt, onPropose: onProposeLabel, onSubmit: onSubmitLabel, onCancel: onCancelLabel } = useContextPrompt();
  const editingId = inlineLabelElementIdOf(prompt);

  const diagramModel = useMemo<DiagramModel>(() => {
    // Frames first, so everything they enclose paints on top.
    const frames = [...model.frames.values()].map((frame): FrameBoxElement => ({
      id: frame.id,
      type: "frame",
      x: frame.x + FRAME_WIDTH / 2,
      y: frame.y + FRAME_HEIGHT / 2,
      width: FRAME_WIDTH,
      height: FRAME_HEIGHT,
      label: frame.label,
      frame,
    }));
    const nodes = [...model.nodes.values()].map((node): NodeElement => ({
      id: node.id,
      // The dependency gesture is tasks-only, so the task kind is its own element type.
      type: node.id.startsWith("task:") ? "task" : "node",
      x: node.x + NODE_WIDTH / 2,
      y: node.y + NODE_HEIGHT / 2,
      width: NODE_WIDTH,
      height: NODE_HEIGHT,
      label: node.label,
      node,
      simulated: simulation.states.get(node.id),
    }));
    const connections = [...model.edges.values()].map((edge) => ({
      id: edge.id,
      type: edge.kind,
      sourceId: edge.fromElementId,
      targetId: edge.toElementId,
      label: edge.outcome || undefined,
      className: edge.outcome === "true"
        ? "databricks-edge-outcome-true"
        : edge.outcome === "false"
          ? "databricks-edge-outcome-false"
          : undefined,
    }));
    return { elements: [...frames, ...nodes], connections };
  }, [model, simulation.states]);

  /** The backend's push is the selection; the canvas renders it and never decides. */
  const librarySelection = useMemo<DiagramSelection>(() => {
    if (selectedId === null) {
      return [];
    }
    return [{ kind: model.edges.has(selectedId) ? "connection" : "element", id: selectedId }];
  }, [selectedId, model.edges]);

  /**
   * Runs an action, letting the interception seam play it locally first (Requirement 8.6): a
   * simulated id starts the client-side show and never reaches executeAction, the history or
   * a file (Requirement 11.6); everything else travels as ever.
   */
  const runAction = (actionId: string, sourceId?: string) => {
    if ((interceptAction ?? simulation.intercept)(actionId)) {
      return;
    }

    void (async () => {
      const outcome = await executeAction(actionId, sourceId ? elementSourceOf(sourceId) : undefined);
      if (!outcome.accepted && outcome.error) {
        setRejection(outcome.error);
      }
    })();
  };

  const runShortcut = (shortcut: ContextShortcut, sourceId: string) => {
    void (async () => {
      const outcome = await executeShortcut(shortcut, elementSourceOf(sourceId));
      if (!outcome.accepted && outcome.error) {
        setRejection(outcome.error);
      }
    })();
  };

  const events: DiagramEventHandlers = {
    onSelectionChanged: ({ selection: next }) => {
      // Edges are not selectable in this family (recorded pending elsewhere): a press on a
      // connection is ignored, exactly as it fell on nothing before the migration.
      if (next.some((item) => item.kind === "connection")) {
        return;
      }
      const element = next.find((item) => item.kind === "element");
      select(element !== undefined ? elementSelectionOf(entryId, path, element.id) : null);
    },
    onElementMoved: ({ elementId, position }) => {
      setRejection("");
      const isFrame = model.frames.has(elementId);
      const width = isFrame ? FRAME_WIDTH : NODE_WIDTH;
      const height = isFrame ? FRAME_HEIGHT : NODE_HEIGHT;
      // The authored position, raw: the layout block stores what the author placed, and
      // rounding it here would quietly turn the canvas into a grid.
      void (async () => {
        const error = await moveElementTo(elementId, position.x - width / 2, position.y - height / 2);
        if (error) {
          setRejection(error);
        }
      })();
    },
    // The whole gesture in one stateless rel: call - the dragged task becomes the dependency
    // the landing task waits for. Released on nothing, the library raises nothing: this
    // family creates tasks by drop, not by relation-to-empty-space.
    onConnectionDrawn: ({ sourceElementId, targetElementId }) =>
      runAction("databricks.connect", `rel:${sourceElementId}->${targetElementId}`),
    // A toolbox drop names a placement - `new:{x},{y}` under the pointer (Requirement 9).
    onElementDropped: ({ elementType, position }) => runAction(elementType, `new:${position.x},${position.y}`),
    onElementDeleted: ({ elementId }) =>
      runShortcut({ key: "Delete", ctrl: false, shift: false, alt: false, meta: false } as ContextShortcut, elementId),
    onConnectionDeleted: ({ connectionId }) =>
      runShortcut({ key: "Delete", ctrl: false, shift: false, alt: false, meta: false } as ContextShortcut, connectionId),
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

  /** F2 travels to the backend as data; Delete is the library's event, handled above. */
  const onKeyDown = (event: React.KeyboardEvent) => {
    if (!selectedId || isTextTarget(event.target)) {
      return;
    }

    const shortcut = structuralShortcutFor(event, ["F2"]);
    if (!shortcut) {
      return;
    }

    event.preventDefault();
    runShortcut(shortcut, selectedId);
  };

  if (failed) {
    return (
      <div className="databricks-canvas canvas-host databricks-canvas-message canvas-host-message">
        <p>This diagram could not be opened.</p>
      </div>
    );
  }

  return (
    <div className="databricks-canvas canvas-host" role="application" aria-label={ariaLabel} onKeyDown={onKeyDown}>
      <DiagramCanvas
        definition={connectable ? CONNECTABLE_DEFINITION : RENDER_ONLY_DEFINITION}
        model={diagramModel}
        events={events}
        selection={librarySelection}
        toolboxItems={toolboxItems}
        editing={{ editingId, onPropose: onProposeLabel, onSubmit: onSubmitLabel, onCancel: onCancelLabel }}
        context={{
          selectionKey: selectionKey ?? undefined,
          actions,
          selectForMenu: (id) => select(elementSelectionOf(entryId, path, id, ContextSelectionAction.CONTEXT_MENU)),
          executeAction: (actionId) => runAction(actionId, selectedId ?? undefined),
        }}
        ariaLabel={ariaLabel}
        className="databricks-surface"
        scrollbarsClassName="databricks-scrollbars"
      />
      {simulation.marker ? (
        <button type="button" className="databricks-simulation-banner" onClick={simulation.dismiss}>
          {simulation.marker} · dismiss
        </button>
      ) : null}
      {loading ? <p className="databricks-status canvas-status">Opening…</p> : null}
      {rejection ? <p className="databricks-rejection canvas-rejection">{rejection}</p> : null}
    </div>
  );
}

function overrides(count: number): string {
  return count === 0 ? "" : count === 1 ? "1 override" : `${count} overrides`;
}
