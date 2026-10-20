import { assertValidDiagramDefinition } from "@client/canvas/library/definition/validateDiagramDefinition";
import type { CustomRouteRef, DiagramDefinition } from "@client/canvas/library/definition/diagramDefinition";
import type { DiagramModel, DiagramModelElement } from "@client/canvas/library/api/diagramModel";
import { SIMULATED_ACTION_IDS } from "./useSimulatedRun";
import type { DatabricksFrame, DatabricksModel, DatabricksNode } from "./databricksModel";

/**
 * THE ORACLE, AND TEST CODE ONLY: the canvas definition `DatabricksCanvas.tsx` stated by hand until the
 * three readings were compiled from their bundled definitions, moved here unchanged with the model it
 * built, so `databricksCompiledDefinition.test.tsx` can hold the compiled drawings to it. Nothing in the
 * product imports this file.
 *
 * It was one definition for all three readings, told apart only by whether the dependency gesture was
 * offered: a `task` type for a job's tasks, one `node` type for every other box of every reading, a
 * `frame` for a bundle's targets, and the `depends`, `flow` and `override` relations.
 */

/** A node's drawn size, in the module's own canvas units - matching the backend layouts' spacing. */
export const NODE_WIDTH = 200;
export const NODE_HEIGHT = 56;

/** A target frame's drawn size; the frame is an enclosure, so it is larger than a node. */
export const FRAME_WIDTH = 220;
export const FRAME_HEIGHT = 120;

/** FixedBezierConnection's control reach, kept for the family's layered edges. */
const EDGE_REACH = 30;

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

/** The job reading's definition, which offered the dependency gesture. */
export const HAND_WRITTEN_CONNECTABLE: DiagramDefinition = definitionFor(true);

/** The bundle and pipeline readings' definition, which offered none. */
export const HAND_WRITTEN_RENDER_ONLY: DiagramDefinition = definitionFor(false);

function overrides(count: number): string {
  return count === 0 ? "" : count === 1 ? "1 override" : `${count} overrides`;
}

/** The model the hand-written definition drew, built as `DatabricksCanvas` built it. */
export function handWrittenModel(model: DatabricksModel, states: ReadonlyMap<string, string>): DiagramModel {
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
      ...(states.get(node.id) !== undefined ? { simulated: states.get(node.id) } : {}),
    },
    node,
    simulated: states.get(node.id),
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
}
