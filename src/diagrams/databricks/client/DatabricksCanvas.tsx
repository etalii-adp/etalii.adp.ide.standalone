import { useMemo, useState } from "react";

import { elementSourceOf } from "@client/canvas/selection";
import { DiagramCanvas } from "@client/canvas/library/DiagramCanvas";
import { assertValidDiagramDefinition } from "@client/canvas/library/definition/validateDiagramDefinition";
import type {
  CustomRouteRef,
  DiagramDefinition,
  ShapeBounds,
} from "@client/canvas/library/definition/diagramDefinition";
import type { DiagramEventHandlers } from "@client/canvas/library/api/diagramEvents";
import type { DiagramModel, DiagramModelElement } from "@client/canvas/library/api/diagramModel";
import { useContextConnection } from "@client/shell/context/ContextConnectionProvider";
import { useToolboxItems } from "@client/shell/panels/useToolboxItems";
import type { DiagramCanvasProps } from "@client/shell/panels/diagramCanvas";
import { useViewReport } from "@client/diagrams/useViewReport";
import { useDatabricksStream } from "./useDatabricksStream";
import { SIMULATED_ACTION_IDS, SIMULATED_MARKER, useSimulatedRun } from "./useSimulatedRun";
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
        shape: "box",
        classNames: [
          { className: "databricks-node canvas-element", on: "element" },
          { className: { template: "databricks-node-{payload.kind}" }, on: "element" },
          { className: "databricks-node-missing", on: "element", when: { path: "payload.unresolved", is: "true" } },
          { className: { template: "databricks-sim-{payload.simulated}" }, on: "element", when: { path: "payload.simulated", is: "present" } },
          { className: "databricks-node-box canvas-node", on: "shape" },
        ],
        labels: [
          {
            text: { path: "payload.label" },
            anchorTo: "top",
            offset: { x: 0, y: NODE_HEIGHT / 2 - 4 },
            // Left, on the badge strip's edge, as the BoxElement drew it before the migration.
            align: "start",
            insetX: 8,
            editable: true,
            // The editor covers the whole box, as `placement: "inside"` did: this label sits
            // low in the box to leave room for the badge strip, but it is still the element's
            // one name and renaming it is renaming the task.
            editorBox: { top: 0, height: NODE_HEIGHT },
            truncate: true,
            className: "databricks-label canvas-node-label",
          },
          {
            // The badge strip, joined - one line rather than a stack, which is how the notation
            // shows it and why a collection binding may name its separator.
            text: { path: "payload.badges", each: { path: "text" }, join: " · " },
            when: { path: "payload.badges", is: "non-empty" },
            anchorTo: "top",
            offset: { x: 0, y: NODE_HEIGHT - 8 },
            align: "start",
            insetX: 8,
            className: "databricks-badges",
          },
        ],
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
      {
        id: "node",
        shape: "box",
        classNames: [
          { className: "databricks-node canvas-element", on: "element" },
          { className: { template: "databricks-node-{payload.kind}" }, on: "element" },
          { className: "databricks-node-missing", on: "element", when: { path: "payload.unresolved", is: "true" } },
          { className: { template: "databricks-sim-{payload.simulated}" }, on: "element", when: { path: "payload.simulated", is: "present" } },
          { className: "databricks-node-box canvas-node", on: "shape" },
        ],
        labels: [
          {
            text: { path: "payload.label" },
            anchorTo: "top",
            offset: { x: 0, y: NODE_HEIGHT / 2 - 4 },
            // Left, on the badge strip's edge, as the BoxElement drew it before the migration.
            align: "start",
            insetX: 8,
            editable: true,
            // The editor covers the whole box, as `placement: "inside"` did: this label sits
            // low in the box to leave room for the badge strip, but it is still the element's
            // one name and renaming it is renaming the task.
            editorBox: { top: 0, height: NODE_HEIGHT },
            truncate: true,
            className: "databricks-label canvas-node-label",
          },
          {
            // The badge strip, joined - one line rather than a stack, which is how the notation
            // shows it and why a collection binding may name its separator.
            text: { path: "payload.badges", each: { path: "text" }, join: " · " },
            when: { path: "payload.badges", is: "non-empty" },
            anchorTo: "top",
            offset: { x: 0, y: NODE_HEIGHT - 8 },
            align: "start",
            insetX: 8,
            className: "databricks-badges",
          },
        ],
        anchors: { kind: "edge" },
        sizing: "model",
      },
      {
        id: "frame",
        shape: "frame",
        classNames: [
          { className: "databricks-frame", on: "element" },
          // The dashed outline's own class: the stylesheet reaches the frame by it, never by a
          // descendant `rect` (client-centralization Requirement 3.1).
          { className: "databricks-frame-outline" },
        ],
        labels: [
          { text: { path: "payload.label" }, placement: "above", className: "databricks-frame-label" },
          {
            // `mode · default · 2 overrides`, with the parts that do not apply left out and no
            // separator left behind - which is the whole of a parts binding's justification.
            text: {
              parts: [
                { path: "payload.mode" },
                { template: "default", when: { path: "payload.isDefault", is: "true" } },
                { path: "payload.overrides", when: { path: "payload.overrides", is: "non-empty" } },
              ],
              join: " · ",
            },
            anchorTo: "top",
            offset: { x: 0, y: 18 },
            align: "start",
            insetX: 12,
            className: "databricks-badges",
          },
        ],
        anchors: { kind: "edge" },
        sizing: "model",
      },
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
    // WHAT THIS TYPE OFFERS, AND WHAT INVOKES IT. The key list was hand-written in this canvas
    // and the delete was a keystroke it built to describe a gesture the library had already
    // handed it. Declared, the library derives the key set and dispatches an action id.
    actions: [
      { id: "rename", backendKey: "F2", invokedBy: [{ kind: "shortcut", key: "F2" }], appliesTo: [{ kind: "element" }] },
      { id: "delete", backendKey: "Delete", invokedBy: [{ kind: "gesture", gesture: "delete" }], appliesTo: [{ kind: "element" }, { kind: "connection" }] },
      // THE SIMULATED RUNS ARE THIS CANVAS'S TO RUN (Requirements 8.6, 11.6). The backend offers
      // them in the shared menu; declared here as menu entries, the library hands them to this
      // module's handler and sends nothing - so a simulation never reaches a command, the history
      // or a file. The ids are the engine's own list, never retyped.
      ...SIMULATED_ACTION_IDS.map((id) => ({ id, invokedBy: [{ kind: "menu" as const }], appliesTo: [{ kind: "element" as const }] })),
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
  const { executeAction } = useContextConnection();
  const toolboxItems = useToolboxItems(projectId, path);
  const [viewport, setViewport] = useState<ShapeBounds | null>(null);

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
      payload: {
        label: frame.label,
        mode: frame.mode,
        isDefault: frame.isDefault,
        overrides: overrides(frame.overrideCount),
      },
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
      payload: {
        label: node.label,
        kind: node.kind,
        unresolved: node.unresolved,
        badges: node.badges.map((text) => ({ text })),
        ...(simulation.states.get(node.id) !== undefined ? { simulated: simulation.states.get(node.id) } : {}),
      },
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
    void executeAction(actionId, sourceId ? elementSourceOf(sourceId) : undefined);
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
      void moveElementTo(elementId, position.x - width / 2, position.y - height / 2);
    },
    // The whole gesture in one stateless rel: call - the dragged task becomes the dependency
    // the landing task waits for. Released on nothing, the library raises nothing: this
    // family creates tasks by drop, not by relation-to-empty-space.
    onConnectionDrawn: ({ sourceElementId, targetElementId }) =>
      runAction("databricks.connect", `rel:${sourceElementId}->${targetElementId}`),
    // A toolbox drop names a placement - `new:{x},{y}` under the pointer (Requirement 9).
    onElementDropped: ({ elementType, position }) => runAction(elementType, `new:${position.x},${position.y}`),
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
        definition={connectable ? CONNECTABLE_DEFINITION : RENDER_ONLY_DEFINITION}
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

function overrides(count: number): string {
  return count === 0 ? "" : count === 1 ? "1 override" : `${count} overrides`;
}
