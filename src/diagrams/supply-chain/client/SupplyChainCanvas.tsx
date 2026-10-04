import { useMemo, useState, type ReactNode } from "react";

import { elementSourceOf } from "@client/canvas/selection";
import { DiagramCanvas } from "@client/canvas/library/DiagramCanvas";
import { assertValidDiagramDefinition } from "@client/canvas/library/definition/validateDiagramDefinition";
import type {
  DiagramDefinition,
  ElementTypeDefinition,
  RouteEnds,
  ShapeBounds,
  ShapePoint,
} from "@client/canvas/library/definition/diagramDefinition";
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
import { widthOf } from "@client/canvas/label/textMetrics";
import {
  FLOW_TYPE,
  GROUP_TYPE,
  StepperTypes,
  SUPPLY_CHAIN_ADD_ACTION_PREFIX,
  SUPPLY_CHAIN_STAGES,
  SupplyChainActions,
  SupplyChainTraces,
  type SupplyChainStage,
} from "./supplyChainIds";
import { applyDelta, emptyModel, formatAmount, type SupplyChainFlow, type SupplyChainModel } from "./supplyChainModel";

/** The word each stage's header shows. */
const STAGE_TITLES: Readonly<Record<SupplyChainStage, string>> = {
  "raw-material": "Raw material",
  supplier: "Supplier",
  manufacturer: "Manufacturer",
  assembler: "Assembler",
  distributor: "Distributor",
  retailer: "Retailer",
  consumer: "Consumer",
};

/** A stepper's size, and how far its centre sits from the card's right edge and from its centre line. */
const STEPPER = 22;
const STEPPER_GAP = 4;
const CARD_INSET = 14;
const HEADER = 24;

/** The keys that step: `+` with or without Shift (its key differs by layout), `=` beside it, and `-`. */
const INCREASE_KEYS: readonly ActionInvocation[] = [
  { kind: "shortcut", key: "+" },
  { kind: "shortcut", key: "+", shift: true },
  { kind: "shortcut", key: "=" },
];
const DECREASE_KEYS: readonly ActionInvocation[] = [
  { kind: "shortcut", key: "-" },
  { kind: "shortcut", key: "_", shift: true },
];

/** Where a flow leaves its supplier and reaches its consumer: the middle of the facing sides. */
function flowEndsOf(ends: RouteEnds): { start: ShapePoint; end: ShapePoint } {
  const forward = ends.target.x + ends.target.width / 2 >= ends.source.x + ends.source.width / 2;
  return {
    start: { x: forward ? ends.source.x + ends.source.width : ends.source.x, y: ends.source.y + ends.source.height / 2 },
    end: { x: forward ? ends.target.x : ends.target.x + ends.target.width, y: ends.target.y + ends.target.height / 2 },
  };
}

/**
 * The flow's curve: a horizontal S from one side's middle to the other's, so goods read left to
 * right the way the layout lays the chain out - and a flow that runs backwards still leaves and
 * arrives square to the side it uses.
 */
export function flowPath(from: ShapePoint, to: ShapePoint, ends?: RouteEnds): string {
  const { start, end } = ends !== undefined ? flowEndsOf(ends) : { start: from, end: to };
  const direction = end.x >= start.x ? 1 : -1;
  const pull = Math.max(48, Math.abs(end.x - start.x) / 2);
  const c1 = { x: start.x + direction * pull, y: start.y };
  const c2 = { x: end.x - direction * pull, y: end.y };
  return `M ${start.x} ${start.y} C ${c1.x} ${c1.y} ${c2.x} ${c2.y} ${end.x} ${end.y}`;
}

/** What a flow connection carries beside the library's own fields, for the adornment to read. */
interface FlowConnection extends DiagramModelConnection {
  flow: SupplyChainFlow;
}

/** The band's width for a flow's weight: thin for a trickle, broad for the heaviest in its unit. */
export function bandWidthOf(weight: number): number {
  return 3 + Math.round(Math.max(0, Math.min(1, weight)) * 15);
}

/** The volume pill's font size, as `supply-chain.css` sets it; the pill is measured at it. */
const VOLUME_FONT_SIZE = 10;

/**
 * What rides on a flow: the goods moving along it, an arrowhead square to the consumer's side, and
 * the volume in a pill under the product's name. Drawn inside the connection's group, so it
 * dims, lights and selects with the line it describes.
 */
function flowAdornment(route: { from: ShapePoint; to: ShapePoint; ends?: RouteEnds; highlighted?: boolean }, raw: unknown): ReactNode {
  const connection = raw as FlowConnection;
  const payload = connection.flow.payload;
  const d = flowPath(route.from, route.to, route.ends);
  const { start, end } = route.ends !== undefined ? flowEndsOf(route.ends) : { start: route.from, end: route.to };
  const direction = end.x >= start.x ? 1 : -1;
  const head = 7 + bandWidthOf(payload.weight) / 3;
  const mid = { x: (start.x + end.x) / 2, y: (start.y + end.y) / 2 };
  const volume = payload.hasVolume ? `${formatAmount(payload.volume)}${payload.unit ? ` ${payload.unit}` : ""}` : "";
  const pillWidth = Math.max(36, widthOf(volume, VOLUME_FONT_SIZE) + 16);

  return (
    <>
      <path className="supply-chain-flow-goods" d={d} style={{ strokeWidth: Math.max(2, bandWidthOf(payload.weight) * 0.4) }} />
      <path
        className="supply-chain-flow-head"
        d={`M ${end.x} ${end.y} L ${end.x - direction * head} ${end.y - head * 0.7} L ${end.x - direction * head} ${end.y + head * 0.7} Z`}
      />
      {volume !== "" && (
        <g className="supply-chain-flow-volume">
          <rect className="supply-chain-flow-volume-pill" x={mid.x - pillWidth / 2} y={mid.y + 4} width={pillWidth} height={18} rx={9} />
          <text className="supply-chain-flow-volume-text" x={mid.x} y={mid.y + 17} textAnchor="middle">
            {volume}
          </text>
        </g>
      )}
    </>
  );
}

/** Every stage draws the same card; only its colour, set by class from the stage, differs. */
function stageType(stage: SupplyChainStage): ElementTypeDefinition {
  return {
    id: stage,
    shape: "rounded-rectangle",
    classNames: [
      { className: `canvas-element supply-chain-node supply-chain-stage-${stage}`, on: "element" },
      { className: { template: "supply-chain-trace-{payload.trace}" }, when: { path: "payload.trace", is: "non-empty" }, on: "element" },
      { className: "canvas-node supply-chain-card", on: "shape" },
    ],
    decorations: [
      // The header band in the stage's colour: rounded on top, squared where it meets the body.
      { glyph: "rect", from: { x: { path: "payload.left" }, y: { path: "payload.top" } }, width: { path: "payload.width" }, height: HEADER, radius: 10, className: "supply-chain-header" },
      { glyph: "rect", from: { x: { path: "payload.left" }, y: { path: "payload.headerSeam" } }, width: { path: "payload.width" }, height: 10, className: "supply-chain-header" },
      // The share of its unit's largest quantity, as a bar along the card's foot.
      { glyph: "rect", from: { x: { path: "payload.barLeft" }, y: { path: "payload.barTop" } }, width: { path: "payload.barTrack" }, height: 4, radius: 2, className: "supply-chain-bar-track" },
      { glyph: "rect", from: { x: { path: "payload.barLeft" }, y: { path: "payload.barTop" } }, width: { path: "payload.barWidth" }, height: 4, radius: 2, className: "supply-chain-bar", when: { path: "payload.hasQuantity", is: "true" } },
      // The unit, set just after the number - a decoration because its place depends on the number's width.
      { glyph: "marker", text: { path: "payload.unit" }, textAt: { x: { path: "payload.unitX" }, y: { path: "payload.amountBaseline" } }, textAnchor: "start", className: "supply-chain-unit", when: { path: "payload.hasQuantity", is: "true" } },
    ],
    labels: [
      { text: { path: "payload.stageTitle" }, offset: { x: 0, y: 16 }, anchorTo: "top", align: "start", insetX: CARD_INSET, className: "supply-chain-stage-title" },
      {
        text: { path: "payload.name" },
        offset: { x: 0, y: 46 },
        anchorTo: "top",
        align: "start",
        insetX: CARD_INSET,
        truncate: true,
        editable: true,
        editorBox: { top: 30, height: 22 },
        className: "canvas-node-label supply-chain-name",
      },
      { text: { path: "payload.amount" }, offset: { x: 0, y: 74 }, anchorTo: "top", align: "start", insetX: CARD_INSET, className: "supply-chain-amount" },
    ],
    tooltip: { path: "payload.tooltip" },
    anchors: { kind: "compass", positions: ["e", "w"] },
    sizing: "model",
  };
}

/** A group's frame: a region, a company or a tier the nodes inside it belong to. */
const GROUP_ELEMENT_TYPE: ElementTypeDefinition = {
  id: GROUP_TYPE,
  shape: "rounded-rectangle",
  classNames: [
    { className: "canvas-element supply-chain-group", on: "element" },
    { className: { template: "supply-chain-trace-{payload.trace}" }, when: { path: "payload.trace", is: "non-empty" }, on: "element" },
    { className: "supply-chain-group-frame", on: "shape" },
  ],
  labels: [
    {
      text: { path: "payload.name" },
      offset: { x: 0, y: 26 },
      anchorTo: "top",
      align: "start",
      insetX: 16,
      truncate: true,
      editable: true,
      editorBox: { top: 10, height: 24 },
      className: "supply-chain-group-title",
    },
    { text: { path: "payload.members" }, offset: { x: 0, y: 26 }, anchorTo: "top", align: "end", insetX: 16, className: "supply-chain-group-members" },
  ],
  anchors: { kind: "edge", enabled: false, visible: false },
  sizing: "model",
  beneathConnections: true,
};

/**
 * The + and - beside a selected node's quantity or a selected flow's volume: small round buttons
 * the backend never sends. A press on one is its action rather than a selection, so the node stays
 * selected and its chain stays lit while the value is stepped.
 */
function stepperType(id: string, sign: string, actionId: string): ElementTypeDefinition {
  return {
    id,
    shape: "ellipse",
    classNames: [
      { className: `supply-chain-stepper ${id === StepperTypes.up ? "supply-chain-stepper-up" : "supply-chain-stepper-down"}`, on: "element" },
      { className: "supply-chain-stepper-disabled", when: { path: "payload.enabled", is: "false" }, on: "element" },
      { className: "supply-chain-stepper-button", on: "shape" },
    ],
    labels: [{ text: { path: "payload.sign" }, className: "supply-chain-stepper-sign" }],
    tooltip: { path: "payload.tooltip" },
    anchors: { kind: "edge", enabled: false, visible: false },
    sizing: "model",
    selectable: false,
    draggable: false,
    deletable: false,
    actions: [{ id: actionId, invokedBy: [{ kind: "gesture", gesture: "press" }], appliesTo: [{ kind: "element", elementTypes: [id] }], enabled: { path: "payload.enabled" } }],
    data: { stepper: sign },
  };
}

const STAGE_TYPE_IDS: readonly string[] = SUPPLY_CHAIN_STAGES;

/** What a supply chain diagram draws, and what each gesture on it means. */
export const SUPPLY_CHAIN_DEFINITION: DiagramDefinition = assertValidDiagramDefinition({
  elementTypes: [
    GROUP_ELEMENT_TYPE,
    ...SUPPLY_CHAIN_STAGES.map(stageType),
    stepperType(StepperTypes.down, "−", SupplyChainActions.decrease),
    stepperType(StepperTypes.up, "+", SupplyChainActions.increase),
  ],
  relationTypes: [
    {
      id: FLOW_TYPE,
      route: { customRoute: "supply-chain-flow", path: (from, to, _waypoints, ends) => flowPath(from, to, ends) },
      style: { endMarker: "none" },
      label: { placement: "midpoint", editable: true, offset: -8 },
      className: "supply-chain-flow",
      adorn: flowAdornment,
      endpoints: {
        source: { elementTypes: STAGE_TYPE_IDS },
        target: { elementTypes: STAGE_TYPE_IDS, anchors: "edge" },
        allowSelf: false,
        cardinality: { perPair: "ordered" },
      },
    },
  ],
  actions: [
    { id: SupplyChainActions.increase, invokedBy: INCREASE_KEYS, appliesTo: [{ kind: "element", elementTypes: STAGE_TYPE_IDS }, { kind: "connection" }] },
    { id: SupplyChainActions.decrease, invokedBy: DECREASE_KEYS, appliesTo: [{ kind: "element", elementTypes: STAGE_TYPE_IDS }, { kind: "connection" }] },
    {
      id: SupplyChainActions.rename,
      invokedBy: [{ kind: "shortcut", key: "F2" }, { kind: "gesture", gesture: "activate" }],
      appliesTo: [{ kind: "element", elementTypes: [...STAGE_TYPE_IDS, GROUP_TYPE] }, { kind: "connection" }],
    },
    { id: SupplyChainActions.remove, invokedBy: [{ kind: "gesture", gesture: "delete" }], appliesTo: [{ kind: "element", elementTypes: [...STAGE_TYPE_IDS, GROUP_TYPE] }, { kind: "connection" }] },
  ],
  layout: { modes: ["manual"] },
  dragging: "enabled",
  // A flow is drawn from a node's side handle, or by dragging with the right button from one node to another.
  connectOnRightDrag: true,
  // Arrange diagram and "Add … here" on empty canvas, from the backend's own list.
  backgroundMenu: true,
});

/** The declared actions this module forwards; anything else the library raises is not ours. */
const FORWARDED_ACTIONS: ReadonlySet<string> = new Set([
  SupplyChainActions.increase,
  SupplyChainActions.decrease,
  SupplyChainActions.rename,
  SupplyChainActions.remove,
]);

function isStage(value: string): value is SupplyChainStage {
  return (SUPPLY_CHAIN_STAGES as readonly string[]).includes(value);
}

/** Roughly how wide the amount is drawn, so the unit can sit just after it. */
function amountWidthOf(text: string): number {
  return [...text].reduce((width, character) => width + (/[0-9]/.test(character) ? 12 : 6), 0);
}

/** The library's elements and connections for a model: frames, cards, the steppers of what is selected, and flows. */
export function diagramModelOf(model: SupplyChainModel): { diagram: DiagramModel; stepperOwners: ReadonlyMap<string, string> } {
  const nodes = [...model.nodes.values()];
  const stepperOwners = new Map<string, string>();

  const groups = [...model.groups.values()].map((group): DiagramModelElement => ({
    id: group.id,
    type: GROUP_TYPE,
    x: group.x,
    y: group.y,
    width: group.payload.width,
    height: group.payload.height,
    label: group.payload.name,
    payload: {
      name: group.payload.name,
      members: group.payload.members === 1 ? "1 node" : `${group.payload.members} nodes`,
      trace: group.payload.trace,
    },
  }));

  const cards = nodes.map((node): DiagramModelElement => {
    const { width, height, quantity, hasQuantity, unit, share } = node.payload;
    const amount = hasQuantity ? formatAmount(quantity) : "—";
    const top = -height / 2;
    const track = width - CARD_INSET * 2;
    return {
      id: node.id,
      type: node.stage,
      x: node.x,
      y: node.y,
      width,
      height,
      label: node.payload.name,
      payload: {
        name: node.payload.name,
        stageTitle: STAGE_TITLES[node.stage].toUpperCase(),
        amount,
        unit,
        hasQuantity,
        trace: node.payload.trace,
        tooltip: `${STAGE_TITLES[node.stage]}: ${node.payload.name}${hasQuantity ? ` — ${amount}${unit ? ` ${unit}` : ""}` : ""}`,
        left: -width / 2,
        top,
        width,
        headerSeam: top + HEADER - 10,
        amountBaseline: top + 74,
        unitX: -width / 2 + CARD_INSET + amountWidthOf(amount) + 6,
        barLeft: -width / 2 + CARD_INSET,
        barTop: height / 2 - 12,
        barTrack: track,
        barWidth: Math.max(4, track * Math.min(1, share)),
      },
    };
  });

  const steppers: DiagramModelElement[] = [];
  const addSteppers = (ownerId: string, at: ShapePoint, value: number, what: string) => {
    for (const [type, sign, offset, verb] of [
      [StepperTypes.down, "−", -(STEPPER + STEPPER_GAP) / 2, "Decrease"],
      [StepperTypes.up, "+", (STEPPER + STEPPER_GAP) / 2, "Increase"],
    ] as const) {
      const id = `${ownerId}::${type}`;
      stepperOwners.set(id, ownerId);
      steppers.push({
        id,
        type,
        x: at.x + offset,
        y: at.y,
        width: STEPPER,
        height: STEPPER,
        payload: { sign, enabled: type === StepperTypes.up || value > 0, tooltip: `${verb} the ${what}` },
      });
    }
  };

  for (const node of nodes.filter((candidate) => candidate.payload.trace === SupplyChainTraces.selected)) {
    // Beside the amount, at the card's right edge.
    const right = node.x + node.payload.width / 2 - CARD_INSET - STEPPER - STEPPER_GAP / 2;
    addSteppers(node.id, { x: right, y: node.y - node.payload.height / 2 + 68 }, node.payload.quantity, "quantity");
  }

  const flows = [...model.flows.values()].flatMap((flow): FlowConnection[] => {
    const from = model.nodes.get(flow.payload.fromElementId);
    const to = model.nodes.get(flow.payload.toElementId);
    if (from === undefined || to === undefined) {
      return []; // an end is not held - off screen, or never declared; a line to nothing is worse than none
    }

    if (flow.payload.trace === SupplyChainTraces.selected) {
      // Under the volume pill, at the curve's middle - which is the middle of its two ends.
      const forward = to.x >= from.x;
      const startX = from.x + (forward ? 1 : -1) * from.payload.width / 2;
      const endX = to.x - (forward ? 1 : -1) * to.payload.width / 2;
      addSteppers(flow.id, { x: (startX + endX) / 2, y: (from.y + to.y) / 2 + 38 }, flow.payload.volume, "volume");
    }

    return [{
      id: flow.id,
      type: FLOW_TYPE,
      sourceId: flow.payload.fromElementId,
      targetId: flow.payload.toElementId,
      label: flow.payload.product || undefined,
      className: flow.payload.trace ? `supply-chain-trace-${flow.payload.trace}` : undefined,
      style: { strokeWidth: bandWidthOf(flow.payload.weight) },
      title: flow.payload.product,
      flow,
    }];
  });

  // Frames first so they lie beneath everything; steppers last so they lie on top of their card.
  return { diagram: { elements: [...groups, ...cards, ...steppers], connections: flows }, stepperOwners };
}

/**
 * A supply chain: raw materials, suppliers, plants, assemblers, distributors, retailers and
 * consumers in the regions they belong to, joined by flows as broad as the goods they carry.
 * Selecting anything lights the whole chain through it - what feeds it, and what it feeds - and
 * dims the rest; the backend works that out, so it arrives as ordinary deltas.
 */
export function SupplyChainCanvas({ projectId, entryId, path }: ToolContentProps) {
  const { watchId, executeAction } = useContextConnection();
  const { model, loading, failed, client, moveElementTo } = useDiagramStream(projectId, path, emptyModel, applyDelta);
  const toolboxItems = useToolboxItems(projectId, path);
  const [viewport, setViewport] = useState<ShapeBounds | null>(null);

  const { diagram, stepperOwners } = useMemo(() => diagramModelOf(model), [model]);

  // Every refusal reaches the one line the library draws around every canvas, and clears when sent.
  const runAction = (actionId: string, targetId: string) => {
    void executeAction(actionId, elementSourceOf(targetId, entryId));
  };

  const events: DiagramEventHandlers = {
    // A stepper names the node or flow it belongs to; everything else names itself.
    onActionInvoked: ({ actionId, targetId }) => {
      if (targetId !== undefined && FORWARDED_ACTIONS.has(actionId)) {
        runAction(actionId, stepperOwners.get(targetId) ?? targetId);
      }
    },
    // The library reports the CENTRE it drew the element at; the document holds the top-left. A
    // group's frame moves every member with it - the backend does that, given the frame's top-left.
    onElementMoved: ({ elementId, position }) => {
      const node = model.nodes.get(elementId);
      const group = model.groups.get(elementId);
      const size = node?.payload ?? group?.payload;
      if (size !== undefined) {
        void moveElementTo(elementId, position.x - size.width / 2, position.y - size.height / 2);
      }
    },
    onConnectionDrawn: ({ sourceElementId, targetElementId }) => {
      runAction(SupplyChainActions.connect, relationId(sourceElementId, targetElementId));
    },
    // The backend's toolbox drops its add action; a toolbox derived from the definition drops the bare stage.
    onElementDropped: ({ elementType, position }) => {
      const stage = elementType.startsWith(SUPPLY_CHAIN_ADD_ACTION_PREFIX) ? elementType.slice(SUPPLY_CHAIN_ADD_ACTION_PREFIX.length) : elementType;
      if (isStage(stage)) {
        runAction(SupplyChainActions.add(stage), placementId(position.x, position.y));
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
    <div className="supply-chain-canvas canvas-host" role="application" aria-label="Supply chain diagram">
      <DiagramCanvas
        definition={SUPPLY_CHAIN_DEFINITION}
        model={diagram}
        events={events}
        source={{ entryId, path }}
        toolboxItems={toolboxItems}
        ariaLabel="Supply chain diagram"
        className="supply-chain-surface"
      />
    </div>
  );
}
