import { describe, expect, it, vi } from "vitest";
import { render } from "@testing-library/react";
import {
  facingAnchorsBetween,
  forwardBezierPath,
  horizontalBezierPath,
  sideAnchorOf,
  type ConnectorBox,
} from "@client/canvas/connectors";
import disText from "../definition/dependency-graph.dis?raw";
import { assertValidDiagramDefinition } from "@client/canvas/library/definition/validateDiagramDefinition";
import type { CustomRouteRef, DiagramDefinition, LabelDeclaration, ShapeBounds } from "@client/canvas/library/definition/diagramDefinition";
import type { DiagramModel, DiagramModelConnection, DiagramModelElement } from "@client/canvas/library/api/diagramModel";
import { compileNotation } from "@client/canvas/library/disl/compileNotation";
import { parseDisl } from "@client/canvas/library/disl/disTypes";
import { fakeContextConnection } from "@client/canvas/library/testing/canvasHarness";
import { DEPENDENCY_GRAPH_BINDINGS } from "./dependencyGraphBindings";

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
const { DEPENDENCY_GRAPH_DEFINITION } = await import("./DependencyGraphCanvas");

const ROW_HEIGHT = 60;
const NODE_WIDTH = 160;
const NODE_HEIGHT = 36;

/*
 * The dependency graph's canvas definition, compiled from its bundled DISL specification, draws what
 * the definition the module stated by hand until the switch-over drew.
 *
 * The oracle below is that hand-written definition, moved here unchanged from
 * `DependencyGraphCanvas.tsx`. The two are not equal key for key, and the differences are each a
 * decision: the relation is called by the specification's name (`dependson` rather than `depends`); a
 * label binds its path rather than a template of that one path, and leaves the default `inside`
 * placement unsaid; and the actions are the specification's context menus' entries with their wire
 * ids, declared for the whole diagram where the oracle declared them on the node type - the dead
 * Insert key the backend never answered is gone, a double-click renames as the specification's
 * `doubleClick` says, F2 on a dependency relabels it, and the delete gesture is one action per target.
 * One is new: the specification offers "Add node here" on empty canvas, so a right-click there opens
 * that menu, placing the node on the row nearest the click. So the tests are the drawing, the
 * declarations once those decisions are taken out, and the actions as the specification states them.
 */
/** A corner-based bounds as the centre-based connector geometry wants it. */
function boxOf(bounds: ShapeBounds): ConnectorBox {
  return {
    x: bounds.x + bounds.width / 2,
    y: bounds.y + bounds.height / 2,
    width: bounds.width,
    height: bounds.height,
  };
}

/**
 * The dependency curve, exactly as `InteractiveBezierConnection` drew it: the facing bezier
 * when the dependency sits clear to the right, and the forward loop - out of the dependent's
 * right, back into the dependency's left - when it sits behind. The loop is decided from the
 * endpoint BOUNDS, which the connect preview does not have yet; the preview draws the plain
 * horizontal bezier to the pointer.
 */
const dependencyRoute: CustomRouteRef = {
  customRoute: "dependency-bezier",
  path: (from, to, _waypoints, ends) => {
    if (!ends) {
      return horizontalBezierPath(from, to);
    }

    const fromBox = boxOf(ends.source);
    const toBox = boxOf(ends.target);
    const loopsBack = ends.target.x < ends.source.x + ends.source.width;
    const [a, b] = loopsBack
      ? [sideAnchorOf(fromBox, "right"), sideAnchorOf(toBox, "left")]
      : facingAnchorsBetween(fromBox, toBox);
    return loopsBack ? forwardBezierPath(a, b) : horizontalBezierPath(a, b);
  },
};

/**
 * THE ORACLE. What a dependency graph allows, stated once: nodes that drag freely on x and land on rows,
 * with the two side anchors a dependency gesture lifts from - which side decides the edge's
 * direction on release - and one directed depends-on relation whose release over empty
 * canvas is the create-and-relate gesture. Labels edit inline on nodes and relations alike.
 */
const HAND_WRITTEN: DiagramDefinition = assertValidDiagramDefinition({
  elementTypes: [
    {
      id: "node",
      // THE SHAPE THIS MODULE USED TO DRAW ITSELF. `span` is the same shared component the
      // custom renderer wrapped - the renderer existed to add three classes and an edge rule,
      // both of which are declarations now.
      shape: "span",
      classNames: [
        { className: "dependency-graph-element canvas-element" },
        { className: "dependency-graph-node canvas-node" },
      ],
      labels: [
        {
          // `label || id`, as the renderer wrote it: the element's own label, falling back to
          // its id when the document names none.
          text: { template: "{element.label}" },
          placement: "inside",
          truncate: true,
          editable: true,
          className: "dependency-graph-label canvas-node-label",
        },
      ],
      // WHAT THIS TYPE OFFERS, AND WHAT INVOKES IT. The four keys were a hand-written list in
      // this canvas and the delete was a keystroke it built by hand to describe a gesture the
      // library had already handed it. Declared, the library derives the key set and dispatches
      // an action id; the backend still holds the key-to-action table, so each declaration names
      // the key it travels as in `backendKey`, and the library sends that key itself.
      actions: [
        { id: "rename", backendKey: "F2", invokedBy: [{ kind: "shortcut", key: "F2" }], appliesTo: [{ kind: "element" }] },
        { id: "insert", backendKey: "Insert", invokedBy: [{ kind: "shortcut", key: "Insert" }], appliesTo: [{ kind: "element" }] },
        { id: "add-right", backendKey: "Tab", invokedBy: [{ kind: "shortcut", key: "Tab" }], appliesTo: [{ kind: "element" }] },
        { id: "add-below", backendKey: "Enter", invokedBy: [{ kind: "shortcut", key: "Enter" }], appliesTo: [{ kind: "element" }] },
        { id: "delete", backendKey: "Delete", invokedBy: [{ kind: "gesture", gesture: "delete" }], appliesTo: [{ kind: "element" }, { kind: "connection" }] },
      ],
      anchors: {
        kind: "sides",
        fractions: [
          { side: "left", at: 0.5, name: "left" },
          { side: "right", at: 0.5, name: "right" },
        ],
        // The other half of what `nodeShape.edgePoint` did: a target end attaches on the side
        // facing its source rather than by true edge intersection.
        edgeSides: "horizontal",
      },
      sizing: "model",
    },
  ],
  relationTypes: [
    {
      id: "depends",
      route: dependencyRoute,
      style: { endMarker: "arrow" },
      label: { placement: "midpoint", offset: -6, editable: true },
      className: "dependency-graph-relation",
      lineClassName: "dependency-graph-relation-line",
      hitClassName: "dependency-graph-relation-hit",
      endpoints: {
        source: { elementTypes: ["node"], anchors: ["left", "right"] },
        // `anchors: "edge"` with the type's own horizontal constraint: a connector reaches the
        // side facing its other end, whatever the angle, because this notation reads
        // left-to-right and an edge through the top of a box reads as a different relation.
        target: { elementTypes: ["node"], anchors: "edge" },
        allowSelf: false,
      },
      emptyRelease: "complete",
    },
  ],
  // The rows this diagram has always had, said to the library so the drag shows what the drop
  // sends: a node's top comes to rest on a row, which is where this canvas draws a row's node.
  snap: { y: { step: ROW_HEIGHT } },
  layout: { modes: ["manual"] },
  dragging: "enabled",
});

function node(id: string, x: number, row: number, label: string): DiagramModelElement {
  return { id, type: "node", x: x + NODE_WIDTH / 2, y: row * ROW_HEIGHT + NODE_HEIGHT / 2, width: NODE_WIDTH, height: NODE_HEIGHT, label };
}

/**
 * Every case the drawing distinguishes: a node named by its id, a long label, a dependency forward, one
 * looping back from well behind and one from a node that overlaps its source, labelled and not.
 */
const ELEMENTS: readonly DiagramModelElement[] = [
  node("aaa", 0, 0, "Api gateway"),
  node("bbb", 320, 1, "bbb"),
  node("ccc", 120, 2, "A node with a label far too long for its box"),
  node("ddd", 80, 3, "Overlaps"),
];

function connections(type: string): DiagramModelConnection[] {
  return [
    { id: "r1", type, sourceId: "aaa", targetId: "bbb", label: "calls" },
    { id: "r2", type, sourceId: "bbb", targetId: "ccc" },
    { id: "r3", type, sourceId: "ccc", targetId: "aaa", label: "publishes to" },
    { id: "r4", type, sourceId: "aaa", targetId: "ddd" },
  ];
}

/** The canvas's markup, with the ids React generates per mount taken out. */
function drawn(definition: DiagramDefinition, model: DiagramModel): string {
  const { container, unmount } = render(
    <DiagramCanvas
      definition={definition}
      model={model}
      events={{}}
      source={{ entryId: new Uint8Array(16), path: ["graph.dgr"] }}
      ariaLabel="Dependency graph"
      className="dependency-graph-surface"
      scrollbarsClassName="dependency-graph-scrollbars"
    />,
  );
  const markup = container.innerHTML.replace(/«r[0-9a-z]+»|:r[0-9a-z]+:/g, "«id»");
  unmount();
  return markup;
}

/** A route's drawing between two boxes, forward and looping back, which is what makes two routes the same. */
function drawingOf(route: DiagramDefinition["relationTypes"][number]["route"]): unknown {
  const ref = route as CustomRouteRef;
  const box = (x: number, y: number) => ({ x, y, width: NODE_WIDTH, height: NODE_HEIGHT });
  const cases = [[box(0, 0), box(320, 60)], [box(320, 60), box(0, 120)], [box(0, 0), box(80, 180)]] as const;
  return {
    name: ref.customRoute,
    preview: ref.path({ x: 0, y: 0 }, { x: 200, y: 60 }, []),
    paths: cases.map(([source, target]) => ref.path({ x: 0, y: 0 }, { x: 0, y: 0 }, [], { source, target })),
  };
}

/** The compiled definition with the decisions the opening comment names taken back out. */
function withoutDecisions(definition: DiagramDefinition): unknown {
  const relation = definition.relationTypes[0]!;
  const { actions: _actions, backgroundMenu: _menu, backgroundPlacement: _placement, ...rest } = definition;
  return {
    ...rest,
    elementTypes: definition.elementTypes.map((type) => ({
      ...type,
      labels: type.labels?.map((label: LabelDeclaration) => ({
        ...label,
        text: "path" in label.text && label.text.path === "element.label" ? { template: "{element.label}" } : label.text,
        ...(label.placement === undefined ? { placement: "inside" } : {}),
      })),
    })),
    relationTypes: [{ ...relation, id: "depends", route: drawingOf(relation.route) }],
  };
}

describe("the dependency graph's compiled definition", () => {
  it("draws what the hand-written definition drew, element for element", () => {
    const compiled = drawn(DEPENDENCY_GRAPH_DEFINITION, { elements: ELEMENTS, connections: connections(DEPENDENCY_GRAPH_DEFINITION.relationTypes[0]!.id) });

    // The case the comparison would pass vacuously on: a canvas that drew nothing for either.
    expect(compiled).toContain("dependency-graph-node");
    expect(compiled).toContain("dependency-graph-relation-hit");
    expect(compiled).toContain("publishes to");
    expect(compiled).toBe(drawn(HAND_WRITTEN, { elements: ELEMENTS, connections: connections("depends") }));
  });

  it("declares what the hand-written definition declared, but for the decisions it names", () => {
    const oracle = {
      ...HAND_WRITTEN,
      elementTypes: HAND_WRITTEN.elementTypes.map(({ actions: _actions, ...type }) => type),
      relationTypes: HAND_WRITTEN.relationTypes.map((relation) => ({ ...relation, route: drawingOf(relation.route) })),
    };

    expect(withoutDecisions(DEPENDENCY_GRAPH_DEFINITION)).toEqual(oracle);
  });

  it("offers the specification's actions, each sending the key its menu entry writes", () => {
    expect(DEPENDENCY_GRAPH_DEFINITION.actions).toEqual([
      { id: "dependencies.rename", backendKey: "F2", invokedBy: [{ kind: "shortcut", key: "F2" }, { kind: "gesture", gesture: "activate" }], appliesTo: [{ kind: "element" }] },
      { id: "dependencies.remove", backendKey: "Delete", invokedBy: [{ kind: "gesture", gesture: "delete" }], appliesTo: [{ kind: "element" }] },
      { id: "dependencies.disconnect", backendKey: "Delete", invokedBy: [{ kind: "gesture", gesture: "delete" }], appliesTo: [{ kind: "connection" }] },
      { id: "dependencies.add-after", backendKey: "Tab", invokedBy: [{ kind: "shortcut", key: "Tab" }], appliesTo: [{ kind: "element" }] },
      { id: "dependencies.add-below", backendKey: "Enter", invokedBy: [{ kind: "shortcut", key: "Enter" }], appliesTo: [{ kind: "element" }] },
      { id: "dependencies.relabel", backendKey: "F2", invokedBy: [{ kind: "shortcut", key: "F2" }], appliesTo: [{ kind: "connection" }] },
    ]);
    // Every key the oracle sent but the one no backend action answered.
    const sent = new Set(DEPENDENCY_GRAPH_DEFINITION.actions!.map((action) => action.backendKey));
    const oracleKeys = HAND_WRITTEN.elementTypes.flatMap((type) => type.actions ?? []).map((action) => action.backendKey);
    expect(oracleKeys.filter((key) => !sent.has(key))).toEqual(["Insert"]);
  });

  it("opens the background menu on empty canvas, placing a node on the row nearest the click", () => {
    expect(HAND_WRITTEN.backgroundMenu).toBeUndefined();
    expect(DEPENDENCY_GRAPH_DEFINITION.backgroundMenu).toBe(true);
    expect(DEPENDENCY_GRAPH_DEFINITION.backgroundPlacement?.({ x: 412.5, y: 89 })).toEqual({ x: 412.5, y: 1 });
    expect(DEPENDENCY_GRAPH_DEFINITION.backgroundPlacement?.({ x: -3, y: -91 })).toEqual({ x: -3, y: -2 });
  });

  it("is what the canvas draws: the module's definition is compiled from the bundled specification", () => {
    // The canary for the tests above: were the canvas still holding its own definition, they would
    // compare the oracle with a copy of itself.
    expect(DEPENDENCY_GRAPH_DEFINITION).toEqual(assertValidDiagramDefinition(compileNotation(parseDisl(disText), DEPENDENCY_GRAPH_BINDINGS)));
  });
});
