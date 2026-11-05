import type { ActionDeclaration, ActionInvocation } from "@client/canvas/library/definition/actions";
import type {
  ClassDeclaration,
  DecorationDeclaration,
  ElementTypeDefinition,
  LabelDeclaration,
} from "@client/canvas/library/definition/diagramDefinition";
import type { NotationBindings } from "@client/canvas/library/disl/compileNotation";
import { flowAdornment, supplyChainFlowRoute } from "./supplyChainFlow";
import { StepperTypes, SupplyChainActions } from "./supplyChainIds";

/**
 * What the supply chain's canvas needs beside its bundled specification, because the library cannot
 * read it from there. `compileNotation` derives everything else - the card and the frame, their labels,
 * the left and right sides a flow attaches to, the flow with its product label, the actions and the
 * background menu - from `definition/supply-chain.dis`.
 *
 * <b>Why each entry is code.</b>
 * - **The CEL paths.** The library has no CEL: the canvas computes each expression the notation binds into
 *   the element's payload (`diagramModelOf`), and this table names where.
 * - **The class names.** They tie the drawing to `supply-chain.css`: the stage's colour is a class bound
 *   to the payload's stage, and the trace's to the payload's trace, which the notation states as styles.
 * - **The card's parts.** The library draws the card as its rounded rectangle; the header band and the
 *   share bar the specification's `supplyCard` states are its ornaments (`CARD_PARTS`), which
 *   `supplyChainShapeGeometry.test.ts` holds to the specification's boxes.
 * - **What the notation cannot state** (finding A10, `supply-chain.md`, *Notation DISL cannot state*), drawn
 *   by the module in place of what the specification states (`drawnByModule`): the unit set just after the
 *   quantity's number, the goods moving along a band, the arrowhead square to the consumer's side, the
 *   volume pill under the product's name, the flow's path through the lanes the layout reserved for it, and
 *   the steppers under a selected flow's label and beside a selected node's quantity.
 * - **The second keys.** `+` with Shift, `=` and Shift with `_` step too; DISL gives an entry one shortcut.
 */

/** A card's size, as the specification fixes it. */
export const CARD_WIDTH = 192;
export const CARD_HEIGHT = 96;

/** How far the card's text and bar sit in from its sides, and how tall its header band is. */
export const CARD_INSET = 14;
export const HEADER = 24;

/** A stepper's size, and the gap between the two. */
export const STEPPER = 22;
export const STEPPER_GAP = 4;

/** The share bar's track: the card's width less its inset on each side. */
export const BAR_TRACK = CARD_WIDTH - CARD_INSET * 2;

/** The share bar's width for a share of its unit's largest quantity, never thinner than its rounding. */
export function barWidthOf(share: number): number {
  return Math.max(4, BAR_TRACK * Math.min(1, share));
}

/**
 * The + and - beside a selected node's quantity or a selected flow's volume: small round buttons
 * the backend never sends. A press on one is its action rather than a selection, so the node stays
 * selected and its chain stays lit while the value is stepped.
 */
export function stepperType(id: string, sign: string, actionId: string): ElementTypeDefinition {
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

/** The two steppers, after the types the specification states. */
export const STEPPER_TYPES: readonly ElementTypeDefinition[] = [
  stepperType(StepperTypes.down, "−", SupplyChainActions.decrease),
  stepperType(StepperTypes.up, "+", SupplyChainActions.increase),
];

/**
 * The card's parts beside its body, as the specification's `supplyCard` states them, measured from the
 * card's centre: the header band in the stage's colour, rounded on top and squared where it meets the
 * body, and the share bar on its track along the foot, drawn only while the node states a quantity.
 */
export const CARD_PARTS: readonly DecorationDeclaration[] = [
  { glyph: "rect", from: { x: -CARD_WIDTH / 2, y: -CARD_HEIGHT / 2 }, width: CARD_WIDTH, height: HEADER, radius: 10, className: "supply-chain-header" },
  { glyph: "rect", from: { x: -CARD_WIDTH / 2, y: -CARD_HEIGHT / 2 + HEADER - 10 }, width: CARD_WIDTH, height: 10, className: "supply-chain-header" },
  { glyph: "rect", from: { x: -CARD_WIDTH / 2 + CARD_INSET, y: CARD_HEIGHT / 2 - 12 }, width: BAR_TRACK, height: 4, radius: 2, className: "supply-chain-bar-track" },
  {
    glyph: "rect",
    from: { x: -CARD_WIDTH / 2 + CARD_INSET, y: CARD_HEIGHT / 2 - 12 },
    width: { path: "payload.barWidth" },
    height: 4,
    radius: 2,
    className: "supply-chain-bar",
    when: { path: "payload.hasQuantity", is: "true" },
  },
];

/** The quantity's number, which the specification joins to its unit in one label. */
export const AMOUNT_LABEL: LabelDeclaration = {
  text: { path: "payload.amount" },
  offset: { x: 0, y: 74 },
  anchorTo: "top",
  align: "start",
  insetX: CARD_INSET,
  className: "supply-chain-amount",
};

/** The unit, set just after the number - an ornament because its place depends on the number's width. */
export const UNIT_MARK: DecorationDeclaration = {
  glyph: "marker",
  text: { path: "payload.unit" },
  textAt: { x: { path: "payload.unitX" }, y: { path: "payload.amountBaseline" } },
  textAnchor: "start",
  className: "supply-chain-unit",
  when: { path: "payload.hasQuantity", is: "true" },
};

const TRACE_CLASS: ClassDeclaration = { className: { template: "supply-chain-trace-{payload.trace}" }, when: { path: "payload.trace", is: "non-empty" }, on: "element" };

const ELEMENT_CLASSES: Readonly<Record<string, readonly ClassDeclaration[]>> = {
  Stage: [
    { className: { template: "canvas-element supply-chain-node supply-chain-stage-{payload.stage}" }, on: "element" },
    TRACE_CLASS,
    { className: "canvas-node supply-chain-card", on: "shape" },
  ],
  Group: [
    { className: "canvas-element supply-chain-group", on: "element" },
    TRACE_CLASS,
    { className: "supply-chain-group-frame", on: "shape" },
  ],
};

const LABEL_CLASSES: Readonly<Record<string, string>> = {
  "Stage/stage": "supply-chain-stage-title",
  "Stage/name": "canvas-node-label supply-chain-name",
  "Group/name": "supply-chain-group-title",
  "Group/members": "supply-chain-group-members",
};

const EDITOR_BOXES: Readonly<Record<string, { top: number; height: number }>> = {
  "Stage/name": { top: 30, height: 22 },
  "Group/name": { top: 10, height: 24 },
};

/** The keys that step beside the one each menu entry writes: `+` with Shift (its key differs by layout), `=` beside it, and Shift with `_`. */
const SECOND_KEYS: Readonly<Record<string, readonly ActionInvocation[]>> = {
  "+": [{ kind: "shortcut", key: "+", shift: true }, { kind: "shortcut", key: "=" }],
  "-": [{ kind: "shortcut", key: "_", shift: true }],
};

/** An action as the canvas offers it: with the second keys its entry's shortcut has. */
function dispatched(action: ActionDeclaration, entry: { shortcut?: string }): ActionDeclaration {
  const more = entry.shortcut === undefined ? [] : (SECOND_KEYS[entry.shortcut] ?? []);
  return more.length === 0 ? action : { ...action, invokedBy: [...action.invokedBy, ...more] };
}

/** The node type the compiled definition gives every stage, whose colour is the class its payload's stage names. */
export const STAGE_TYPE = "stage";

export const SUPPLY_CHAIN_BINDINGS: NotationBindings = {
  wireIds: "x-supply-chain",
  celPaths: {
    "upper(stageLabel(self.stage))": "payload.stageTitle",
    "nameOf(self)": "payload.name",
    "stageLabel(self.stage) + ': ' + nameOf(self) + (has(self.quantity) ? ' — ' + formatAmount(self.quantity) + (self.unit != '' ? ' ' + self.unit : '') : '')": "payload.tooltip",
    "quantityShare(self)": "payload.share",
    "string(members(self).size()) + (members(self).size() == 1 ? ' node' : ' nodes')": "payload.members",
  },
  classNames: (type) => ELEMENT_CLASSES[type] ?? [],
  labelClassName: (type, label) => LABEL_CLASSES[`${type}/${label.id}`],
  labelEditorBox: (type, label) => EDITOR_BOXES[`${type}/${label.id}`],
  customShapes: { supplyCard: { shape: "rounded-rectangle", decorations: () => CARD_PARTS } },
  builtInShapes: { roundedRect: "rounded-rectangle" },
  drawnByModule: ["Stage/labels/amount", "Flow/labels/volume", "Flow/midMarkers", "Flow/targetMarker"],
  elementLabels: (type) => (type === "Stage" ? [AMOUNT_LABEL] : []),
  elementDecorations: (type) => (type === "Stage" ? [UNIT_MARK] : []),
  // A flow is drawn from a card's left or right handle, as the specification's `connect` says.
  sideHandles: (type) => type === "Stage",
  relationClassName: () => "supply-chain-flow",
  endpointAnchors: (_relation, end) => (end === "target" ? "edge" : undefined),
  customRoutes: { Flow: supplyChainFlowRoute },
  edgeAdornments: { Flow: flowAdornment },
  action: dispatched,
  extras: () => ({ elementTypes: STEPPER_TYPES }),
};
