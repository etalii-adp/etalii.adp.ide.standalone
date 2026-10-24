import { describe, expect, it, vi } from "vitest";
import { render } from "@testing-library/react";
import type { ReactNode } from "react";
import { create } from "@bufbuild/protobuf";
import { SankeyFlowPayloadSchema, SankeyNodePayloadSchema } from "@client/generated/sankey_pb";
import disText from "../definition/sankey.dis?raw";
import { assertValidDiagramDefinition } from "@client/canvas/library/definition/validateDiagramDefinition";
import type { ActionInvocation } from "@client/canvas/library/definition/actions";
import type { CustomRouteRef, DiagramDefinition, ElementTypeDefinition, LabelDeclaration, ShapePoint } from "@client/canvas/library/definition/diagramDefinition";
import type { DiagramModel, DiagramModelConnection } from "@client/canvas/library/api/diagramModel";
import { compileNotation } from "@client/canvas/library/disl/compileNotation";
import { parseDisl } from "@client/canvas/library/disl/disTypes";
import { fakeContextConnection } from "@client/canvas/library/testing/canvasHarness";
import { SANKEY_BINDINGS } from "./sankeyBindings";
import { FLOW_TYPE, NODE_TYPE, SANKEY_COLORS, SankeyActions, SankeySides } from "./sankeyIds";
import { NODE_GAP, type SankeyFlow, type SankeyModel } from "./sankeyModel";

vi.mock("@client/shell/context/ContextConnectionProvider", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@client/shell/context/ContextConnectionProvider")>();
  return {
    ...actual,
    useContextPrompt: () => ({ prompt: null, onPropose: vi.fn(), onSubmit: vi.fn(), onCancel: vi.fn() }),
    useContextConnection: () => connection,
    useContextSelection: () => ({ selection: null }),
  };
});

const connection = fakeContextConnection({ select: vi.fn(), revealPath: vi.fn() });

const { DiagramCanvas } = await import("@client/canvas/library/DiagramCanvas");
const { SANKEY_DEFINITION, diagramModelOf, paintOf } = await import("./SankeyCanvas");

/*
 * The Sankey canvas definition, compiled from its bundled DISL specification, draws what the
 * definition the module stated by hand until the switch-over drew.
 *
 * The oracle below is that hand-written definition, moved here unchanged from `SankeyCanvas.tsx` with
 * the helpers only it used. The two are not equal key for key, and the differences are each a decision:
 * - A label states the `centre` it is measured from, which the oracle left as the default, and the
 *   labels after the bar come before those before it, in the specification's order.
 * - A node is dragged along y only, as its placement's `movable` says, where the oracle locked x with
 *   a snap of a million units resting on the node's own left; the payload carries no snap any more.
 * - The flow allows parallel instances, as the metamodel says: a second flow between the same two
 *   nodes in the same direction is refused by the backend's `connect` rule with its sentence, where
 *   the oracle refused it on the canvas without one. Its ends are attached to a part, so a connection
 *   whose attachment names a hidden segment is hidden, which no Sankey attachment does.
 * - The actions are the specification's context menus' entries: the delete gesture is one action per
 *   target, and rename applies to every element, of which there is one type.
 * - The band's highlight is the library's selection colour, with the fallback it states everywhere.
 * So the tests are the drawing, the declarations once those decisions are taken out, and the actions
 * as the specification states them.
 */

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

/** How strongly a band's curve pulls: half the distance it travels, so it leaves and arrives level. */
function pullOf(from: ShapePoint, to: ShapePoint): number {
  return Math.max(40, Math.abs(to.x - from.x) / 2);
}

function centrePath(from: ShapePoint, to: ShapePoint): string {
  const pull = pullOf(from, to);
  return `M ${from.x} ${from.y} C ${from.x + pull} ${from.y} ${to.x - pull} ${to.y} ${to.x} ${to.y}`;
}

function bandPath(from: ShapePoint, to: ShapePoint, thickness: number): string {
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

/** A token as CSS can use it inline; a literal colour as itself. */
function cssColour(paint: string): string {
  return paint.startsWith("--") ? `var(${paint})` : paint;
}

/** What a flow connection carries beside the library's own fields, for the band to read. */
interface FlowConnection extends DiagramModelConnection {
  flow: SankeyFlow;
}

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
  anchors: { kind: "along", edges: ["left", "right"], visible: false },
  sizing: "model",
};

/** THE ORACLE: what the module stated by hand. */
const HAND_WRITTEN: DiagramDefinition = assertValidDiagramDefinition({
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
  snap: { x: { step: { path: "payload.snapStep" }, origin: { path: "payload.snapLeft" } } },
  connectOnRightDrag: true,
  backgroundMenu: true,
});

/**
 * Every case the drawing distinguishes: nodes in the first column, labelled before the bar, and in
 * later ones, labelled after it; with a note and without; each palette word, no colour and a hex
 * colour; flows in each, steep and level, thick and thin, one back within its column, and one whose
 * target is not held.
 */
function corpus(): SankeyModel {
  const words = ["", ...SANKEY_COLORS];
  const node = (id: string, name: string, height: number, column: number, y: number, color: string, customColor = "", note = "") => {
    const payload = create(SankeyNodePayloadSchema, { name, displayValue: `€${height}M`, note, width: 24, height, column, color, customColor, side: column === 0 ? "left" : "right" });
    return [id, { id, x: 12 + column * 320, y, payload }] as const;
  };
  let top = 0;
  const sources = words.map((word, index) => {
    const height = 40 + index * 5;
    const y = top + height / 2;
    top += height + NODE_GAP;
    return node(`s${index}`, `Source ${index}`, height, 0, y, word, "", index % 3 === 0 ? `note ${index}` : "");
  });
  const nodes = new Map([
    ...sources,
    node("hex", "Brand", 120, 1, 80, "", "#336699", "+6% Y/Y"),
    node("mid", "Middle", 400, 1, 400, "green"),
    node("end", "Sink", 300, 2, 260, "red", "", "kept"),
  ]);
  const flow = (id: string, fromElementId: string, toElementId: string, thickness: number, sourceAt: number, targetAt: number, color: string, customColor = "") =>
    [id, { id, payload: create(SankeyFlowPayloadSchema, { fromElementId, toElementId, displayValue: `€${thickness}M`, thickness, sourceAt, targetAt, color, customColor }) }] as const;
  const flows = new Map([
    ...words.map((word, index) => flow(`f${index}`, `s${index}`, index % 2 === 0 ? "mid" : "hex", 10 + index, 0.5, (index + 1) / (words.length + 1), word)),
    flow("hexFlow", "hex", "end", 120, 0.5, 0.2, "", "#aa3300"),
    flow("level", "mid", "end", 200, 0.5, 0.7, "blue"),
    // Within one column, backwards: so short that the curve's reach is its minimum of 40.
    flow("within", "hex", "mid", 8, 0.8, 0.05, "orange"),
    flow("dangling", "mid", "gone", 5, 0.9, 0.5, "grey"),
  ]);
  return { nodes, flows };
}

/** The model the canvas builds, each flow also carrying itself for the oracle's band. */
function modelOf(model: SankeyModel): DiagramModel {
  const diagram = diagramModelOf(model);
  return { ...diagram, connections: diagram.connections.map((drawn): FlowConnection => ({ ...drawn, flow: model.flows.get(drawn.id)! })) };
}

/** The canvas's markup, with the ids React generates per mount taken out. */
function drawn(definition: DiagramDefinition, model: DiagramModel): string {
  const { container, unmount } = render(
    <DiagramCanvas
      definition={definition}
      model={model}
      events={{}}
      source={{ entryId: new Uint8Array(16), path: ["flows.skv"] }}
      ariaLabel="Sankey diagram"
      className="sankey-surface"
    />,
  );
  const markup = container.innerHTML.replace(/«r[0-9a-z]+»|:r[0-9a-z]+:/g, "«id»");
  unmount();
  return markup;
}

/** A route's drawing at a few points, rising and falling, which is what makes two routes the same. */
function drawingOf(route: DiagramDefinition["relationTypes"][number]["route"]): unknown {
  const ref = route as CustomRouteRef;
  const points = [[{ x: 0, y: 0 }, { x: 296, y: 200 }], [{ x: 24, y: 300 }, { x: 40, y: 10 }]] as const;
  return { name: ref.customRoute, paths: points.map(([from, to]) => ref.path(from, to, [])) };
}

/** The compiled definition with the decisions the opening comment names taken back out. */
function withoutDecisions(definition: DiagramDefinition): unknown {
  const relation = definition.relationTypes[0]!;
  const { hideWhenAttachmentHidden: _hidden, adorn: _adorn, ...line } = relation;
  const type = definition.elementTypes[0]!;
  const { dragAxis: _axis, ...element } = type;
  const labels = (type.labels ?? []).map(({ anchorTo, ...label }) => (anchorTo === "centre" ? label : { anchorTo, ...label }));
  return {
    ...definition,
    actions: undefined,
    snap: undefined,
    elementTypes: [{ ...element, labels: [...labels.slice(3), ...labels.slice(0, 3)] }],
    relationTypes: [{ ...line, route: drawingOf(relation.route), endpoints: { ...relation.endpoints, cardinality: { perPair: "ordered" } } }],
  };
}

describe("the Sankey diagram's compiled definition", () => {
  it("draws what the hand-written definition drew, bar for bar and band for band", () => {
    const model = modelOf(corpus());
    const compiled = drawn(SANKEY_DEFINITION, model);

    // The case the comparison would pass vacuously on: a canvas that drew nothing for either.
    expect(compiled).toContain("sankey-band");
    expect(compiled).toContain("sankey-bar");
    expect(compiled).toContain("sankey-note");
    expect(compiled).toContain("#336699");
    expect(compiled).toContain("var(--color-diagram-sankey-brown)");
    expect(compiled.match(/class="sankey-band"/g)).toHaveLength(SANKEY_COLORS.length + 4);
    expect(compiled).toBe(drawn(HAND_WRITTEN, model));
  });

  it("declares what the hand-written definition declared, but for the decisions it names", () => {
    const { adorn: _adorn, ...line } = HAND_WRITTEN.relationTypes[0]!;
    const oracle = { ...HAND_WRITTEN, actions: undefined, snap: undefined, relationTypes: [{ ...line, route: drawingOf(line.route) }] };

    expect(withoutDecisions(SANKEY_DEFINITION)).toEqual(oracle);
    expect(SANKEY_DEFINITION.elementTypes[0]!.dragAxis).toBe("y");
  });

  it("offers the specification's actions, with the second keys of + and -", () => {
    expect(SANKEY_DEFINITION.actions).toEqual([
      { id: SankeyActions.remove, invokedBy: [{ kind: "gesture", gesture: "delete" }], appliesTo: [{ kind: "element" }] },
      { id: SankeyActions.remove, invokedBy: [{ kind: "gesture", gesture: "delete" }], appliesTo: [{ kind: "connection" }] },
      { id: SankeyActions.rename, invokedBy: [{ kind: "shortcut", key: "F2" }, { kind: "gesture", gesture: "activate" }], appliesTo: [{ kind: "element" }] },
      { id: SankeyActions.increase, invokedBy: INCREASE_KEYS, appliesTo: [{ kind: "connection" }] },
      { id: SankeyActions.decrease, invokedBy: DECREASE_KEYS, appliesTo: [{ kind: "connection" }] },
    ]);
  });

  it("is what the canvas draws: the module's definition is compiled from the bundled specification", () => {
    // The canary for the tests above: were the canvas still holding its own definition, they would
    // compare the oracle with a copy of itself.
    // The band's adornment is made per compile, so it is held to its drawing above and its presence here.
    const withoutBand = (definition: DiagramDefinition) => ({ ...definition, relationTypes: definition.relationTypes.map(({ adorn, ...relation }) => ({ ...relation, adorned: adorn !== undefined })) });
    expect(withoutBand(SANKEY_DEFINITION)).toEqual(withoutBand(assertValidDiagramDefinition(compileNotation(parseDisl(disText), SANKEY_BINDINGS))));
  });
});
