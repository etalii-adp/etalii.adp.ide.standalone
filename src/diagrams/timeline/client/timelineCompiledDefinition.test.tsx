import { describe, expect, it, vi } from "vitest";
import { render } from "@testing-library/react";
import { forwardBezierPath, horizontalBezierPath } from "@client/canvas/connectors";
import disText from "../definition/timeline.dis?raw";
import { assertValidDiagramDefinition } from "@client/canvas/library/definition/validateDiagramDefinition";
import type { CustomRouteRef, DiagramDefinition, LabelDeclaration } from "@client/canvas/library/definition/diagramDefinition";
import type { DiagramModel, DiagramModelConnection, DiagramModelElement } from "@client/canvas/library/api/diagramModel";
import { compileNotation } from "@client/canvas/library/disl/compileNotation";
import { parseDisl } from "@client/canvas/library/disl/disTypes";
import { fakeContextConnection } from "@client/canvas/library/testing/canvasHarness";
import { TIMELINE_BINDINGS } from "./timelineBindings";

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
const { TIMELINE_DEFINITION } = await import("./TimelineCanvas");

const ROW_HEIGHT = 60;
const ELEMENT_HEIGHT = 36;

/** One canvas unit as a fraction of a row, for the hint's declared row arithmetic. */
const ROWS_PER_UNIT = 1 / ROW_HEIGHT;

/** How far above the box the drag hint sits, matching where the shared span drew it. */
const HINT_ABOVE = ELEMENT_HEIGHT / 2 + 8;

/*
 * The timeline's canvas definition, compiled from its bundled DISL specification, draws what the
 * definition the module stated by hand until the switch-over drew.
 *
 * The oracle below is that hand-written definition, moved here unchanged from `TimelineCanvas.tsx`.
 * The two are not equal key for key, and the differences are each a decision: the relation is called
 * by the specification's name (`connection` rather than `gates`); its source end names the two anchors
 * the specification's `connect.from` starts a relation from, which are all the anchors a node has; its
 * line states the arrowhead the oracle drew by default; a label binds its path rather than a template
 * of that one path, and leaves the default `inside` placement unsaid. The actions are the
 * specification's context menus' entries with their wire ids, where the oracle had local ids: the dead
 * Insert key the backend never answered is gone, a double-click renames as the specification's
 * `doubleClick` says, F2 on a relation relabels it, and the delete gesture is one action per target.
 * So the tests are the drawing, the declarations once those decisions are taken out, and the actions
 * as the specification states them.
 */
/**
 * The drag hint both element types show, declared once.
 *
 * <b>The one row in the tree whose text is arithmetic rather than a field</b>: the time under
 * the element's left edge, and the row its top would land on. Both read the LIVE bounds, which
 * is why `bounds` is a binding root at all - the model still holds the PRE-drag position while
 * this is on screen, so no payload could carry either number.
 */
const DRAG_HINT: LabelDeclaration = {
  // THE DRAG HINT, and the one row in the whole tree whose text is arithmetic rather
  // than a field: the time under the element's left edge, and the row its top would
  // land on. Both read the LIVE bounds, which is why `bounds` is a root at all - the
  // model still holds the PRE-drag position while this is on screen, so a payload
  // could not carry either of them.
  text: {
    parts: [
      {
        path: "bounds.left",
        number: { times: "payload.secondsPerUnit", plus: "payload.originSeconds", format: "yyyy-MM-ddTHH:mm:ss" },
      },
      {
        // `row N`: a literal word beside a computed number, which is why parts nest.
        parts: [
          { template: "row" },
          { path: "bounds.top", number: { times: ROWS_PER_UNIT, plus: "payload.originRows", round: "nearest" } },
        ],
        join: " ",
      },
    ],
    join: " · ",
  },
  when: { path: "state.dragging", is: "true" },
  offset: { x: 0, y: -HINT_ABOVE },
  className: "timeline-hint canvas-hint",
};


/**
 * THE ORACLE. What a timeline allows, stated once: periods that drag and resize, moments that drag,
 * either connecting to either from its begin or end anchor, with one bezier relation whose
 * empty release is itself a gesture - the create-and-relate the notation offers.
 *
 * Stated here rather than compiled from `definition/timeline.dis`: the span shape, the named
 * begin and end anchors, the loop-back route, the drag hint and the payload-driven day snap are
 * not in what `compileNotation` reads. `timelineDefinition.test.ts` holds it to what the bundled
 * specification states.
 */
const HAND_WRITTEN: DiagramDefinition = assertValidDiagramDefinition({
  elementTypes: [
    {
      id: "period",
      // The box the shared span drew, chosen by TYPE rather than by a payload flag: the
      // definition already had two types and the renderer decided between them again at draw
      // time, which is the duplication this migration removes.
      shape: "span",
      classNames: [{ className: "timeline-period canvas-node", on: "shape" }],
      labels: [
        {
          text: { template: "{element.label}" },
          placement: "inside",
          truncate: true,
          editable: true,
          className: "timeline-label canvas-node-label",
        },
        DRAG_HINT,
      ],
      anchors: {
        kind: "sides",
        fractions: [
          { side: "left", at: 0.5, name: "begin" },
          { side: "right", at: 0.5, name: "end" },
        ],
        edgeSides: "horizontal",
      },
      sizing: "user",
    },
    {
      id: "moment",
      // A diamond rather than a box - the built-in that exists because this row needed it.
      shape: "moment",
      classNames: [{ className: "timeline-moment", on: "shape" }],
      labels: [
        {
          text: { template: "{element.label}" },
          placement: "beside",
          editable: true,
          className: "timeline-label canvas-node-label",
        },
        DRAG_HINT,
      ],
      anchors: {
        kind: "sides",
        fractions: [
          { side: "left", at: 0.5, name: "begin" },
          { side: "right", at: 0.5, name: "end" },
        ],
        edgeSides: "horizontal",
      },
      sizing: "model",
    },
  ],
  relationTypes: [
    {
      id: "gates",
      // The loop is decided by geometry: a target beginning before the source ends gets the
      // forward-and-back curve, exactly as the interactive bezier drew it.
      route: {
        customRoute: "timeline-bezier",
        path: (from, to) => (to.x < from.x ? forwardBezierPath(from, to) : horizontalBezierPath(from, to)),
      },
      label: { placement: "midpoint", offset: -6, editable: true },
      className: "timeline-connection",
      lineClassName: "timeline-connection-line",
      hitClassName: "timeline-connection-hit",
      endpoints: {
        source: { elementTypes: ["period", "moment"] },
        target: { elementTypes: ["period", "moment"], anchors: "edge" },
        allowSelf: false,
      },
      emptyRelease: "complete",
    },
  ],
  // WHAT THIS TYPE OFFERS, AND WHAT INVOKES IT. The key list was hand-written in this canvas
  // and the delete was a keystroke it built to describe a gesture the library had already
  // handed it. Declared, the library derives the key set and dispatches an action id.
  actions: [
    { id: "rename", backendKey: "F2", invokedBy: [{ kind: "shortcut", key: "F2" }], appliesTo: [{ kind: "element" }] },
    { id: "insert", backendKey: "Insert", invokedBy: [{ kind: "shortcut", key: "Insert" }], appliesTo: [{ kind: "element" }] },
    { id: "add-right", backendKey: "Tab", invokedBy: [{ kind: "shortcut", key: "Tab" }], appliesTo: [{ kind: "element" }] },
    { id: "add-below", backendKey: "Enter", invokedBy: [{ kind: "shortcut", key: "Enter" }], appliesTo: [{ kind: "element" }] },
    { id: "delete", backendKey: "Delete", invokedBy: [{ kind: "gesture", gesture: "delete" }], appliesTo: [{ kind: "element" }, { kind: "connection" }] },
  ],
  // Where a dragged element comes to rest, said once so the drag shows what the drop sends: an
  // element's top on a row, and a date-only element's begin on the start of a day - where
  // TimelineScale.ToTime lands a date-only begin. The day lattice depends on the frozen scale,
  // so it rides each element's payload; an element with a time of day carries none and moves
  // freely in x, as its backend keeps its seconds.
  snap: {
    y: { step: ROW_HEIGHT },
    x: { step: { path: "payload.dayUnits" }, origin: { path: "payload.dayOriginUnits" } },
  },
  layout: { modes: ["manual"] },
  dragging: "enabled",
  // Arrange diagram and "Add … here" on empty canvas, from the backend's own list; the canvas
  // supplies where the click lands in seconds and rows, because the scale is frozen per canvas.
  backgroundMenu: true,
});

/** A period or a moment as the canvas builds it, with the payload the drag hint and the day snap read. */
function element(id: string, type: "period" | "moment", x: number, row: number, width: number, label: string, dateOnly: boolean): DiagramModelElement {
  return {
    id,
    type,
    x: x + width / 2,
    y: row * ROW_HEIGHT + ELEMENT_HEIGHT / 2,
    width,
    height: ELEMENT_HEIGHT,
    label,
    payload: {
      secondsPerUnit: 4320,
      originSeconds: 1767225600,
      originRows: -1,
      ...(dateOnly ? { dayUnits: 20, dayOriginUnits: -409080 } : {}),
    },
  };
}

/**
 * Every case the drawing distinguishes: a date-only period and one with a time of day, a moment, a
 * period named by its id for want of a label, relations forward and looping back, with a label and
 * without.
 */
const ELEMENTS: readonly DiagramModelElement[] = [
  element("aaa", "period", 100, 1, 780, "Discovery", true),
  element("bbb", "moment", 900, 3, 18, "Go", true),
  element("ccc", "period", 300, 2, 2, "ccc", false),
  element("ddd", "moment", 40, 0, 18, "A moment with a long name", false),
];

function connections(type: string): DiagramModelConnection[] {
  return [
    { id: "r1", type, sourceId: "aaa", targetId: "bbb", sourceAnchor: "end", targetAnchor: "begin", label: "gates" },
    { id: "r2", type, sourceId: "bbb", targetId: "ccc", sourceAnchor: "end", targetAnchor: "begin", label: "" },
    { id: "r3", type, sourceId: "ddd", targetId: "aaa", sourceAnchor: "end", targetAnchor: "begin", label: "loops? no: forward" },
  ];
}

/** The canvas's markup, with the ids React generates per mount taken out. */
function drawn(definition: DiagramDefinition, model: DiagramModel): string {
  const { container, unmount } = render(
    <DiagramCanvas
      definition={definition}
      model={model}
      events={{}}
      source={{ entryId: new Uint8Array(16), path: ["plan.tl"] }}
      ariaLabel="Timeline"
      className="timeline-surface canvas-viewport"
      scrollbarsClassName="timeline-scrollbars"
    />,
  );
  const markup = container.innerHTML.replace(/«r[0-9a-z]+»|:r[0-9a-z]+:/g, "«id»");
  unmount();
  return markup;
}

/** A route's drawing at a few points, forward and looping back, which is what makes two routes the same. */
function drawingOf(route: DiagramDefinition["relationTypes"][number]["route"]): unknown {
  const ref = route as CustomRouteRef;
  const points = [[{ x: 0, y: 0 }, { x: 200, y: 60 }], [{ x: 300, y: 120 }, { x: 100, y: 0 }]] as const;
  return { name: ref.customRoute, paths: points.map(([from, to]) => ref.path(from, to, [])) };
}

/** The compiled definition with the decisions the opening comment names taken back out. */
function withoutDecisions(definition: DiagramDefinition): unknown {
  const relation = definition.relationTypes[0]!;
  return {
    ...definition,
    actions: undefined,
    elementTypes: definition.elementTypes.map((type) => ({
      ...type,
      labels: type.labels?.map((label: LabelDeclaration) => {
        const text = "path" in label.text && label.text.path === "element.label" ? { template: "{element.label}" } : label.text;
        return { ...label, text, ...(label.className === "timeline-label canvas-node-label" && label.placement === undefined ? { placement: "inside" } : {}) };
      }),
    })),
    relationTypes: [{
      ...relation,
      id: "gates",
      route: drawingOf(relation.route),
      // The library's default line ends in an arrow, which is what the oracle drew by stating no style.
      style: relation.style?.endMarker === "arrow" && relation.style.startMarker === undefined ? undefined : relation.style,
      endpoints: { ...relation.endpoints, source: { elementTypes: relation.endpoints.source.elementTypes } },
    }],
  };
}

describe("the timeline's compiled definition", () => {
  it("draws what the hand-written definition drew, element for element", () => {
    const compiled = drawn(TIMELINE_DEFINITION, { elements: ELEMENTS, connections: connections(TIMELINE_DEFINITION.relationTypes[0]!.id) });

    // The case the comparison would pass vacuously on: a canvas that drew nothing for either.
    expect(compiled).toContain("timeline-period");
    expect(compiled).toContain("timeline-moment");
    expect(compiled).toContain("timeline-connection-hit");
    expect(compiled).toContain("gates");
    expect(compiled).toBe(drawn(HAND_WRITTEN, { elements: ELEMENTS, connections: connections("gates") }));
  });

  it("declares what the hand-written definition declared, but for the decisions it names", () => {
    const oracle = { ...HAND_WRITTEN, actions: undefined, relationTypes: HAND_WRITTEN.relationTypes.map((relation) => ({ ...relation, route: drawingOf(relation.route) })) };

    expect(withoutDecisions(TIMELINE_DEFINITION)).toEqual(oracle);
  });

  it("offers the specification's actions, each sending the key its menu entry writes", () => {
    expect(TIMELINE_DEFINITION.actions).toEqual([
      { id: "timeline.rename", backendKey: "F2", invokedBy: [{ kind: "shortcut", key: "F2" }, { kind: "gesture", gesture: "activate" }], appliesTo: [{ kind: "element" }] },
      { id: "timeline.remove", backendKey: "Delete", invokedBy: [{ kind: "gesture", gesture: "delete" }], appliesTo: [{ kind: "element" }] },
      { id: "timeline.disconnect", backendKey: "Delete", invokedBy: [{ kind: "gesture", gesture: "delete" }], appliesTo: [{ kind: "connection" }] },
      { id: "timeline.add-after", backendKey: "Tab", invokedBy: [{ kind: "shortcut", key: "Tab" }], appliesTo: [{ kind: "element" }] },
      { id: "timeline.add-below", backendKey: "Enter", invokedBy: [{ kind: "shortcut", key: "Enter" }], appliesTo: [{ kind: "element" }] },
      { id: "timeline.relabel", backendKey: "F2", invokedBy: [{ kind: "shortcut", key: "F2" }], appliesTo: [{ kind: "connection" }] },
    ]);
    // Every key the oracle sent but the one no backend action answered.
    const sent = new Set(TIMELINE_DEFINITION.actions!.map((action) => action.backendKey));
    expect((HAND_WRITTEN.actions ?? []).map((action) => action.backendKey).filter((key) => !sent.has(key))).toEqual(["Insert"]);
  });

  it("is what the canvas draws: the module's definition is compiled from the bundled specification", () => {
    // The canary for the tests above: were the canvas still holding its own definition, they would
    // compare the oracle with a copy of itself.
    expect(TIMELINE_DEFINITION).toEqual(assertValidDiagramDefinition(compileNotation(parseDisl(disText), TIMELINE_BINDINGS)));
  });
});
