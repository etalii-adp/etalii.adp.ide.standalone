import { useEffect, useMemo, useState, type ReactNode } from "react";

import { elementSourceOf } from "@client/canvas/selection";
import { DiagramCanvas } from "@client/canvas/library/DiagramCanvas";
import { assertValidDiagramDefinition } from "@client/canvas/library/definition/validateDiagramDefinition";
import type { DiagramDefinition, ElementTypeDefinition, LabelDeclaration, ShapeBounds, ShapePoint } from "@client/canvas/library/definition/diagramDefinition";
import type { ActionInvocation } from "@client/canvas/library/definition/actions";
import type { DiagramEventHandlers } from "@client/canvas/library/api/diagramEvents";
import type { DiagramModel, DiagramModelConnection, DiagramModelElement } from "@client/canvas/library/api/diagramModel";
import { useContextConnection } from "@client/shell/context/ContextConnectionProvider";
import { useToolboxItems } from "@client/shell/panels/useToolboxItems";
import type { ToolContentProps } from "@client/shell/panels/toolPanelRegistration";
import { useDiagramStream } from "@client/diagrams/useDiagramStream";
import { viewReportOf } from "@client/diagrams/viewReport";
import { useViewReport } from "@client/diagrams/useViewReport";
import { placementId, relationId } from "@client/canvas/gestureIds";
import { DEFAULT_COLOR, FLOW_TYPE, NODE_TYPE, SankeyActions, SankeySides } from "./sankeyIds";
import { applyDelta, columnPlaceOf, emptyModel, type SankeyFlow, type SankeyModel } from "./sankeyModel";

/** How far a label sits from its bar: the bar's width plus a little air, measured from the far edge. */
const LABEL_INSET = 24 + 10;

/** The keys that step a flow: `+` with or without Shift (its key differs by layout), `=` beside it, and `-`. */
const INCREASE_KEYS: readonly ActionInvocation[] = [
  { kind: "shortcut", key: "+" },
  { kind: "shortcut", key: "+", shift: true },
  { kind: "shortcut", key: "=" },
];
const DECREASE_KEYS: readonly ActionInvocation[] = [
  { kind: "shortcut", key: "-" },
  { kind: "shortcut", key: "_", shift: true },
];

/**
 * A step so large that the only resting line is the one at the node's own left: a node moves up and
 * down its column and never out of it. A column is decided by the flows, or by a `column` the
 * property grid sets; a drag sideways would be a third way that the document could not keep.
 */
const COLUMN_LOCK = 1_000_000;

/** How strongly a band's curve pulls: half the distance it travels, so it leaves and arrives level. */
function pullOf(from: ShapePoint, to: ShapePoint): number {
  return Math.max(40, Math.abs(to.x - from.x) / 2);
}

/**
 * The centre line of a band: a horizontal S from where it leaves its source's right edge to where
 * it reaches its target's left edge. It is what the library hit-tests and what a connect preview
 * draws; the band itself is {@link bandPath}, drawn around it.
 */
export function centrePath(from: ShapePoint, to: ShapePoint): string {
  const pull = pullOf(from, to);
  return `M ${from.x} ${from.y} C ${from.x + pull} ${from.y} ${to.x - pull} ${to.y} ${to.x} ${to.y}`;
}

/**
 * The band itself, filled: its top edge and its bottom edge are the same S, `thickness` apart
 * VERTICALLY all the way along.
 *
 * <b>Filled rather than stroked, because a stroke is thick across the curve, not down it.</b> A
 * stroked S is as wide as its value only where it runs level; on the slope it narrows, so a band
 * looks pinched exactly where two bands cross, and the ends it meets at the bars no longer line up
 * with the stacked bands beside it. Two parallel curves keep the value's height at every x - which
 * is how the bands at a bar stack edge to edge with no gap and no overlap.
 */
export function bandPath(from: ShapePoint, to: ShapePoint, thickness: number): string {
  const half = thickness / 2;
  const pull = pullOf(from, to);
  const [x0, x1, c0, c1] = [from.x, to.x, from.x + pull, to.x - pull];
  return [
    `M ${x0} ${from.y - half}`,
    `C ${c0} ${from.y - half} ${c1} ${to.y - half} ${x1} ${to.y - half}`,
    `L ${x1} ${to.y + half}`,
    `C ${c1} ${to.y + half} ${c0} ${from.y + half} ${x0} ${from.y + half}`,
    "Z",
  ].join(" ");
}

/** A palette word as its theme token, or a `#rrggbb` as itself. */
export function paintOf(word: string, custom: string): string {
  return custom !== "" ? custom : `--color-diagram-sankey-${word !== "" ? word : DEFAULT_COLOR}`;
}

/** A token as CSS can use it inline; a literal colour as itself. */
function cssColour(paint: string): string {
  return paint.startsWith("--") ? `var(${paint})` : paint;
}

/** What a flow connection carries beside the library's own fields, for the band to read. */
interface FlowConnection extends DiagramModelConnection {
  flow: SankeyFlow;
}

/**
 * The band: a filled ribbon in its colour, as thick as its value, drawn inside the connection's
 * group so a press on it selects the flow. The library's own line is the centre, kept for hit
 * testing and hidden by the stylesheet; the highlight is painted here instead, as an outline.
 */
function bandAdornment(route: { from: ShapePoint; to: ShapePoint; highlighted?: boolean }, raw: unknown): ReactNode {
  const { flow } = raw as FlowConnection;
  const paint = cssColour(paintOf(flow.payload.color, flow.payload.customColor));
  return (
    <path
      className="sankey-band"
      d={bandPath(route.from, route.to, flow.payload.thickness)}
      style={{
        fill: paint,
        ...(route.highlighted === true ? { stroke: "var(--color-selected)", strokeWidth: 2 } : {}),
      }}
    />
  );
}

/** One side's three lines: the name in the node's colour, the value beneath it, and the note under that. */
function sideLabels(side: string, align: "start" | "end"): LabelDeclaration[] {
  const on = { path: "payload.side", equals: side };
  return [
    { text: { path: "payload.name" }, offset: { x: 0, y: -5 }, align, insetX: LABEL_INSET, when: on, typography: { fontSize: 13, fontWeight: "bold" }, className: "sankey-label sankey-name" },
    { text: { path: "payload.displayValue" }, offset: { x: 0, y: 11 }, align, insetX: LABEL_INSET, when: on, typography: { fontSize: 12 }, className: "sankey-label sankey-value" },
    {
      text: { path: "payload.note" },
      offset: { x: 0, y: 26 },
      align,
      insetX: LABEL_INSET,
      when: { path: "payload.noteSide", equals: side },
      typography: { fontSize: 11, fontStyle: "italic", color: "--color-text-muted" },
      className: "sankey-label sankey-note",
    },
  ];
}

/** A bar: as tall as its value, painted in its colour, its labels before it in the first column and after it elsewhere. */
const NODE_ELEMENT_TYPE: ElementTypeDefinition = {
  id: NODE_TYPE,
  shape: "box",
  style: { stroke: "transparent" },
  boundStyle: { fill: { path: "payload.paint" }, labelColor: { path: "payload.paint" } },
  classNames: [
    { className: "canvas-element sankey-node", on: "element" },
    { className: "sankey-bar", on: "shape" },
  ],
  labels: [...sideLabels(SankeySides.left, "end"), ...sideLabels(SankeySides.right, "start")],
  tooltip: { path: "payload.tooltip" },
  // A band leaves anywhere along the right edge and arrives anywhere along the left, where the
  // layout stacked it; the whole edge is the handle, so no dot is drawn.
  anchors: { kind: "along", edges: ["left", "right"], visible: false },
  sizing: "model",
};

/** What a Sankey diagram draws, and what each gesture on it means. */
export const SANKEY_DEFINITION: DiagramDefinition = assertValidDiagramDefinition({
  elementTypes: [NODE_ELEMENT_TYPE],
  relationTypes: [
    {
      id: FLOW_TYPE,
      route: { customRoute: "sankey-band", path: (from, to) => centrePath(from, to) },
      style: { endMarker: "none" },
      className: "sankey-flow",
      lineClassName: "sankey-flow-line",
      adorn: bandAdornment,
      endpoints: {
        source: { elementTypes: [NODE_TYPE] },
        target: { elementTypes: [NODE_TYPE] },
        allowSelf: false,
        cardinality: { perPair: "ordered" },
      },
    },
  ],
  actions: [
    { id: SankeyActions.increase, invokedBy: INCREASE_KEYS, appliesTo: [{ kind: "connection" }] },
    { id: SankeyActions.decrease, invokedBy: DECREASE_KEYS, appliesTo: [{ kind: "connection" }] },
    {
      id: SankeyActions.rename,
      invokedBy: [{ kind: "shortcut", key: "F2" }, { kind: "gesture", gesture: "activate" }],
      appliesTo: [{ kind: "element", elementTypes: [NODE_TYPE] }],
    },
    { id: SankeyActions.remove, invokedBy: [{ kind: "gesture", gesture: "delete" }], appliesTo: [{ kind: "element", elementTypes: [NODE_TYPE] }, { kind: "connection" }] },
  ],
  layout: { modes: ["manual"] },
  dragging: "enabled",
  // Up and down its column only: x rests on the node's own left edge.
  snap: { x: { step: { path: "payload.snapStep" }, origin: { path: "payload.snapLeft" } } },
  // A flow is drawn by dragging with the right button from one node to another.
  connectOnRightDrag: true,
  // "Add node here" and the thickness on empty canvas, from the backend's own list.
  backgroundMenu: true,
});

/** The declared actions this module forwards; anything else the library raises is not ours. */
const FORWARDED_ACTIONS: ReadonlySet<string> = new Set([SankeyActions.increase, SankeyActions.decrease, SankeyActions.rename, SankeyActions.remove]);

/** Where nodes are drawn other than where the model has them: room made during a drag, and a drop awaiting its answer. */
export interface Rearrangement {
  /** How far each node moves down (or up, negative) to make room. */
  shifts: ReadonlyMap<string, number>;
  /** The dropped node, drawn at its slot until the model places it. */
  settled?: { id: string; y: number };
}

/** The library's elements and connections for a model, with any rearrangement a drag has made. */
export function diagramModelOf(model: SankeyModel, rearrangement?: Rearrangement): DiagramModel {
  const elements = [...model.nodes.values()].map((node): DiagramModelElement => {
    const { width, height, name, displayValue, note, side, color, customColor } = node.payload;
    const y = rearrangement?.settled?.id === node.id ? rearrangement.settled.y : node.y + (rearrangement?.shifts.get(node.id) ?? 0);
    return {
      id: node.id,
      type: NODE_TYPE,
      x: node.x,
      y,
      width,
      height,
      label: name,
      payload: {
        name,
        displayValue,
        note,
        side,
        noteSide: note !== "" ? side : "",
        paint: paintOf(color, customColor),
        tooltip: `${name}: ${displayValue}${note !== "" ? ` (${note})` : ""}`,
        snapLeft: node.x - width / 2,
        snapStep: COLUMN_LOCK,
      },
    };
  });

  const connections = [...model.flows.values()].flatMap((flow): FlowConnection[] => {
    const from = model.nodes.get(flow.payload.fromElementId);
    const to = model.nodes.get(flow.payload.toElementId);
    if (from === undefined || to === undefined) {
      return []; // an end is not held - off screen, or never declared; a band to nothing is worse than none
    }

    return [{
      id: flow.id,
      type: FLOW_TYPE,
      sourceId: from.id,
      targetId: to.id,
      sourceAttachment: { edge: "right", at: flow.payload.sourceAt },
      targetAttachment: { edge: "left", at: flow.payload.targetAt },
      title: `${from.payload.name} → ${to.payload.name}: ${flow.payload.displayValue}`,
      flow,
    }];
  });

  return { elements, connections };
}

/**
 * A Sankey diagram: quantities flowing between nodes, each band as thick as what it carries and
 * each bar as tall as what passes through it - money through an income statement, energy through
 * a country, people through their outcomes. Nothing here knows which: the document names the
 * nodes, and the backend lays them out.
 *
 * <b>Dragging a node reorders its column, and only that.</b> While it moves, the nodes it passes
 * step aside to show where it will land; on release the backend moves its entry and lays the
 * column out again, and until that answer arrives the node is drawn in the slot it was given.
 */
export function SankeyCanvas({ projectId, entryId, path }: ToolContentProps) {
  const { watchId, executeAction } = useContextConnection();
  const { model, loading, failed, client, moveElementTo } = useDiagramStream(projectId, path, emptyModel, applyDelta);
  const toolboxItems = useToolboxItems(projectId, path);
  const [viewport, setViewport] = useState<ShapeBounds | null>(null);
  const [rearrangement, setRearrangement] = useState<Rearrangement | null>(null);

  // The model's answer replaces whatever the drag drew: it is where everything really is now.
  useEffect(() => setRearrangement(null), [model]);

  const diagram = useMemo(() => diagramModelOf(model, rearrangement ?? undefined), [model, rearrangement]);

  // Every refusal reaches the one line the library draws around every canvas, and clears when sent.
  const runAction = (actionId: string, targetId: string) => {
    void executeAction(actionId, elementSourceOf(targetId, entryId));
  };

  const events: DiagramEventHandlers = {
    onActionInvoked: ({ actionId, targetId }) => {
      if (targetId !== undefined && FORWARDED_ACTIONS.has(actionId)) {
        runAction(actionId, targetId);
      }
    },
    // While a node is dragged, the nodes it passes make room; nothing is written until the release.
    onElementPreviewed: ({ elementId, bounds }) => {
      if (bounds === null) {
        // The gesture is over. A drop has already settled the node; an abandoned drag puts everything back.
        setRearrangement((current) => (current?.settled !== undefined ? current : null));
        return;
      }
      const place = columnPlaceOf(model, elementId, bounds.y + bounds.height / 2);
      if (place !== null) {
        setRearrangement((current) => (current !== null && sameShifts(current.shifts, place.shifts) ? current : { shifts: place.shifts }));
      }
    },
    // The library reports the CENTRE it drew the node at; the backend places by the top-left.
    onElementMoved: ({ elementId, position }) => {
      const node = model.nodes.get(elementId);
      const place = columnPlaceOf(model, elementId, position.y);
      if (node === undefined || place === null) {
        return;
      }
      // Drawn in its slot at once - a hair off its old place when it stays, so the library lets go of the drop.
      setRearrangement({ shifts: place.shifts, settled: { id: elementId, y: place.moved ? place.slotY : node.y + 0.01 } });
      void moveElementTo(elementId, position.x - node.payload.width / 2, position.y - node.payload.height / 2).then((refusal) => {
        if (refusal !== "" || !place.moved) {
          setRearrangement(null);
        }
      });
    },
    onConnectionDrawn: ({ sourceElementId, targetElementId }) => {
      runAction(SankeyActions.connect, relationId(sourceElementId, targetElementId));
    },
    // The backend's toolbox drops its add action; a toolbox derived from the definition drops the bare type.
    onElementDropped: ({ elementType, position }) => {
      if (elementType === SankeyActions.add || elementType === NODE_TYPE) {
        runAction(SankeyActions.add, placementId(position.x, position.y));
      }
    },
    onViewChanged: ({ viewport: next }) => setViewport(next),
  };

  useViewReport({
    view: { x: viewport?.x ?? 0, y: viewport?.y ?? 0, w: viewport?.width ?? 0, h: viewport?.height ?? 0 },
    report: viewReportOf(client, projectId, watchId, path),
    convert: () => ({
      minX: viewport?.x ?? 0,
      minY: viewport?.y ?? 0,
      maxX: (viewport?.x ?? 0) + (viewport?.width ?? 0),
      maxY: (viewport?.y ?? 0) + (viewport?.height ?? 0),
    }),
    ready: !loading && !failed && viewport !== null,
  });

  return (
    <div className="sankey-canvas canvas-host" role="application" aria-label="Sankey diagram">
      <DiagramCanvas
        definition={SANKEY_DEFINITION}
        model={diagram}
        events={events}
        source={{ entryId, path }}
        toolboxItems={toolboxItems}
        ariaLabel="Sankey diagram"
        className="sankey-surface"
      />
    </div>
  );
}

function sameShifts(one: ReadonlyMap<string, number>, other: ReadonlyMap<string, number>): boolean {
  return one.size === other.size && [...one].every(([id, shift]) => other.get(id) === shift);
}
