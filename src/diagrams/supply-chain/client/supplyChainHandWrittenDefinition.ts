import type { ActionInvocation } from "@client/canvas/library/definition/actions";
import { assertValidDiagramDefinition } from "@client/canvas/library/definition/validateDiagramDefinition";
import type { DiagramDefinition, ElementTypeDefinition } from "@client/canvas/library/definition/diagramDefinition";
import { CARD_INSET, HEADER, stepperType } from "./supplyChainBindings";
import { flowAdornment, flowPath } from "./supplyChainFlow";
import { FLOW_TYPE, GROUP_TYPE, StepperTypes, SUPPLY_CHAIN_STAGES, SupplyChainActions, type SupplyChainStage } from "./supplyChainIds";

/**
 * THE ORACLE, imported by tests only: the supply chain's canvas definition as the module stated it by hand
 * until it was compiled from `definition/supply-chain.dis` (supply-chain-disl-fbl, task 8), moved here
 * unchanged from `SupplyChainCanvas.tsx`. The flow's route and adornment and the steppers are the module's
 * own drawing still, so they are imported from where they now live rather than copied.
 * `supplyChainCompiledDefinition.test.tsx` holds the compiled definition to what this one draws.
 */

/** How much broader than a trickle the heaviest band is. */
const MAX_BAND_GROWTH = 15;

/** How far above a band's centre the product's name is set: past half the broadest band, and a little air. */
const PRODUCT_LABEL_OFFSET = -((3 + MAX_BAND_GROWTH) / 2 + 6);

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

const STAGE_TYPE_IDS: readonly string[] = SUPPLY_CHAIN_STAGES;

/** What a supply chain diagram draws, and what each gesture on it means. */
export const HAND_WRITTEN_SUPPLY_CHAIN_DEFINITION: DiagramDefinition = assertValidDiagramDefinition({
  elementTypes: [
    GROUP_ELEMENT_TYPE,
    ...SUPPLY_CHAIN_STAGES.map(stageType),
    stepperType(StepperTypes.down, "−", SupplyChainActions.decrease),
    stepperType(StepperTypes.up, "+", SupplyChainActions.increase),
  ],
  relationTypes: [
    {
      id: FLOW_TYPE,
      route: { customRoute: "supply-chain-flow", path: (from, to, waypoints, ends) => flowPath(from, to, ends, waypoints) },
      style: { endMarker: "none" },
      label: { placement: "midpoint", editable: true, offset: PRODUCT_LABEL_OFFSET },
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

